using HalfEdgeMesh;

namespace Sandbox;

partial class PolygonMesh
{
	public enum BooleanOperation
	{
		Union,
		Subtract,
		Intersect
	}

	/// <summary>
	/// Perform a boolean operation between this mesh (A) and another mesh (B).
	/// The result replaces this mesh. The other mesh is not modified.
	/// </summary>
	public bool PerformBoolean( PolygonMesh other, Transform relativeTransform, BooleanOperation operation )
	{
		var boundsA = CalculateBounds();
		var boundsB = BBox.FromPoints( other.GetVertexPositions().Select( v => relativeTransform.PointToWorld( v ) ) );

		if ( !boundsA.Overlaps( boundsB ) )
			return HandleNonOverlapping( other, relativeTransform, operation );

		var planesA = CollectUniquePlanes( this, Transform.Zero );
		var planesB = CollectUniquePlanes( other, relativeTransform );
		var trianglesA = BuildTriangleList( this, Transform.Zero );
		var trianglesB = BuildTriangleList( other, relativeTransform );

		var resultA = ProcessOperand( this, Transform.Zero, planesB, trianglesB, boundsB, operation, isOperandB: false );
		var resultB = ProcessOperand( other, relativeTransform, planesA, trianglesA, boundsA, operation, isOperandB: true );

		if ( operation == BooleanOperation.Subtract )
		{
			resultB.FlipAllFaces();
			resultB.ComputeFaceTextureCoordinatesFromParameters();
		}

		RemoveFaces( FaceHandles.ToList() );
		MergeMesh( resultA, Transform.Zero, out _, out _, out _ );
		MergeMesh( resultB, Transform.Zero, out _, out _, out _ );

		var allVertices = VertexHandles.ToList();
		if ( allVertices.Count >= 2 )
			MergeVerticesWithinDistance( allVertices, 0.001f, true, true, out _ );

		IsDirty = true;
		return true;
	}

	private PolygonMesh ProcessOperand( PolygonMesh source, Transform sourceTransform, List<Plane> otherPlanes, List<(Vector3 A, Vector3 B, Vector3 C)> otherTriangles, BBox otherBounds, BooleanOperation operation, bool isOperandB )
	{
		var result = new PolygonMesh();
		result.MergeMesh( source, sourceTransform, out _, out _, out _ );
		result.SetTransform( Transform );

		foreach ( var plane in otherPlanes )
		{
			var facesToClip = result.FaceHandles
				.Where( f => f.IsValid && result.DoesFaceStraddlePlane( f, plane ) && result.DoesFaceOverlapBounds( f, otherBounds ) )
				.ToList();

			if ( facesToClip.Count > 0 )
				result.ClipFacesByPlaneAndCap( facesToClip, plane, false, false );
		}

		var facesToRemove = result.FaceHandles
			.Where( f => f.IsValid && ShouldRemoveFace( result, f, otherTriangles, otherBounds, operation, isOperandB ) )
			.ToList();

		result.RemoveFaces( facesToRemove );
		DissolveCoplanarEdges( result );
		result.ComputeFaceTextureCoordinatesFromParameters();

		return result;
	}

	private static bool ShouldRemoveFace( PolygonMesh mesh, FaceHandle face, List<(Vector3 A, Vector3 B, Vector3 C)> otherTriangles, BBox otherBounds, BooleanOperation operation, bool isOperandB )
	{
		var center = mesh.GetFaceCenter( face );
		var inside = IsPointInsideMesh( center, otherTriangles, otherBounds );

		return operation switch
		{
			BooleanOperation.Union => inside,
			BooleanOperation.Subtract => isOperandB ? !inside : inside,
			BooleanOperation.Intersect => !inside,
			_ => false
		};
	}

	private static bool IsPointInsideMesh( Vector3 point, List<(Vector3 A, Vector3 B, Vector3 C)> triangles, BBox meshBounds )
	{
		if ( !meshBounds.Contains( point ) )
			return false;

		ReadOnlySpan<Vector3> directions =
		[
			new Vector3( 1.0f, 0.3f, 0.1f ).Normal,
			new Vector3( -0.5f, 1.0f, 0.2f ).Normal,
			new Vector3( 0.2f, -0.7f, 1.0f ).Normal
		];

		int insideVotes = 0;

		foreach ( var dir in directions )
		{
			int count = 0;
			foreach ( var (a, b, c) in triangles )
			{
				if ( RayHitsTriangle( point, dir, a, b, c ) )
					count++;
			}

			if ( (count % 2) == 1 )
				insideVotes++;
		}

		return insideVotes >= 2;
	}

	private static bool RayHitsTriangle( Vector3 origin, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2 )
	{
		var edge1 = v1 - v0;
		var edge2 = v2 - v0;
		var h = Vector3.Cross( dir, edge2 );
		var a = Vector3.Dot( edge1, h );

		if ( MathF.Abs( a ) < 0.00001f )
			return false;

		var f = 1.0f / a;
		var s = origin - v0;
		var u = f * Vector3.Dot( s, h );

		if ( u < 0.0f || u > 1.0f )
			return false;

		var q = Vector3.Cross( s, edge1 );
		var v = f * Vector3.Dot( dir, q );

		if ( v < 0.0f || (u + v) > 1.0f )
			return false;

		var t = f * Vector3.Dot( edge2, q );
		return t > 0.0001f;
	}

