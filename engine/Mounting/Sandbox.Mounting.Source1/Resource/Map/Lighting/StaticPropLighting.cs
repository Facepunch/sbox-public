using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Source1;

internal sealed class StaticPropLighting( string name, string contentRoot, VtxFile vtx, Dictionary<Material, Material> vertexLitMaterials )
{
	public string Name { get; } = name;
	public string ContentRoot { get; } = contentRoot;
	public SourceMaterialOverrides BakedShadowBindings { get; init; }

	private readonly Dictionary<(int BodyPart, int Model, int Lod, int Mesh), Vector3[]> colors = [];
	private readonly Dictionary<(int BodyPart, int Model, int Lod, int Mesh), Texture> textures = [];
	private PropLightingState fallback;
	private Transform worldTransform;
	private float? directionalDot;
	private bool vertexLit;
	public bool HasLighting => colors.Count > 0 || textures.Count > 0 || fallback != null;

	public void SetFallback( PropLightingState state, Transform transform, float? directional, bool vertices )
	{
		fallback = state;
		worldTransform = transform;
		directionalDot = directional;
		vertexLit = vertices;
	}

	public void ReadVertices( BaseMount mount, string filename, int checksum )
	{
		var found = mount.TryOpenContentFile( $"{ContentRoot}{filename}", out var stream );
		using ( stream )
		{
			if ( !found )
				return;
			try
			{
				var file = new VhvFile( stream, checksum );
				if ( file.Meshes.Length != vtx.LightingMeshes.Count )
					throw new InvalidDataException( "VHV and VTX hardware mesh counts do not match." );
				var mapped = MapVertexColors( file, vtx.LightingMeshes );
				foreach ( var entry in mapped )
					colors.Add( entry.Key, entry.Value );
			}
			catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException or OverflowException )
			{
				Log.Warning( $"{ContentRoot}{filename}: baked vertex lighting was not imported: {e.Message}" );
			}
		}
	}

	public bool ReadTexels( BaseMount mount, string filename, int checksum )
	{
		var found = mount.TryOpenContentFile( $"{ContentRoot}{filename}", out var stream );
		using ( stream )
		{
			if ( !found )
				return false;
			try
			{
				var file = new PplFile( stream, checksum );
				var meshes = MapTexels( file, vtx.LightingMeshes );
				foreach ( var (key, mesh) in meshes )
				{
					if ( key.Item3 >= vtx.RenderLodCount )
						continue;
					textures.Add( key, CreateLightmapTexture( mesh, file.Format ) );
				}
			}
			catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException or OverflowException )
			{
				textures.Clear();
				Log.Warning( $"{ContentRoot}{filename}: baked texel lighting was not imported: {e.Message}" );
				return false;
			}
		}

		return true;
	}

	internal static Dictionary<(int BodyPart, int Model, int Lod, int Mesh), PplFile.Mesh> MapTexels( PplFile file, IReadOnlyList<VtxFile.LightingMesh> meshes )
	{
		if ( file.Meshes.Length != meshes.Count )
			throw new InvalidDataException( "PPL and VTX hardware mesh counts do not match." );
		var result = new Dictionary<(int, int, int, int), PplFile.Mesh>();
		for ( int i = 0; i < meshes.Count; i++ )
		{
			var source = meshes[i];
			var mesh = file.Meshes[i];
			var key = (source.BodyPart, source.Model, source.Lod, source.Mesh);
			if ( result.TryGetValue( key, out var previous ) && (previous.Width != mesh.Width || previous.Height != mesh.Height || !previous.Pixels.AsSpan().SequenceEqual( mesh.Pixels )) )
				throw new InvalidDataException( "PPL strip groups disagree on their mesh lightmap." );
			result[key] = mesh;
		}

		return result;
	}

	internal static Dictionary<(int BodyPart, int Model, int Lod, int Mesh), Vector3[]> MapVertexColors( VhvFile file, IReadOnlyList<VtxFile.LightingMesh> meshes )
	{
		var result = new Dictionary<(int, int, int, int), Vector3[]>();
		if ( file.Meshes.Length != meshes.Count )
			throw new InvalidDataException( "VHV and VTX hardware mesh counts do not match." );
		for ( int i = 0; i < meshes.Count; i++ )
		{
			var source = meshes[i];
			var mesh = file.Meshes[i];
			if ( mesh.Lod != source.Lod || mesh.VertexCount != source.Vertices.Length )
				throw new InvalidDataException( "VHV and VTX hardware mesh vertices or LODs do not match." );
			var key = (source.BodyPart, source.Model, source.Lod, source.Mesh);
			int count = source.Vertices.Length == 0 ? 0 : source.Vertices.Max() + 1;
			if ( !result.TryGetValue( key, out var vertices ) )
			{
				vertices = new Vector3[count];
				Array.Fill( vertices, new Vector3( float.NaN ) );
			}
			else if ( vertices.Length < count )
			{
				int previous = vertices.Length;
				Array.Resize( ref vertices, count );
				Array.Fill( vertices, new Vector3( float.NaN ), previous, count - previous );
			}

			for ( int vertex = 0; vertex < source.Vertices.Length; vertex++ )
			{
				int original = source.Vertices[vertex];
				var color = mesh.GetColor( vertex );
				if ( float.IsFinite( vertices[original].x ) && vertices[original] != color )
					throw new InvalidDataException( "VHV strip groups disagree on their original vertex lighting." );
				vertices[original] = color;
			}

			result[key] = vertices;
		}

		return result;
	}

	public Vector3 GetColor( (int BodyPart, int Model, int Lod, int Mesh) key, int vertex, Vector3 position, Vector3 normal )
	{
		if ( fallback != null && vertexLit )
			return fallback.VertexColor( worldTransform.PointToWorld( position ), worldTransform.Rotation * normal, directionalDot );
		if ( !colors.TryGetValue( key, out var vertices ) )
			return Vector3.Zero;
		if ( (uint)vertex >= vertices.Length || !float.IsFinite( vertices[vertex].x ) )
			throw new InvalidDataException( "A rendered VTX vertex has no baked VHV lighting." );
		return vertices[vertex];
	}

	public Material CreateMaterial( BaseMount mount, Material original, int slot, (int BodyPart, int Model, int Lod, int Mesh) key, int skin )
	{
		if ( !SourceMaterialBuilder.IsLitSurfaceShader( original ) )
			return original;
		int mode = textures.ContainsKey( key ) ? 2 : colors.ContainsKey( key ) ? 1 : fallback != null ? vertexLit ? 1 : 3 : 0;
		if ( mode == 0 )
			return original;
		if ( mode == 1 && vertexLitMaterials.TryGetValue( original, out var cached ) )
			return cached;

		var bindings = BakedShadowBindings?.Snapshot() ?? new SourceMaterialOverrides();
		bindings.Features["F_SOURCE_PROP_LIGHTING"] = 1;
		bindings.Integers["g_nSourcePropLighting"] = mode;
		if ( mode == 2 )
			bindings.Textures["g_tSourcePropLightmap"] = textures[key];
		if ( mode == 3 )
			fallback.AddMaterialBindings( bindings );
		var material = mount.CreateMaterialVariant( original, $"{System.IO.Path.ChangeExtension( Name, null )}_body{key.BodyPart}_model{key.Model}_lod{key.Lod}_mesh{key.Mesh}_slot{slot}_skin{skin}.vmat", bindings );
		if ( mode == 1 )
			vertexLitMaterials.Add( original, material );
		return material;
	}

	private static Texture CreateLightmapTexture( PplFile.Mesh mesh, VtfFormat sourceFormat )
	{
		var (format, convert) = TextureLoader.GetImageFormat( sourceFormat );
		if ( format == ImageFormat.None )
			throw new NotSupportedException( $"Unsupported PPL texture format {sourceFormat}." );
		bool decompress = sourceFormat is VtfFormat.DXT1 or VtfFormat.DXT1_ONEBITALPHA or VtfFormat.DXT3 or VtfFormat.DXT5;
		if ( decompress )
			format = ImageFormat.RGBA8888;
		var mipData = new byte[mesh.MipCount][];
		int offset = 0;
		for ( int mip = 0; mip < mesh.MipCount; mip++ )
		{
			int width = Math.Max( 1, mesh.Width >> mip );
			int height = Math.Max( 1, mesh.Height >> mip );
			int size = VtfFile.GetImageDataSize( width, height, 1, 1, sourceFormat );
			var pixels = mesh.Pixels.AsSpan( offset, size );
			var destination = new byte[convert || decompress ? checked(width * height * (format == ImageFormat.RGBA32323232F ? 16 : 4)) : size];
			if ( decompress )
				VtfFile.DecompressDxt( pixels, destination, sourceFormat, width, height );
			else if ( convert )
				VtfFile.ConvertPixels( pixels, destination, sourceFormat );
			else
				pixels.CopyTo( destination );
			mipData[mesh.MipCount - mip - 1] = destination;
			offset += size;
		}

		var texture = new Texture2DBuilder().WithSize( mesh.Width, mesh.Height ).WithFormat( format ).WithData( mipData.SelectMany( mip => mip ).ToArray() ).WithMips( mesh.MipCount ).WithStaticUsage().Finish();
		if ( texture == null || !texture.IsValid || texture.IsError )
			throw new InvalidDataException( "PPL lightmap texture could not be created." );
		return texture;
	}
}
