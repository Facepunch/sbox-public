using HalfEdgeMesh;
using System;
using System.IO;

namespace Editor;

public static partial class EditorUtility
{
	/// <summary>
	/// Import an OBJ, FBX or DMX source file into a new editable mesh in model space.
	/// Accepts an absolute filename or an asset-relative path. Returns null if the file cannot be imported.
	/// </summary>
	public static PolygonMesh CreatePolygonMeshFromMeshFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		var extension = Path.GetExtension( path ).ToLowerInvariant();
		if ( extension is not (".obj" or ".fbx" or ".dmx") )
			return null;

		var filename = Path.IsPathFullyQualified( path ) ? path : AssetSystem.FindByPath( path )?.AbsolutePath;
		if ( !File.Exists( filename ) )
			return null;

		var source = CSourcePolygonMesh.Create();
		try
		{
			return source.LoadMeshFile( filename ) ? ReadPolygonMesh( source, filename ) : null;
		}
		finally
		{
			source.DeleteThis();
		}
	}

	private static unsafe PolygonMesh ReadPolygonMesh( CSourcePolygonMesh source, string path )
	{
		var positions = new Vector3[source.GetNumVertices()];
		var faceSizes = new int[source.GetNumFaces()];
		var materialIndices = new int[faceSizes.Length];
		var indices = new int[source.GetNumFaceVertices()];
		var uvs = source.HasTexCoords() ? new Vector2[indices.Length] : null;
		var normals = source.HasNormals() ? new Vector3[indices.Length] : null;
		var colors = source.HasColors() ? new Color32[indices.Length] : null;
		fixed ( Vector3* p = positions, n = normals )
		fixed ( Vector2* uv = uvs )
		fixed ( Color32* c = colors )
		fixed ( int* sizes = faceSizes, g = materialIndices, ix = indices )
		{
			if ( !source.CopyPolygons( (IntPtr)p, (IntPtr)sizes, (IntPtr)g, (IntPtr)ix, (IntPtr)uv, (IntPtr)n, (IntPtr)c ) )
				return null;
		}

		var materials = new Material[source.GetNumFaceGroups()];
		for ( int i = 0; i < materials.Length; i++ )
		{
			var name = source.GetFaceGroupMaterial( i );
			if ( string.IsNullOrWhiteSpace( name ) ) continue;
			var materialPath = Path.ChangeExtension( name, ".vmat" );
			var asset = AssetSystem.FindByPath( materialPath ) ?? AssetSystem.FindByPath( Path.Combine( Path.GetDirectoryName( path ) ?? "", materialPath ) );
			materials[i] = asset?.LoadResource<Material>();
		}

		var mesh = new PolygonMesh();
		var vertices = mesh.AddVertices( positions );
		var edgeNormals = normals is null ? null : new Dictionary<HalfEdgeHandle, (Vector3 Start, Vector3 End)>();
		VertexHandle[] loop = [];
		int offset = 0;
		for ( int i = 0; i < faceSizes.Length; i++ )
		{
			if ( loop.Length != faceSizes[i] )
				loop = new VertexHandle[faceSizes[i]];
			for ( int j = 0; j < loop.Length; j++ )
				loop[j] = vertices[indices[offset + j]];

			var face = mesh.AddFace( loop );
			if ( !face.IsValid )
			{
				// A non-manifold edge cannot share a half-edge in PolygonMesh.
				// Keep this polygon as a separate island so its geometry survives import.
				for ( int j = 0; j < loop.Length; j++ )
					loop[j] = mesh.AddVertex( positions[indices[offset + j]] );
				face = mesh.AddFace( loop );
				if ( !face.IsValid ) return null;
			}

			if ( (uint)materialIndices[i] < materials.Length && materials[materialIndices[i]] is { } material )
				mesh.SetFaceMaterial( face, material );

			var edge = face.Edge;
			for ( int j = 0; j < loop.Length; j++, edge = edge.NextEdge )
			{
				if ( uvs is not null )
					mesh.SetTextureCoord( edge, uvs[offset + j] );
				if ( colors is not null )
					mesh.SetVertexColor( edge, colors[offset + j] );
				if ( normals is null ) continue;

				var start = normals[offset + (j + loop.Length - 1) % loop.Length];
				var end = normals[offset + j];
				if ( edgeNormals.Remove( edge.OppositeEdge, out var other ) )
				{
					var smooth = start.AlmostEqual( other.End ) && end.AlmostEqual( other.Start );
					var mode = smooth ? PolygonMesh.EdgeSmoothMode.Soft : PolygonMesh.EdgeSmoothMode.Hard;
					mesh.SetEdgeSmoothing( edge, mode );
					mesh.SetEdgeSmoothing( edge.OppositeEdge, mode );
				}
				else
				{
					edgeNormals.Add( edge, (start, end) );
				}
			}
			offset += loop.Length;
		}

		if ( uvs is not null )
			mesh.ComputeFaceTextureParametersFromCoordinates();
		else
			mesh.TextureAlignToGrid( mesh.Transform );

		return mesh;
	}
}