	private static List<(Vector3 A, Vector3 B, Vector3 C)> BuildTriangleList( PolygonMesh mesh, Transform transform )
	{
		var triangles = new List<(Vector3, Vector3, Vector3)>();

		foreach ( var face in mesh.FaceHandles )
		{
			if ( !mesh.GetVerticesConnectedToFace( face, out var vertices ) || vertices.Count() < 3 )
				continue;

			var v0 = transform.PointToWorld( mesh.GetVertexPosition( vertices[0] ) );
			for ( int i = 1; i < vertices.Count() - 1; i++ )
			{
				var v1 = transform.PointToWorld( mesh.GetVertexPosition( vertices[i] ) );
				var v2 = transform.PointToWorld( mesh.GetVertexPosition( vertices[i + 1] ) );
				triangles.Add( (v0, v1, v2) );
			}
		}

		return triangles;
	}

	private bool DoesFaceStraddlePlane( FaceHandle face, Plane plane )
	{
		if ( !GetVerticesConnectedToFace( face, out var vertices ) )
			return false;

		const float eps = 0.001f;
		bool hasFront = false, hasBack = false;

		foreach ( var vertex in vertices )
		{
			var dist = plane.GetDistance( GetVertexPosition( vertex ) );
			if ( dist > eps ) hasFront = true;
			else if ( dist < -eps ) hasBack = true;

			if ( hasFront && hasBack )
				return true;
		}

		return false;
	}

	private bool DoesFaceOverlapBounds( FaceHandle face, BBox bounds )
	{
		if ( !GetVerticesConnectedToFace( face, out var vertices ) )
			return false;

		var expanded = new BBox( bounds.Mins - 1.0f, bounds.Maxs + 1.0f );
		var faceMin = new Vector3( float.MaxValue );
		var faceMax = new Vector3( float.MinValue );

		foreach ( var vertex in vertices )
		{
			var pos = GetVertexPosition( vertex );
			faceMin = Vector3.Min( faceMin, pos );
			faceMax = Vector3.Max( faceMax, pos );
		}

		var faceBounds = new BBox( faceMin, faceMax );
		return faceBounds.Overlaps( expanded );
	}

	private static List<Plane> CollectUniquePlanes( PolygonMesh mesh, Transform transform )
	{
		var planes = new List<Plane>();

		foreach ( var face in mesh.Topology.FaceHandles )
		{
			mesh.GetFacePlane( face, transform, out var plane );

			bool isDuplicate = planes.Any( existing =>
				Vector3.Dot( plane.Normal, existing.Normal ) > 0.999f &&
				MathF.Abs( plane.GetDistance( Vector3.Zero ) - existing.GetDistance( Vector3.Zero ) ) < 0.01f
			);

			if ( !isDuplicate )
				planes.Add( plane );
		}

		return planes;
	}

	private static void DissolveCoplanarEdges( PolygonMesh mesh )
	{
		var edgesToDissolve = new List<HalfEdgeHandle>();
		var seen = new HashSet<int>();

		foreach ( var edge in mesh.HalfEdgeHandles )
		{
			var fullEdge = mesh.Topology.GetFullEdgeForHalfEdge( edge );
			if ( !fullEdge.IsValid || !seen.Add( fullEdge.Index ) )
				continue;

			mesh.Topology.GetFacesConnectedToFullEdge( fullEdge, out var faceA, out var faceB );
			if ( faceA == FaceHandle.Invalid || faceB == FaceHandle.Invalid )
				continue;

			mesh.ComputeFaceNormal( faceA, out var normalA );
			mesh.ComputeFaceNormal( faceB, out var normalB );

			if ( normalA.Normal.Dot( normalB.Normal ) > 0.999f )
				edgesToDissolve.Add( fullEdge );
		}

		if ( edgesToDissolve.Count > 0 )
			mesh.DissolveEdges( edgesToDissolve, true, DissolveRemoveVertexCondition.Colinear );
	}

	private bool HandleNonOverlapping( PolygonMesh other, Transform relativeTransform, BooleanOperation operation ) => operation switch
	{
		BooleanOperation.Union => MergeMeshAndReturn( other, relativeTransform ),
		BooleanOperation.Subtract => true,
		BooleanOperation.Intersect => RemoveAllFacesAndReturn(),
		_ => false
	};

	private bool MergeMeshAndReturn( PolygonMesh other, Transform transform )
	{
		MergeMesh( other, transform, out _, out _, out _ );
		return true;
	}

	private bool RemoveAllFacesAndReturn()
	{
		RemoveFaces( FaceHandles.ToList() );
		return true;
	}
}
