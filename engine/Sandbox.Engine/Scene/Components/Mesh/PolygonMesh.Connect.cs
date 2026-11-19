using HalfEdgeMesh;

namespace Sandbox;

public sealed partial class PolygonMesh
{
	public ConnectVerticesResult CanConnectVertexPath( IReadOnlyList<VertexHandle> vertices )
	{
		if ( vertices is null || vertices.Count < 2 ||
			vertices.Any( vertex => !vertex.IsValid || vertex.Mesh != Topology ) ||
			vertices.Distinct().Count() != vertices.Count )
			return ConnectVerticesResult.InvalidVertices;

		var result = ConnectVerticesResult.AlreadyConnected;
		for ( int i = 1; i < vertices.Count; ++i )
		{
			var segment = FindVertexPath( vertices[i - 1], vertices[i], null );
			if ( segment is not (ConnectVerticesResult.Success or ConnectVerticesResult.AlreadyConnected) )
				return segment;

			if ( segment == ConnectVerticesResult.Success )
				result = segment;
		}

		return result == ConnectVerticesResult.AlreadyConnected && vertices.Count > 2
			? FindVertexPath( vertices[^1], vertices[0], null )
			: result;
	}

	public bool ConnectVertexPath( IReadOnlyList<VertexHandle> vertices )
	{
		if ( CanConnectVertexPath( vertices ) != ConnectVerticesResult.Success )
			return false;

		var result = CreateVertexPath( vertices );
		Assert.True( result == ConnectVerticesResult.Success );
		return result == ConnectVerticesResult.Success;
	}

	ConnectVerticesResult CreateVertexPath( IReadOnlyList<VertexHandle> vertices )
	{
		var version = TopologyVersion;
		var points = new List<(VertexHandle Vertex, HalfEdgeHandle Edge, float Fraction)>();

		for ( int i = 0; i < vertices.Count; ++i )
		{
			if ( i == vertices.Count - 1 && (vertices.Count < 3 || TopologyVersion != version) )
				break;

			var start = vertices[i];
			var end = vertices[(i + 1) % vertices.Count];
			points.Clear();
			if ( FindVertexPath( start, end, points ) is not (ConnectVerticesResult.Success or ConnectVerticesResult.AlreadyConnected) )
				return ConnectVerticesResult.NoSharedFace;

			var current = start;
			foreach ( var point in points )
			{
				var next = point.Vertex;
				if ( !next.IsValid )
				{
					GetVerticesConnectedToEdge( point.Edge, out var a, out var b );
					if ( !AddVertexToEdge( a, b, point.Fraction, out next ) )
						return ConnectVerticesResult.NoSharedFace;
				}

				if ( !FindEdgeConnectingVertices( current, next ).IsValid && !ConnectVertices( current, next, out _ ) )
					return ConnectVerticesResult.NoSharedFace;

				current = next;
			}
		}

		return TopologyVersion != version ? ConnectVerticesResult.Success : ConnectVerticesResult.AlreadyConnected;
	}

	ConnectVerticesResult FindVertexPath( VertexHandle start, VertexHandle end, List<(VertexHandle Vertex, HalfEdgeHandle Edge, float Fraction)> points )
	{
		const float tolerance = 0.001f;

		if ( FindEdgeConnectingVertices( start, end ).IsValid )
		{
			points?.Add( (end, HalfEdgeHandle.Invalid, 0) );
			return ConnectVerticesResult.AlreadyConnected;
		}

		var target = GetVertexPosition( end );
		if ( !FindCutEdgeIntersection( start, target, out var firstEdge, out var firstFace, out var firstIntersection ) )
			return ConnectVerticesResult.NoSharedFace;

		var current = start;
		var position = GetVertexPosition( start );
		ComputeFaceNormal( firstFace, out var normal );
		var plane = new Plane( position, normal.Cross( target - position ).Normal );
		var entry = HalfEdgeHandle.Invalid;
		var face = firstFace;
		var visited = new HashSet<HalfEdgeHandle>();
		var result = ConnectVerticesResult.AlreadyConnected;

		for ( int remaining = Topology.HalfEdgeCount; remaining > 0; --remaining )
		{
			HalfEdgeHandle edge;
			Vector3 intersection;

			if ( remaining == Topology.HalfEdgeCount )
			{
				edge = firstEdge;
				intersection = firstIntersection;
			}
			else if ( current.IsValid )
			{
				if ( FindEdgeConnectingVertices( current, end ).IsValid )
				{
					points?.Add( (end, HalfEdgeHandle.Invalid, 0) );
					return result;
				}

				if ( !FindVertexPathIntersection( current, target, plane, out edge, out face, out intersection ) )
					return ConnectVerticesResult.NoSharedFace;
			}
			else
			{
				face = entry.Face == face ? entry.OppositeEdge.Face : entry.Face;
				if ( !face.IsValid )
					return ConnectVerticesResult.NoSharedFace;

				if ( !FindCutEdgeIntersectionInFace( face, position, plane, current, entry, out edge, out intersection ) )
					return ConnectVerticesResult.NoSharedFace;
			}

			var fullEdge = edge.Index < edge.OppositeEdge.Index ? edge : edge.OppositeEdge;
			if ( !visited.Add( fullEdge ) )
				return ConnectVerticesResult.NoSharedFace;

			GetVerticesConnectedToEdge( edge, out var a, out var b );
			var pa = GetVertexPosition( a );
			var pb = GetVertexPosition( b );
			CalcClosestPointOnLineSegment( intersection, pa, pb, out _, out var fraction );
			var next = fraction < tolerance ? a : fraction > 1 - tolerance ? b : VertexHandle.Invalid;
			var nextPosition = next.IsValid ? GetVertexPosition( next ) : Vector3.Lerp( pa, pb, fraction );
			var existing = current.IsValid && next.IsValid && FindEdgeConnectingVertices( current, next ).IsValid;

			if ( nextPosition == position || (!existing && !IsLineBetweenVerticesInsideFace( face, position, nextPosition )) )
				return ConnectVerticesResult.NoSharedFace;

			if ( !existing )
				result = ConnectVerticesResult.Success;
			points?.Add( (next, edge, fraction) );
			if ( next == end )
				return result;

			current = next;
			position = nextPosition;
			entry = edge;
		}

		return ConnectVerticesResult.NoSharedFace;
	}

	bool FindVertexPathIntersection( VertexHandle vertex, Vector3 target, Plane plane, out HalfEdgeHandle outEdge, out FaceHandle outFace, out Vector3 outPosition )
	{
		outEdge = HalfEdgeHandle.Invalid;
		outFace = FaceHandle.Invalid;
		outPosition = default;
		var position = GetVertexPosition( vertex );
		var minDistance = float.MaxValue;
		GetFacesConnectedToVertex( vertex, out var faces );

		foreach ( var face in faces )
		{
			if ( !FindCutEdgeIntersectionInFace( face, position, plane, vertex, HalfEdgeHandle.Invalid, out var edge, out var intersection ) )
				continue;

			var distance = intersection.DistanceSquared( target );
			if ( distance < minDistance )
			{
				outEdge = edge;
				outFace = face;
				outPosition = intersection;
				minDistance = distance;
			}
		}

		return outFace.IsValid;
	}
}
