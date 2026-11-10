using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	const float WorldFragmentSize = 1024.0f;
	readonly record struct WorldAggregate( Model Model, List<AggregateFragmentInfo> Fragments );
	sealed class WorldMaterialGroup( Material material, Vector2 mappingSize )
	{
		public readonly Material Material = material;
		public readonly Vector2 MappingSize = mappingSize;
		public readonly bool IsTranslucent = material.Flags.IsTranslucent;
		public readonly Dictionary<Vector3, MeshGroup> Chunks = [];
	}

	(Model Surfaces, List<WorldAggregate> Aggregates) BuildWorldModel( BspFile bsp, bool useAggregates = true, int modelIndex = 0, bool skybox = false )
	{
		var surfaces = _faceSurfaces;
		ref var worldModel = ref bsp.Models[modelIndex];
		var meshGroups = new Dictionary<(int Texture, int Page, bool NoDecals), WorldMaterialGroup>();
		var sharedGroups = new Dictionary<Material, WorldMaterialGroup>();
		var waterGroups = new Dictionary<(int Texture, float Height), MeshGroup>();
		var defaultMaterial = Material.Load( "materials/dev/reflectivity_30.vmat" );
		for ( int fi = 0; fi < worldModel.FaceCount; fi++ )
		{
			int faceIdx = worldModel.FirstFace + fi;
			ref var face = ref bsp.Faces[faceIdx];
			if ( modelIndex == 0 && (skybox ? !bsp.IsActiveSkyboxFace( faceIdx ) : bsp.IsSkyboxFace( faceIdx )) || !IsRenderable( bsp, face ) )
				continue;
			ref var texInfo = ref bsp.TexInfos[face.TexInfo];
			int texDataIdx = texInfo.TexData;
			var surface = surfaces[faceIdx];
			var groupKey = (texDataIdx, surface.Width > 0 && surface.Height > 0 ? surface.Page : -1, (texInfo.Flags & 0x2000) != 0);
			if ( !meshGroups.TryGetValue( groupKey, out var materialGroup ) )
			{
				var material = LoadWorldMaterial( bsp, texDataIdx, groupKey.Item2, defaultMaterial, groupKey.Item3 );
				if ( material.Flags.IsTranslucent || !sharedGroups.TryGetValue( material, out materialGroup ) )
				{
					materialGroup = new WorldMaterialGroup( material, Host.GetMaterialMappingSize( material, bsp.TexDatas[texDataIdx] ) );
					if ( !materialGroup.IsTranslucent )
						sharedGroups.Add( material, materialGroup );
				}
				meshGroups[groupKey] = materialGroup;
			}

			var tangent = new Vector3( texInfo.TextureVecsS.x, texInfo.TextureVecsS.y, texInfo.TextureVecsS.z ).Normal;
			var projection = new FaceProjection( texInfo, materialGroup.MappingSize, surface );
			if ( modelIndex == 0 && materialGroup.Material.ShaderName == "shaders/source1/water.shader" && materialGroup.Material.GetFeature( "F_SOURCE_WATER" ) != 0 )
			{
				var plane = bsp.Planes[face.PlaneNum];
				if ( MathF.Abs( plane.Normal.z ) > 0.9999f && face.DispInfo == -1 )
				{
					var waterKey = (texDataIdx, plane.Dist / plane.Normal.z);
					if ( !waterGroups.TryGetValue( waterKey, out var waterGroup ) )
					{
						waterGroup = new MeshGroup( materialGroup.Material );
						waterGroups.Add( waterKey, waterGroup );
					}

					BuildFace( bsp, ref face, projection, tangent, waterGroup, bsp.FaceVertexNormalOffsets[faceIdx] );
					continue;
				}

				Log.Warning( $"{FullPath}: water face {faceIdx} is not a horizontal plane; retaining its material without planar views." );
			}

			var cell = useAggregates && !materialGroup.IsTranslucent ? FaceCell( bsp, face ) : Vector3.Zero;
			if ( !materialGroup.Chunks.TryGetValue( cell, out var group ) )
			{
				group = new MeshGroup( materialGroup.Material );
				materialGroup.Chunks.Add( cell, group );
			}

			if ( face.DispInfo != -1 )
			{
				BuildDisplacement( bsp, ref face, ref surface, projection, tangent, group );
			}
			else
			{
				BuildFace( bsp, ref face, projection, tangent, group, bsp.FaceVertexNormalOffsets[faceIdx] );
			}
		}

		BuildWaterModels( waterGroups, skybox );
		return AssembleWorldModel( meshGroups, useAggregates, modelIndex, skybox );
	}

	static Vector3 FaceCell( BspFile bsp, BspFace face )
	{
		var center = Vector3.Zero;
		float scale = 1.0f / (face.NumEdges * WorldFragmentSize);
		for ( int edge = 0; edge < face.NumEdges; edge++ )
		{
			int surfEdge = bsp.SurfEdges[face.FirstEdge + edge];
			center += (surfEdge >= 0 ? bsp.Vertices[bsp.Edges[surfEdge].V0] : bsp.Vertices[bsp.Edges[-surfEdge].V1]) * scale;
		}

		return new Vector3( MathF.Floor( center.x ), MathF.Floor( center.y ), MathF.Floor( center.z ) );
	}

	(Model Surfaces, List<WorldAggregate> Aggregates) AssembleWorldModel( Dictionary<(int Texture, int Page, bool NoDecals), WorldMaterialGroup> groups, bool useAggregates, int modelIndex, bool skybox )
	{
		var surfaceBuilder = Model.Builder;
		var aggregates = new List<WorldAggregate>();
		bool hasSurfaces = false;
		BBox? renderBounds = null;
		int maxFragments = useAggregates ? AggregateRenderer.MaxFragments : 1;
		foreach ( var (key, materialGroup) in groups.DistinctBy( group => group.Value ) )
		{
			var chunks = materialGroup.Chunks.Values.Where( c => c.Vertices.Count > 0 && c.Indices.Count > 0 ).ToArray();
			if ( chunks.Length == 0 )
				continue;
			for ( int offset = 0; offset < chunks.Length; offset += maxFragments )
			{
				var batch = chunks.AsSpan( offset, Math.Min( maxFragments, chunks.Length - offset ) );
				bool isAggregate = useAggregates && !materialGroup.IsTranslucent;
				var builder = isAggregate ? Model.Builder : surfaceBuilder;
				var (bounds, fragments) = BuildWorldMesh( builder, materialGroup.Material, batch );
				renderBounds = renderBounds?.AddBBox( bounds ) ?? bounds;
				if ( isAggregate )
				{
					var name = $"{(skybox ? "skybox_" : "")}aggregate_{key.Texture}_{key.Page}_{offset / maxFragments}{(key.NoDecals ? "_nodecal" : "")}.vmdl";
					var model = CreateWorldModel( builder.WithViewBounds( bounds ), name );
					aggregates.Add( new WorldAggregate( model, fragments ) );
				}
				else
				{
					hasSurfaces = true;
				}
			}
		}

		var surfaces = hasSurfaces ? CreateWorldModel( surfaceBuilder.WithViewBounds( renderBounds ?? default ), $"{(skybox ? "skybox_" : "")}{(modelIndex == 0 ? "surfaces" : $"brush_{modelIndex}")}.vmdl" ) : null;
		return (surfaces, aggregates);
	}

	Model CreateWorldModel( ModelBuilder builder, string name )
	{
		var model = builder.WithName( $"source1/{Host.Ident}/{FullPath}/{name}" ).Create();
		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidDataException( $"{FullPath}: failed to build '{name}'." );
		_worldModels.Add( model );
		return model;
	}

	static (BBox Bounds, List<AggregateFragmentInfo> Fragments) BuildWorldMesh( ModelBuilder builder, Material material, ReadOnlySpan<MeshGroup> chunks, bool addTraceMesh = true )
	{
		int vertexCount = 0, indexCount = 0;
		foreach ( var chunk in chunks )
		{
			vertexCount = checked(vertexCount + chunk.Vertices.Count);
			indexCount = checked(indexCount + chunk.Indices.Count);
		}

		var vertices = new BlendLightmapVertex[vertexCount];
		var indices = new int[indexCount];
		var tracePositions = addTraceMesh ? new Vector3[vertexCount] : [];
		var traceIndices = addTraceMesh ? new int[indexCount] : [];
		var fragments = new List<AggregateFragmentInfo>( chunks.Length );
		BBox? bounds = null;
		int vertexOffset = 0, indexOffset = 0;
		foreach ( var chunk in chunks )
		{
			CollectionsMarshal.AsSpan( chunk.Vertices ).CopyTo( vertices.AsSpan( vertexOffset ) );
			CollectionsMarshal.AsSpan( chunk.Indices ).CopyTo( indices.AsSpan( indexOffset ) );
			var chunkBounds = new BBox( chunk.Vertices[0].Position, chunk.Vertices[0].Position );
			for ( int i = 0; i < chunk.Vertices.Count; i++ )
			{
				var position = chunk.Vertices[i].Position;
				if ( addTraceMesh )
					tracePositions[vertexOffset + i] = position;
				chunkBounds = chunkBounds.AddPoint( position );
			}

			if ( addTraceMesh )
				for ( int i = 0; i < chunk.Indices.Count; i++ )
					traceIndices[indexOffset + i] = chunk.Indices[i] + vertexOffset;
			var mins = chunkBounds.Mins;
			var maxs = chunkBounds.Maxs;
			if ( !float.IsFinite( mins.x ) || !float.IsFinite( mins.y ) || !float.IsFinite( mins.z ) || !float.IsFinite( maxs.x ) || !float.IsFinite( maxs.y ) || !float.IsFinite( maxs.z ) || mins.x > maxs.x || mins.y > maxs.y || mins.z > maxs.z )
				throw new InvalidDataException( "BSP bounds are non-finite or inverted." );
			bounds = bounds?.AddBBox( chunkBounds ) ?? chunkBounds;
			fragments.Add( new AggregateFragmentInfo( chunkBounds ) );
			vertexOffset += chunk.Vertices.Count;
			indexOffset += chunk.Indices.Count;
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = bounds.Value;
		mesh.SetVertexRange( 0, chunks[0].Vertices.Count );
		mesh.SetIndexRange( 0, chunks[0].Indices.Count );
		vertexOffset = chunks[0].Vertices.Count;
		indexOffset = chunks[0].Indices.Count;
		for ( int i = 1; i < chunks.Length; i++ )
		{
			mesh.AddSubMesh( material, indexOffset, chunks[i].Indices.Count, vertexOffset, chunks[i].Vertices.Count );
			vertexOffset += chunks[i].Vertices.Count;
			indexOffset += chunks[i].Indices.Count;
		}

		builder.AddMesh( mesh );
		if ( addTraceMesh )
			builder.AddTraceMesh( tracePositions, traceIndices );
		return (bounds.Value, fragments);
	}
}
