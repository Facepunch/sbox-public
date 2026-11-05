using System;
using System.Collections.Generic;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private List<Model> BuildShadowModel( BspFile bsp, int modelIndex )
	{
		var material = Material.Load( "materials/dev/reflectivity_30.vmat" );
		if ( material is null || material.IsError )
			throw new InvalidOperationException( $"{FullPath}: could not load the shadow blocker material." );
		var bindings = new SourceMaterialOverrides();
		bindings.Features["F_RENDER_BACKFACES"] = 0;
		material = Host.CreateMaterialVariant( material, $"{ResourcePrefix}_shadow_blockers.vmat", bindings );
		var opaqueTextures = new Dictionary<int, (bool Opaque, Vector2 MappingSize)>();
		var group = new MeshGroup( material );
		var model = bsp.Models[modelIndex];
		for ( int index = model.FirstFace; index < model.FirstFace + model.FaceCount; index++ )
		{
			ref var face = ref bsp.Faces[index];
			if ( modelIndex == 0 && bsp.IsSkyboxFace( index ) || !IsRenderable( bsp, face ) )
				continue;
			ref var info = ref bsp.TexInfos[face.TexInfo];
			if ( (info.Flags & (0x1000 | 8)) != 0 || !IsOpaqueTexture( info.TexData, out var mappingSize ) )
				continue;
			var surface = _faceSurfaces[index];
			var projection = new FaceProjection( info, mappingSize, surface );
			var tangent = new Vector3( info.TextureVecsS.x, info.TextureVecsS.y, info.TextureVecsS.z ).Normal;
			if ( face.DispInfo >= 0 )
				BuildDisplacement( bsp, ref face, ref surface, projection, tangent, group );
			else
				BuildFace( bsp, ref face, projection, tangent, group, bsp.FaceVertexNormalOffsets[index] );
		}

		if ( group.Indices.Count == 0 )
			return [];
		for ( int index = 0; index < group.Vertices.Count; index++ )
		{
			var vertex = group.Vertices[index];
			vertex.Normal = -vertex.Normal;
			group.Vertices[index] = vertex;
		}

		for ( int index = 0; index < group.Indices.Count; index += 3 )
			(group.Indices[index + 1], group.Indices[index + 2]) = (group.Indices[index + 2], group.Indices[index + 1]);
		var chunks = new Dictionary<Vector3, (MeshGroup Mesh, Dictionary<int, int> Vertices)>();
		for ( int index = 0; index < group.Indices.Count; index += 3 )
		{
			var center = (group.Vertices[group.Indices[index]].Position
				+ group.Vertices[group.Indices[index + 1]].Position
				+ group.Vertices[group.Indices[index + 2]].Position) / (3 * WorldFragmentSize * 4);
			var cell = new Vector3( MathF.Floor( center.x ), MathF.Floor( center.y ), MathF.Floor( center.z ) );
			if ( !chunks.TryGetValue( cell, out var chunk ) )
			{
				chunk = (new MeshGroup( material ), []);
				chunks.Add( cell, chunk );
			}
			for ( int vertex = 0; vertex < 3; vertex++ )
			{
				var source = group.Indices[index + vertex];
				if ( !chunk.Vertices.TryGetValue( source, out var target ) )
				{
					target = chunk.Mesh.Vertices.Count;
					chunk.Vertices.Add( source, target );
					chunk.Mesh.Vertices.Add( group.Vertices[source] );
				}
				chunk.Mesh.Indices.Add( target );
			}
		}

		var result = new List<Model>( chunks.Count );
		foreach ( var chunk in chunks.Values )
		{
			var builder = Model.Builder;
			var (bounds, _) = BuildWorldMesh( builder, material, [chunk.Mesh], addTraceMesh: false );
			result.Add( CreateWorldModel( builder.WithViewBounds( bounds ), $"shadow_faces_{modelIndex}_{result.Count}.vmdl" ) );
		}
		Log.Info( $"{FullPath}: model {modelIndex} shadow blockers contain {group.Indices.Count / 3} inverted opaque triangles in {result.Count} spatial chunks." );
		return result;
		bool IsOpaqueTexture( int texture, out Vector2 mappingSize )
		{
			if ( !opaqueTextures.TryGetValue( texture, out var entry ) )
			{
				var surface = LoadWorldMaterial( bsp, texture, -1, material );
				entry = (!surface.Flags.IsTranslucent && !surface.Flags.IsAlphaTest, Host.GetMaterialMappingSize( surface, bsp.TexDatas[texture] ));
				opaqueTextures.Add( texture, entry );
			}
			mappingSize = entry.MappingSize;
			return entry.Opaque;
		}
	}

	private void AddShadowBlocker( List<Model> models, GameObject parent, bool enabled = true )
	{
		foreach ( var model in models )
		{
			var obj = new GameObject( false, "Source shadow blockers" )
			{
				Parent = parent,
				IsStatic = parent.IsStatic,
				LocalPosition = Vector3.Zero,
				LocalRotation = Rotation.Identity,
				LocalScale = Vector3.One
			};
			var renderer = obj.AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
			obj.Enabled = enabled;
		}
	}
}
