using System;
using System.Collections.Generic;
using Sandbox;
using Sandbox.Rendering;

namespace Editor;

partial class SceneCompiler
{
	/// <summary>
	/// Add a prop's selected LOD draw calls to the groups as world space chunks, one chunk per draw call.
	/// All or nothing, like the map compiler - a prop we can only partially compile keeps drawing itself.
	/// A baked prop is read as its lightmapped copy draws it, so it lands in the lightmap where the bake put it.
	/// </summary>
	static bool TryCompileProp( ModelRenderer renderer, Dictionary<GroupKey, List<Chunk>> groups )
	{
		var model = renderer.Model;
		var lod = renderer.LightmapLod;
		var lightmap = renderer.Lightmap;

		var draws = lightmap is not null
			? LightmapGeometry.Unwrap( model, renderer.BodyGroups, lod, renderer.WorldScale, lightmap.LuxelSize )
			: LightmapGeometry.Read( model, renderer.BodyGroups, lod );

		if ( draws is not { Length: > 0 } )
		{
			Log.Warning( $"Compile Scene: couldn't read {model.ResourceName}, leaving it alone." );
			return false;
		}

		var transform = new GeometryTransform( renderer.WorldTransform );
		var material = renderer.MaterialOverride;
		var swap = LightmapGeometry.MaterialSwap( model, renderer.MaterialGroup );
		var tint = renderer.Tint;
		var tags = TagKey( renderer.GameObject );

		foreach ( var mesh in draws )
		{
			var draw = material ?? swap( mesh.Material );
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
					LightmapUv = mesh.Uvs is null ? default : mesh.Uvs[i] + renderer.LightmapOffset.Value,
					Color = vertex.Color,
				};
			}

			// A stream the model never had reads back as a default, and including that default
			// would light up a shader path the prop doesn't take.
			var streams = mesh.ModelStreams ? CompiledStreams.Model : CompiledStreams.None;

			Add( groups, draw, tint, tags, lightmap is not null, vertices, mesh.Indices, streams, preserveOrigin ? renderer.WorldTransform : Transform.Zero,
				preserveOrigin ? model.Bounds : Bounds( vertices ) );
		}

		return true;
	}
}
