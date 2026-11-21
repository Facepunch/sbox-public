using System;
using System.Collections.Generic;
using Sandbox;

namespace Editor;

partial class SceneCompiler
{
	/// <summary>
	/// Turn the meshes we couldn't weld into the world into models of their own.
	/// </summary>
	static async Task<int> ConvertMeshes( Scene compiled, MeshComponent[] meshes, SceneFolder folder, string outputFolder, string resourceFolder, SceneCompileStatistics statistics, Func<int, int, Task> step, SceneCompileSession session )
	{
		var converted = 0;

		for ( int i = 0; i < meshes.Length; i++ )
		{
			// Only the conversion itself wants the compiled scene current. Stepping pumps the
			// editor, which has no business drawing a frame with someone else's scene pushed.
			using ( compiled.Push() )
			{
				if ( Convert( meshes[i], folder, $"{outputFolder}/mesh_{converted}.vmdl_c", $"{resourceFolder}/mesh_{converted}.vmdl", statistics, session ) )
					converted++;
			}

			await step( i + 1, meshes.Length );
		}

		return converted;
	}

	/// <summary>
	/// Replace one mesh with a renderer and a collider drawing the model we build from it.
	/// </summary>
	static bool Convert( MeshComponent mesh, SceneFolder folder, string path, string resourcePath, SceneCompileStatistics statistics, SceneCompileSession session )
	{
		if ( Build( mesh, resourcePath, statistics ) is not { } built )
		{
			session.Warn( $"Mesh '{mesh.GameObject.Name}' has no triangulated geometry. Removing it from the compiled scene.", mesh );
			return false;
		}

		var model = WriteModel( folder, path, built );
		if ( !model.IsValid() || model.IsError )
			throw new InvalidOperationException( $"Could not load the compiled model for mesh '{mesh.GameObject.Name}'." );

		var go = mesh.GameObject;

		if ( !mesh.HideInGame )
		{
			var renderer = go.AddComponent<ModelRenderer>();
			renderer.Model = model;
			renderer.Tint = mesh.Color;
			renderer.RenderType = mesh.RenderType;
			renderer.Enabled = mesh.Enabled;
		}

		if ( mesh.Collision != MeshComponent.CollisionType.None )
		{
			var properties = mesh.Serialize().AsObject();
			properties.Remove( "__guid" );

			var collider = go.AddComponent<ModelCollider>();
			collider.DeserializeImmediately( properties );
			collider.Model = model;
		}

		// A mesh tags its own object as world when it loads, and nothing's left to do that.
		go.Tags.Add( "world" );

		return true;
	}

	/// <summary>
	/// A model holding one mesh's geometry, in the mesh's own local space, carrying only the
	/// collision it was set to use - a mesh builds a hull and a mesh shape and picks between them
	/// as it goes, which a model collider has no way of knowing.
	/// </summary>
	static Model Build( MeshComponent mesh, string resourcePath, SceneCompileStatistics statistics )
	{
		if ( mesh.Mesh is null )
			return null;

		mesh.Mesh.SetSmoothingAngle( mesh.SmoothingAngle );
		var submeshes = mesh.Mesh.Triangulate();

		if ( submeshes.Count == 0 )
			return null;

		var builder = Model.Builder.WithName( resourcePath );

		for ( int i = 0; i < submeshes.Count; i++ )
		{
			var submesh = submeshes[i];
			var vertices = submesh.Vertices;

			var render = new Mesh( submesh.Material );
			render.CreateVertexBuffer( vertices.Length, vertices );
			render.CreateIndexBuffer( submesh.Indices.Length, submesh.Indices );
			render.Bounds = Bounds( vertices );
			render.UvDensity = UvDensity( vertices, submesh.Indices );

			builder.AddMesh( render );
			statistics.VertexCount += vertices.Length;
			statistics.TriangleCount += submesh.Indices.Length / 3;
		}

		AddMeshCollision( builder, mesh, submeshes, addTraceMesh: true );

		var model = builder.Create();
		if ( !model.IsValid() || model.IsError )
			throw new InvalidOperationException( $"Could not build a model for mesh '{mesh.GameObject.Name}'." );

		return model;
	}

	/// <summary>
	/// Bounds of a submesh the mesh tool triangulated, which is already in the space it's drawn in.
	/// </summary>
	static BBox Bounds( ReadOnlySpan<PolygonMesh.MeshVertex> vertices )
	{
		var min = vertices[0].Position;
		var max = min;

		for ( int i = 1; i < vertices.Length; i++ )
		{
			min = Vector3.Min( min, vertices[i].Position );
			max = Vector3.Max( max, vertices[i].Position );
		}

		return new BBox( min, max );
	}
}
