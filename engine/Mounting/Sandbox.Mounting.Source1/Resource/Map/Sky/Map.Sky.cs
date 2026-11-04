using System;
using System.IO;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private static readonly string[] SkySuffixes = ["rt", "lf", "bk", "ft", "up", "dn"];
	private Material _skyMaterial;
	private readonly record struct SkyFace( string Path, int Frame, VtfHeader Header, bool CompressedHdr, Vector3 Tint, Matrix Transform );
	private sealed partial class EntityBuilder
	{
		public void BuildSky( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) )
				name = "sky_day03_02";
			try
			{
				owner._skyMaterial ??= owner.CreateSkyMaterial( name );
				var sky = new GameObject( false, "skybox" )
				{
					Parent = World,
					IsStatic = true
				};
				var component = sky.AddComponent<SkyBox2D>();
				component.SkyIndirectLighting = false;
				component.SkyMaterial = owner._skyMaterial;
				sky.Enabled = true;
			}
			catch ( Exception e ) when ( e is FileNotFoundException or InvalidDataException or NotSupportedException or EndOfStreamException )
			{
				warnings.Add( $"Sky '{name}' was not imported: {e.Message}" );
			}
		}
	}

	private Material CreateSkyMaterial( string name )
	{
		var faces = new SkyFace[6];
		var size = 0;
		for ( var i = 0; i < faces.Length; i++ )
		{
			faces[i] = ReadSkyFace( $"skybox/{name}{SkySuffixes[i]}" );
			size = Math.Max( size, Math.Max( faces[i].Header.Width, faces[i].Header.Height ) );
		}

		var pixels = new byte[checked(size * size * 6 * 8)];
		for ( var i = 0; i < faces.Length; i++ )
		{
			var face = faces[i];
			var source = Host.GetSourceTexture( face.Path, ContentRoot );
			var vtf = source.ReadFrame( face.Frame );
			var colors = ReadSkyPixels( vtf, face.Frame, face.CompressedHdr, face.Tint );
			WriteSkyFace( MemoryMarshal.Cast<byte, Half>( pixels.AsSpan( i * size * size * 8, size * size * 8 ) ), size, colors, face, i );
		}

		var resourceName = $"source1/{Host.Ident}/{System.IO.Path.ChangeExtension( FullPath, ".sky" )}";
		var texture = Texture.CreateCube( size, size, ImageFormat.RGBA16161616F ).WithName( resourceName ).WithData( pixels ).Finish();
		if ( texture is null || !texture.IsValid || texture.IsError )
			throw new InvalidOperationException( $"{FullPath}: could not create sky texture." );
		var material = Material.Create( $"{resourceName}.vmat", "shaders/sky.shader", false ) ?? throw new InvalidOperationException( $"{FullPath}: could not create sky material." );
		material.Set( "g_tSkyTexture", texture );
		material.Set( "SkyTexture", texture );
		return material;
	}

	private SkyFace ReadSkyFace( string name )
	{
		var path = VmtFile.NormalizePath( name, ".vmt" );
		var vmt = VmtFile.Load( path, file => Host.OpenContentFile( file, ContentRoot ),
			symbol => symbol.Equals( "hdr", StringComparison.OrdinalIgnoreCase ) ? true : null,
			diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
		var shader = VmtFile.NormalizeShaderName( vmt.ShaderName );
		if ( shader is not ("sky" or "sky_hdr" or "unlitgeneric") )
			throw new NotSupportedException( $"{path}: unsupported sky shader '{vmt.ShaderName}'." );
		var parameters = new VmtParameters( VmtFile.SelectParameters( vmt ), path );
		var hdr = shader != "unlitgeneric";
		var compressed = hdr ? parameters.String( "$hdrcompressedtexture" ) : null;
		string texture;
		if ( hdr && parameters.Contains( "$hdrcompressedtexture0" ) && string.IsNullOrWhiteSpace( compressed ) )
		{
			Log.Warning( $"{path}: legacy three-texture HDR sky is unsupported; using its LDR texture." );
			texture = parameters.String( "$basetexture" );
		}
		else
		{
			texture = compressed ?? (hdr ? parameters.String( "$hdrbasetexture" ) : null) ?? parameters.String( "$basetexture" );
		}

		var texturePath = VmtFile.NormalizePath( texture, ".vtf" );
		var source = Host.GetSourceTexture( texturePath, ContentRoot );
		var frame = parameters.Integer( "$frame" );
		if ( source.FaceCount != 1 || source.Depth != 1 || (uint)frame >= source.FrameCount )
			throw new InvalidDataException( $"{texturePath}: sky faces require a 2D texture and a valid frame." );
		if ( vmt.Root.GetBlock( "proxies" ) is not null )
			Log.Warning( $"{path}: sky proxies are not imported; using authored static values." );
		return new SkyFace( texturePath, frame, source.Header, !string.IsNullOrWhiteSpace( compressed ), parameters.Vector( "$color", Vector3.One ), parameters.Transform( "$basetexturetransform" ) );
	}
}
