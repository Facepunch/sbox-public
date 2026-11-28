using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// A model's geometry read back for the lightmap baker. A model has no lightmap coordinates of its own, so a lightmapped
/// renderer draws an unwrapped copy of its geometry that does.
/// </summary>
internal static class LightmapGeometry
{
	/// <summary>
	/// One draw call, in model space. Unwrapped, it has lightmap coordinates and a vertex on a chart's seam is here once for each chart.
	/// </summary>
	internal sealed record Draw( Material Material, Vertex[] Vertices, int[] Indices, bool ModelStreams, Vector2[] Uvs = null );

	[StructLayout( LayoutKind.Sequential )]
	internal struct PlainVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 3 )] public Vector2 LightmapUv;
	}

	// A stream the model never had would light up a shader path it doesn't take, so only models with them get these
	[StructLayout( LayoutKind.Sequential )]
	struct ModelVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 Texcoord;
		[VertexLayout.TexCoord( 1 )] public Vector2 Texcoord1;
		[VertexLayout.TexCoord( 3 )] public Vector2 LightmapUv;
		[VertexLayout.Color] public Color32 Color;
	}

	// The last unwrap of each body and scale of a model, which every instance like it shares
	static readonly ConditionalWeakTable<Model, ConcurrentDictionary<(ulong, int, float, float, float), (float LuxelSize, Draw[] Draws)>> cache = new();

	// A model's charts at each body and scale, the same whatever size a luxel is
	static readonly ConditionalWeakTable<Model, ConcurrentDictionary<(ulong, int, float, float, float), LightmapUnwrap.Charts>> charts = new();

	static LightmapUnwrap.Charts Charts( Model model, ulong bodyGroups, int lod, Vector3 scale ) => charts.GetOrCreateValue( model ).GetOrAdd( (bodyGroups, lod, scale.x, scale.y, scale.z), _ =>
	{
		var corners = (Read( model, bodyGroups, lod ) ?? []).SelectMany( x => x.Indices.Select( i => x.Vertices[i].Position * scale ) ).ToArray();
		return corners.Length > 0 ? LightmapUnwrap.Split( corners ) : null;
	} );

	/// <summary>
	/// The size <see cref="Unwrap"/> would fill at a luxel size, without building anything. Zero if it would be null.
	/// </summary>
	public static Vector2Int Size( Model model, ulong bodyGroups, int lod, Vector3 scale, float luxelSize )
		=> Charts( model, bodyGroups, lod, scale ) is { } split ? LightmapUnwrap.Size( split, luxelSize ) : default;

	/// <summary>
	/// Unwrap the draw calls a renderer's body groups and LOD draw, for luxels of a size in world units at a scale: its
	/// draws with lightmap coordinates in luxels from the corner of its own charts. Null if the model has nothing to draw,
	/// or any of it can't be read.
	/// </summary>
	public static Draw[] Unwrap( Model model, ulong bodyGroups, int lod, Vector3 scale, float luxelSize )
	{
		var unwraps = cache.GetOrCreateValue( model );
		var key = (bodyGroups, lod, scale.x, scale.y, scale.z);

		if ( unwraps.TryGetValue( key, out var cached ) && cached.LuxelSize == luxelSize )
			return cached.Draws;

		if ( Charts( model, bodyGroups, lod, scale ) is not { } layout )
			return null;

		var sources = Read( model, bodyGroups, lod );
		var uvs = LightmapUnwrap.Pack( layout, luxelSize, out var size );
		var draws = new Draw[sources.Length];
		var corner = 0;

		for ( int i = 0; i < sources.Length; i++ )
		{
			var source = sources[i];
			var seen = new Dictionary<(int, int, int), int>();
			var vertices = new List<Vertex>();
			var vertexUvs = new List<Vector2>();
			var indices = new int[source.Indices.Length];

			for ( int k = 0; k < indices.Length; k++, corner++ )
			{
				var uv = uvs[corner];
				var split = (source.Indices[k], BitConverter.SingleToInt32Bits( uv.x ), BitConverter.SingleToInt32Bits( uv.y ));

				if ( !seen.TryGetValue( split, out var index ) )
				{
					seen[split] = index = vertices.Count;
					vertices.Add( source.Vertices[source.Indices[k]] );
					vertexUvs.Add( uv );
				}

				indices[k] = index;
			}

			draws[i] = new Draw( source.Material, vertices.ToArray(), indices, source.ModelStreams, vertexUvs.ToArray() );
		}

		return (unwraps[key] = (luxelSize, draws)).Draws;
	}

	// Read back from the GPU once, every instance like it and unwraps at other luxel sizes reuse it
	static readonly ConditionalWeakTable<Model, ConcurrentDictionary<(ulong, int), Draw[]>> reads = new();

	/// <summary>
	/// Drop what was read from a model. A reloaded one keeps its instance.
	/// </summary>
	public static void Forget( Model model )
	{
		reads.Remove( model );
		charts.Remove( model );
		cache.Remove( model );
	}

	/// <summary>
	/// The draw calls a renderer's body groups and LOD draw, read back from the GPU on the main thread.
	/// Null if any can't be read. Once read, <see cref="Unwrap"/> needs nothing native and can run on any thread.
	/// </summary>
	public static Draw[] Read( Model model, ulong bodyGroups, int lod ) => reads.GetOrCreateValue( model ).GetOrAdd( (bodyGroups, lod), _ =>
	{
		var sources = new List<Draw>();
		var meshes = model.MeshInfo.Meshes;

		for ( int i = 0; i < meshes.Length; i++ )
		{
			// The same tests the scene object makes
			if ( (meshes[i].LodMask & (1 << lod)) == 0 || (meshes[i].MeshGroupMask & bodyGroups) == 0 )
				continue;

			for ( int d = 0; d < meshes[i].DrawCalls.Length; d++ )
			{
				if ( meshes[i].DrawCalls[d].Indices < 3 )
					continue;

				if ( !model.TryReadDrawCall( i, d, out var vertices, out var indices, out var streams ) )
					return null;

				sources.Add( new Draw( meshes[i].DrawCalls[d].Material, vertices, Array.ConvertAll( indices, x => (int)x ), (streams & (VertexStreams.TexCoord1 | VertexStreams.Color)) != 0 ) );
			}
		}

		return sources.ToArray();
	} );

	/// <summary>
	/// A model of the unwrapped geometry, its charts placed at a corner of the lightmap, in luxels.
	/// </summary>
	public static Model Build( Draw[] draws, Vector2 offset, Func<Material, Material> swap )
	{
		var builder = Model.Builder;

		foreach ( var draw in draws )
		{
			var mesh = new Mesh( swap( draw.Material ) );
			var count = draw.Vertices.Length;

			if ( draw.ModelStreams )
			{
				mesh.CreateVertexBuffer<ModelVertex>( count, draw.Vertices.Select( ( v, i ) => new ModelVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = new Vector2( v.TexCoord0.x, v.TexCoord0.y ),
					Texcoord1 = new Vector2( v.TexCoord1.x, v.TexCoord1.y ),
					LightmapUv = draw.Uvs[i] + offset,
					Color = v.Color,
				} ).ToArray() );
			}
			else
			{
				mesh.CreateVertexBuffer<PlainVertex>( count, draw.Vertices.Select( ( v, i ) => new PlainVertex
				{
					Position = v.Position,
					Normal = v.Normal,
					Tangent = v.Tangent,
					Texcoord = new Vector2( v.TexCoord0.x, v.TexCoord0.y ),
					LightmapUv = draw.Uvs[i] + offset,
				} ).ToArray() );
			}

			mesh.CreateIndexBuffer( draw.Indices.Length, draw.Indices.AsSpan() );
			mesh.Bounds = BBox.FromPoints( draw.Vertices.Select( x => x.Position ) );
			builder.AddMesh( mesh );
		}

		return builder.Create();
	}

	static readonly ConcurrentDictionary<string, bool> lightmapShaders = new();

	/// <summary>
	/// Whether a material's shader samples lightmaps. One that doesn't would draw a lightmapped model unlit.
	/// </summary>
	public static bool ReadsLightmaps( Material material ) => material?.ShaderName is { Length: > 0 } shader && lightmapShaders.GetOrAdd( shader, _ =>
	{
		// Finds the combo by name in the compiled shader's bytes. Ask the shader once it can list its combos
		var files = EngineFileSystem.Mounted ?? EngineFileSystem.Assets;
		var path = shader.EndsWith( "_c" ) ? shader : $"{shader}_c";
		return files.FileExists( path ) && files.ReadAllBytes( path ).IndexOf( "D_BAKED_LIGHTING_FROM_LIGHTMAP"u8 ) >= 0;
	} );

	/// <summary>
	/// What a material group draws in place of each of the model's default materials.
	/// </summary>
	public static Func<Material, Material> MaterialSwap( Model model, string group )
	{
		var index = string.IsNullOrEmpty( group ) ? 0 : model.GetMaterialGroupIndex( group );
		if ( index <= 0 ) return material => material;

		var swap = new Dictionary<Material, Material>();

		foreach ( var (original, replacement) in model.GetMaterials( 0 ).Zip( model.GetMaterials( index ) ) )
		{
			if ( original.IsValid() && replacement.IsValid() )
				swap[original] = replacement;
		}

		return material => material is not null && swap.TryGetValue( material, out var replacement ) ? replacement : material;
	}
}
