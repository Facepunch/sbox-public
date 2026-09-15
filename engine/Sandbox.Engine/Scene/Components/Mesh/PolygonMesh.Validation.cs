using HalfEdgeMesh;

namespace Sandbox;

/// <summary>
/// Non-destructive checks that answer whether an edit is legal before it's attempted, so tools can
/// disable an operation and explain why instead of silently doing nothing.
/// </summary>
public partial class PolygonMesh
{
	/// <summary>
	/// Why a pair of vertices can't be connected by <see cref="ConnectVertices(VertexHandle, VertexHandle, out HalfEdgeHandle)"/>.
	/// </summary>
	public enum ConnectVerticesResult
	{
		/// <summary>
		/// A new edge can be created between these two vertices.
		/// </summary>
		Success,

		/// <summary>
		/// One of the vertices is invalid, or they're the same vertex.
		/// </summary>
		InvalidVertices,

		/// <summary>
		/// An edge between these two vertices already exists.
		/// </summary>
		AlreadyConnected,

		/// <summary>
		/// Both vertices are internal to a face. Splitting a face needs at least one vertex on its perimeter.
		/// </summary>
		BothVerticesInternal,

		/// <summary>
		/// The vertices aren't part of the same face, so there's nothing to split. The new edge would
		/// have no face attached to it, which this mesh can't represent.
		/// </summary>
		NoSharedFace,

		/// <summary>
		/// The vertices share a face, but the edge between them would leave that face, which happens on
		/// concave faces.
		/// </summary>
		OutsideFace,
	}

	/// <summary>
	/// Can these two vertices be connected by a new edge? Runs the same checks as
	/// <see cref="ConnectVertices(VertexHandle, VertexHandle, out HalfEdgeHandle)"/> without modifying the mesh.
	/// </summary>
	public ConnectVerticesResult CanConnectVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		if ( !hVertexA.IsValid || !hVertexB.IsValid || hVertexA == hVertexB )
			return ConnectVerticesResult.InvalidVertices;

		if ( Topology.FindFullEdgeConnectingVertices( hVertexA, hVertexB ).IsValid )
			return ConnectVerticesResult.AlreadyConnected;

		// Mirror the order ConnectVertices works in, it looks for a shared face before anything else,
		// otherwise we'd blame something more specific when the vertices simply aren't on a face together.
		Topology.FindFacesSharedByVertices( hVertexA, hVertexB, out var sharedFaces );

		if ( sharedFaces.Count == 0 )
			return ConnectVerticesResult.NoSharedFace;

		if ( Topology.IsVertexInternal( hVertexA ) && Topology.IsVertexInternal( hVertexB ) )
			return ConnectVerticesResult.BothVerticesInternal;

		foreach ( var hSharedFace in sharedFaces )
		{
			if ( !IsLineBetweenVerticesInsideFace( hSharedFace, hVertexA, hVertexB ) )
				continue;

			if ( CanAddEdgeToFace( hSharedFace, hVertexA, hVertexB ) )
				return ConnectVerticesResult.Success;
		}

		return ConnectVerticesResult.OutsideFace;
	}

	/// <summary>
	/// Would adding an edge between these two vertices of a face succeed? Does not modify the mesh.
	/// </summary>
	private bool CanAddEdgeToFace( FaceHandle hFace, VertexHandle hVertexA, VertexHandle hVertexB )
	{
		if ( !ResolveFaceVerticesForNewEdge( hFace, hVertexA, hVertexB, out var hFaceVertexA, out var hFaceVertexB ) )
			return false;

		return Topology.CanAddEdgeToFace( hFaceVertexA, hFaceVertexB );
	}
}
