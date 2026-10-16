using System;

namespace Editor.MapDoc;

/// <summary>
/// One polygon of a <see cref="MapMesh"/>, in world space.
/// </summary>
public sealed class MapMeshFace
{
	/// <summary>
	/// The face's position in the mesh, for <see cref="MapMesh.SetFaceMaterial"/>.
	/// </summary>
	public int Index { get; }

	/// <summary>
	/// The material path, like "materials/dev/reflectivity_30.vmat".
	/// </summary>
	public string Material { get; }

	/// <summary>
	/// Which way the face points.
	/// </summary>
	public Vector3 Normal { get; }

	/// <summary>
	/// The corners, counter-clockwise seen from the side the face points.
	/// </summary>
	public IReadOnlyList<Vector3> Vertices { get; }

	/// <summary>
	/// How many triangles the face renders as.
	/// </summary>
	public int TriangleCount => Math.Max( Vertices.Count - 2, 0 );

	/// <summary>
	/// The average of the corners.
	/// </summary>
	public Vector3 Center { get; }

	/// <summary>
	/// The face's area in square units.
	/// </summary>
	public float Area { get; }

	internal MapMeshFace( int index, string material, Vector3 normal, Vector3[] vertices )
	{
		Index = index;
		Material = material;
		Normal = normal;
		Vertices = vertices;
		Center = vertices.Length == 0 ? default : vertices.Aggregate( Vector3.Zero, ( a, b ) => a + b ) / vertices.Length;

		// Fan triangulation - faces are planar, and convex in all but the oddest cases
		var area = 0f;
		for ( int i = 1; i + 1 < vertices.Length; i++ )
		{
			area += Vector3.Cross( vertices[i] - vertices[0], vertices[i + 1] - vertices[0] ).Length * 0.5f;
		}

		Area = area;
	}
}
