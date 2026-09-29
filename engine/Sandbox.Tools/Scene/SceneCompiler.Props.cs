using System;
using System.Collections.Generic;
using Sandbox;

namespace Editor;

partial class SceneCompiler
{
	/// <summary>
	/// One draw call of a model's geometry, in model space. Reused by props with matching body groups and LOD.
	/// </summary>
	sealed class PropMesh
	{
		public Material Material;
		public Vertex[] Vertices;
		public uint[] Indices;
		public CompiledStreams Streams;
	}

	/// <summary>
	/// Add a prop's selected LOD draw calls to the groups as world space chunks, one chunk per draw call.
	/// All or nothing, like the map compiler - a prop we can only partially compile keeps drawing itself.
	/// </summary>
	static bool TryCompileProp( ModelRenderer renderer, Dictionary<(Model Model, ulong BodyGroups, int Lod), List<PropMesh>> cache, Dictionary<GroupKey, List<Chunk>> groups )
	{
		var model = renderer.Model;
		var lod = Math.Clamp( renderer.LodOverride ?? 0, 0, Math.Max( 0, model.MaxLodLevel ) );
		var key = (model, renderer.BodyGroups, lod);

		if ( !cache.TryGetValue( key, out var meshes ) )
		{
			meshes = Read( model, renderer.BodyGroups, lod );
			cache[key] = meshes;
		}

		if ( meshes is null )
			return false;

		var transform = new GeometryTransform( renderer.WorldTransform );
		var material = renderer.MaterialOverride;
		var swap = MaterialSwap( model, renderer.MaterialGroup );
		var tint = renderer.Tint;
		var tags = TagKey( renderer.GameObject );

		foreach ( var mesh in meshes )
		{
			var draw = mesh.Material;

			if ( swap is not null && swap.TryGetValue( draw, out var replacement ) )
			{
				draw = replacement;
			}

			draw = material ?? draw;
			var preserveOrigin = PropOrigin( draw );
			var source = mesh.Vertices;
			var vertices = new CompiledVertex[source.Length];

			for ( int i = 0; i < source.Length; i++ )
			{
				ref readonly var vertex = ref source[i];

				vertices[i] = new CompiledVertex
				{
					Position = preserveOrigin ? vertex.Position : transform.Position( vertex.Position ),
					Normal = preserveOrigin ? vertex.Normal : transform.Normal( vertex.Normal ),
					Tangent = preserveOrigin ? vertex.Tangent : transform.Tangent( vertex.Tangent ),
					Texcoord = new Vector2( vertex.TexCoord0.x, vertex.TexCoord0.y ),
					Texcoord1 = new Vector2( vertex.TexCoord1.x, vertex.TexCoord1.y ),
					Color = vertex.Color,
				};
			}

			var indices = new int[mesh.Indices.Length];

			for ( int i = 0; i < indices.Length; i++ )
			{
				indices[i] = (int)mesh.Indices[i];
			}

			Add( groups, draw, tint, tags, vertices, indices, mesh.Streams, preserveOrigin ? renderer.WorldTransform : Transform.Zero,
				preserveOrigin ? model.Bounds : Bounds( vertices ) );
		}

		return true;
	}

	/// <summary>
	/// A material group's replacements, keyed by the default material each one stands in for. Group
	/// zero lists the materials a group overrides, so the two lists line up by index. Null when the
	/// prop just draws the model's default materials.
	/// </summary>
	static Dictionary<Material, Material> MaterialSwap( Model model, string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return null;

		var group = model.GetMaterialGroupIndex( name );

		if ( group <= 0 )
			return null;

		var swap = new Dictionary<Material, Material>();

		foreach ( var (original, replacement) in model.GetMaterials( 0 ).Zip( model.GetMaterials( group ) ) )
		{
			if ( original.IsValid() && replacement.IsValid() )
			{
				swap[original] = replacement;
			}
		}

		return swap.Count > 0 ? swap : null;
	}

	/// <summary>
	/// Pull a model's selected LOD geometry off the GPU, one draw call at a time, taking only the meshes
	/// the renderer's body groups draw. Null when there's nothing usable, or when any of it won't
	/// read - we'd rather leave a prop alone than compile half of it.
	/// </summary>
	static List<PropMesh> Read( Model model, ulong bodyGroups, int lod )
	{
		var meshes = model.MeshInfo.Meshes;
		var result = new List<PropMesh>();

		for ( int i = 0; i < meshes.Length; i++ )
		{
			var mesh = meshes[i];

			if ( (mesh.LodMask & (1 << lod)) == 0 )
				continue;

			// The same test the scene object does, so we compile the body the prop is wearing
			if ( (mesh.MeshGroupMask & bodyGroups) == 0 )
				continue;

			for ( int d = 0; d < mesh.DrawCalls.Length; d++ )
			{
				var draw = mesh.DrawCalls[d];

				if ( draw.Indices < 3 )
					continue;

				if ( !model.TryReadDrawCall( i, d, out var vertices, out var indices, out var streams ) )
				{
					Log.Warning( $"Compile Scene: couldn't read {model.ResourceName} mesh {mesh.Name} draw call {d}, leaving it alone." );
					return null;
				}

				// A stream the model never had reads back as a default, and including that default
				// would light up a shader path the prop doesn't take.
				var carried = CompiledStreams.None;

				if ( (streams & (VertexStreams.TexCoord1 | VertexStreams.Color)) != 0 )
				{
					carried = CompiledStreams.Model;
				}

				result.Add( new PropMesh { Material = draw.Material, Vertices = vertices, Indices = indices, Streams = carried } );
			}
		}

		return result.Count > 0 ? result : null;
	}
}
