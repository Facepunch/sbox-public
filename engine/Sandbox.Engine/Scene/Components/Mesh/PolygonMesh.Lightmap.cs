using HalfEdgeMesh;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PolygonMesh
{
	float _luxelSize;
	Vector2 _lightmapOffset;
	Vector3 _lightmapScale = Vector3.One;
	readonly Dictionary<HalfEdgeHandle, Vector2> _lightmapUvs = [];

	/// <summary>
	/// The built vertices don't have the lightmap coordinates <see cref="SetLightmap"/> last asked for.
	/// Not an edit: the faces are what they were.
	/// </summary>
	internal bool IsLightmapDirty { get; private set; }

	/// <summary>
	/// Where this mesh sits in a baked lightmap: the size of a luxel in world units, zero for no lightmap, the
	/// corner of its charts in luxels, and the scale it's drawn at.
	/// </summary>
	internal void SetLightmap( float luxelSize, Vector2 offset, Vector3 scale )
	{
		if ( _luxelSize == luxelSize && _lightmapOffset == offset && _lightmapScale == scale )
			return;

		_luxelSize = luxelSize;
		_lightmapOffset = offset;
		_lightmapScale = scale;
		IsLightmapDirty = true;
	}

	void UpdateLightmapUvs()
	{
		_lightmapUvs.Clear();

		if ( _luxelSize > 0 )
			ComputeLightmapUvs( _luxelSize, _lightmapScale, _lightmapUvs );
	}

	/// <summary>
	/// Unwrap for a lightmap, in luxels of a size in world units. Returns the size the charts fill.
	/// </summary>
	internal Vector2Int ComputeLightmapUvs( float luxelSize, Vector3 scale, Dictionary<HalfEdgeHandle, Vector2> uvs = null )
	{
		var (corners, faces, edges) = LightmapCorners( scale );
		var cornerUvs = LightmapUnwrap.Pack( LightmapUnwrap.Split( corners, faces ), luxelSize, out var size );

		if ( uvs is not null )
		{
			for ( int i = 0; i < edges.Length; i++ )
				uvs[edges[i]] = cornerUvs[i] + _lightmapOffset;
		}

		return size;
	}

	/// <summary>
	/// What <see cref="LightmapUnwrap.Split"/> takes: the corners of its triangles at a scale, the face each triangle is
	/// on, and the half edge each corner is.
	/// </summary>
	internal (Vector3[] Corners, int[] Faces, HalfEdgeHandle[] Edges) LightmapCorners( Vector3 scale )
	{
		var corners = new List<Vector3>();
		var edges = new List<HalfEdgeHandle>();
		var faces = new List<int>();

		foreach ( var hFace in Topology.FaceHandles )
		{
			if ( !GetFaceVerticesConnectedToFace( hFace, out var faceEdges ) )
				continue;

			var positions = Topology.GetFaceVertices( hFace ).Select( x => Positions[x] ).ToArray();
			if ( positions.Length != faceEdges.Length )
				continue;

			var indices = Mesh.TriangulatePolygon( positions );
			if ( indices.Length % 3 != 0 )
				continue;

			foreach ( var index in indices )
			{
				corners.Add( positions[index] * scale );
				edges.Add( faceEdges[index] );
			}

			// A face stays whole, each of its vertices has one place in the lightmap
			for ( int i = 0; i < indices.Length; i += 3 )
				faces.Add( hFace.Index );
		}

		return (corners.ToArray(), faces.ToArray(), edges.ToArray());
	}
}
