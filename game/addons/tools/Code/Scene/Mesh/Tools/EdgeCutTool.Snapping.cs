using HalfEdgeMesh;
using System.Runtime.InteropServices;

namespace Editor.MeshEditor;

partial class EdgeCutTool
{
	enum EdgeCutAlignment { None, Grid, Perpendicular }
	enum PolygonComponentType { Invalid = -1, Vertex, Edge, Face }

	struct SnapPoint
	{
		public Vector3 Position;
		public EdgeCutAlignment Alignment;
		public PolygonComponentType Type;
		public VertexHandle Vertex;
		public HalfEdgeHandle Edge;

		public SnapPoint( Vector3 pos, EdgeCutAlignment align )
		{
			Position = pos;
			Alignment = align;
			Type = PolygonComponentType.Face;
			Vertex = default;
			Edge = default;
		}

		public SnapPoint( VertexHandle v, Vector3 pos )
		{
			Position = pos;
			Alignment = EdgeCutAlignment.None;
			Type = PolygonComponentType.Vertex;
			Vertex = v;
			Edge = default;
		}

		public SnapPoint( HalfEdgeHandle e, Vector3 pos, EdgeCutAlignment align )
		{
			Position = pos;
			Alignment = align;
			Type = PolygonComponentType.Edge;
			Vertex = default;
			Edge = e;
		}
	}

	static bool ShouldSnap() => Gizmo.Settings.SnapToGrid != Gizmo.IsCtrlPressed;

	MeshCutPoint FindSnappedCutPoint()
	{
		var face = MeshTrace.TraceFace( SelectionSampleRadius, out var point );
		if ( !face.IsValid() ) return default;

		GenerateSnapPoints( face, point, out var snapPoints );
		return SnapCutPoint( face, point, snapPoints );
	}

	static MeshCutPoint SnapCutPoint( MeshFace face, Vector3 target, List<SnapPoint> snaps )
	{
		if ( snaps.Count == 0 ) return default;

		var best = snaps.OrderBy( s => target.DistanceSquared( s.Position ) ).First();
		return best.Type switch
		{
			PolygonComponentType.Vertex => new( face, new MeshVertex( face.Component, best.Vertex ) ),
			PolygonComponentType.Edge => new( face, new MeshEdge( face.Component, best.Edge ), best.Position ),
			PolygonComponentType.Face => new( face, best.Position ),
			_ => default
		};
	}

	static void GenerateVertexSnapPoints( MeshFace face, List<SnapPoint> snaps )
	{
		if ( !face.IsValid() ) return;

		var mesh = face.Component.Mesh;
		var transform = face.Component.WorldTransform;

		mesh.GetVerticesConnectedToFace( face.Handle, out var vertices );

		foreach ( var v in vertices )
		{
			mesh.GetVertexPosition( v, transform, out var pos );
			snaps.Add( new SnapPoint( v, pos ) );
		}
	}

	void GenerateEdgeSnapPoints( MeshFace face, Vector3 target, List<SnapPoint> snaps )
	{
		if ( !face.IsValid() ) return;

		var component = face.Component;
		if ( !component.IsValid() ) return;

		var mesh = component.Mesh;
		var faceHandle = face.Handle;

		var hasPrevious = _cutPoints.Count > 0;
		var previous = hasPrevious ? _cutPoints.Last() : default;
		var perpendicularNormal = Vector3.Zero;
		var hasPerpendicular = hasPrevious && ComputePerpendicularPlaneNormalForCutPoint( previous, out perpendicularNormal );

		mesh.GetEdgesConnectedToFace( faceHandle, out var edges );

		for ( var i = 0; i < edges.Count; i++ )
		{
			var edge = edges[i];

			mesh.GetVerticesConnectedToEdge( edge, out var vA, out var vB );
			mesh.GetVertexPosition( vA, component.WorldTransform, out var p0 );
			mesh.GetVertexPosition( vB, component.WorldTransform, out var p1 );

			snaps.Add( new SnapPoint( edge, p0.LerpTo( p1, 0.5f ), EdgeCutAlignment.None ) );

			if ( SnapToEdge( target, p0, p1, float.MaxValue, true, out var edgeSnap ) )
				snaps.Add( new SnapPoint( edge, edgeSnap, EdgeCutAlignment.None ) );

			if ( !hasPrevious || previous.Component != component )
				continue;

			var prevPos = previous.WorldPosition;

			if ( ComputePlaneIntersectionSnapPoint( Vector3.Right, prevPos, target, p0, p1, out var hit ) )
				snaps.Add( new SnapPoint( edge, hit, EdgeCutAlignment.Grid ) );

			if ( ComputePlaneIntersectionSnapPoint( Vector3.Up, prevPos, target, p0, p1, out hit ) )
				snaps.Add( new SnapPoint( edge, hit, EdgeCutAlignment.Grid ) );

			if ( ComputePlaneIntersectionSnapPoint( Vector3.Forward, prevPos, target, p0, p1, out hit ) )
				snaps.Add( new SnapPoint( edge, hit, EdgeCutAlignment.Grid ) );

			if ( hasPerpendicular && ComputePlaneIntersectionSnapPoint( perpendicularNormal, prevPos, target, p0, p1, out hit ) )
			{
				snaps.Add( new SnapPoint( edge, hit, EdgeCutAlignment.Perpendicular ) );
			}
		}
	}

	static void GenerateFaceSnapPoints( MeshFace face, Vector3 target, List<SnapPoint> snaps )
	{
		if ( !face.IsValid() ) return;

		var component = face.Component;
		if ( !component.IsValid() ) return;

		var mesh = component.Mesh;
		var faceHandle = face.Handle;

		var vertices = mesh.GetFaceVertexPositions( faceHandle, component.WorldTransform ).ToList();
		var spacing = Gizmo.Settings.GridSpacing;

		var min = new Vector3(
			MathF.Floor( target.x / spacing ) * spacing,
			MathF.Floor( target.y / spacing ) * spacing,
			MathF.Floor( target.z / spacing ) * spacing );

		var max = new Vector3(
			MathF.Ceiling( target.x / spacing ) * spacing,
			MathF.Ceiling( target.y / spacing ) * spacing,
			MathF.Ceiling( target.z / spacing ) * spacing );

		var gridEdges = new Line[]
		{
			new( new( min.x - 1, min.y, min.z ), new( max.x + 1, min.y, min.z ) ),
			new( new( min.x - 1, min.y, max.z ), new( max.x + 1, min.y, max.z ) ),
			new( new( min.x - 1, max.y, min.z ), new( max.x + 1, max.y, min.z ) ),
			new( new( min.x - 1, max.y, max.z ), new( max.x + 1, max.y, max.z ) ),

			new( new( min.x, min.y - 1, min.z ), new( min.x, max.y + 1, min.z ) ),
			new( new( min.x, min.y - 1, max.z ), new( min.x, max.y + 1, max.z ) ),
			new( new( max.x, min.y - 1, min.z ), new( max.x, max.y + 1, min.z ) ),
			new( new( max.x, min.y - 1, max.z ), new( max.x, max.y + 1, max.z ) ),

			new( new( min.x, min.y, min.z - 1 ), new( min.x, min.y, max.z + 1 ) ),
			new( new( min.x, max.y, min.z - 1 ), new( min.x, max.y, max.z + 1 ) ),
			new( new( max.x, min.y, min.z - 1 ), new( max.x, min.y, max.z + 1 ) ),
			new( new( max.x, max.y, min.z - 1 ), new( max.x, max.y, max.z + 1 ) ),
		};

		var indices = Mesh.TriangulatePolygon( CollectionsMarshal.AsSpan( vertices ) );

		for ( var i = 0; i < gridEdges.Length; i++ )
		{
			var e = gridEdges[i];
			if ( PolygonIntersectLineSegment( vertices, indices, e.Start, e.End, out var hit ) )
				snaps.Add( new SnapPoint( hit, EdgeCutAlignment.None ) );
		}
	}

	void GenerateSnapPoints( MeshFace face, Vector3 target, out List<SnapPoint> snapPoints )
	{
		var alignedOnly = _cutPoints.Count > 0 && Gizmo.IsShiftPressed;

		var all = new List<SnapPoint>();
		GenerateVertexSnapPoints( face, all );
		GenerateEdgeSnapPoints( face, target, all );

		if ( _cutPoints.Count > 0 )
			GenerateFaceSnapPoints( face, target, all );

		snapPoints = new List<SnapPoint>( all.Count );

		const float minDistSq = 0.01f * 0.01f;

		foreach ( var snap in all )
		{
			if ( alignedOnly && snap.Alignment == EdgeCutAlignment.None )
				continue;

			var tooClose = false;
			for ( var i = 0; i < snapPoints.Count; i++ )
			{
				if ( snapPoints[i].Position.DistanceSquared( snap.Position ) < minDistSq )
				{
					tooClose = true;
					break;
				}
			}

			if ( !tooClose )
				snapPoints.Add( snap );
		}
	}

	static bool ComputePerpendicularPlaneNormalForCutPoint( MeshCutPoint cutPoint, out Vector3 planeNormal )
	{
		planeNormal = default;

		if ( !cutPoint.Component.IsValid() || !cutPoint.Face.IsValid() || !cutPoint.Edge.IsValid() )
			return false;

		var mesh = cutPoint.Component.Mesh;
		mesh.GetVerticesConnectedToEdge( cutPoint.Edge.Handle, cutPoint.Face.Handle, out var a, out var b );
		mesh.GetVertexPosition( a, Transform.Zero, out var pa );
		mesh.GetVertexPosition( b, Transform.Zero, out var pb );

		planeNormal = (pb - pa).Normal;
		return true;
	}

	static bool SnapToEdge( Vector3 original, Vector3 a, Vector3 b, float maxDistance, bool gridSnap, out Vector3 snapped )
	{
		ClosestPointOnLineSegment( original, a, b, out var pointOnLine );

		var maxDistSq = maxDistance < float.MaxValue ? maxDistance * maxDistance : float.MaxValue;
		var snappedPoint = Vector3.Zero;
		var snappedOk = false;

		if ( pointOnLine.DistanceSquared( original ) < maxDistSq )
		{
			if ( gridSnap )
			{
				if ( IntersectRayWithGrid( pointOnLine, a, out var snapA ) &&
					 snapA.DistanceSquared( original ) < maxDistSq )
				{
					snappedPoint = snapA;
					snappedOk = true;
				}

				if ( IntersectRayWithGrid( pointOnLine, b, out var snapB ) &&
					 snapB.DistanceSquared( original ) < maxDistSq &&
					 pointOnLine.DistanceSquared( snapB ) < pointOnLine.DistanceSquared( snappedPoint ) )
				{
					snappedPoint = snapB;
					snappedOk = true;
				}
			}
			else
			{
				snappedPoint = pointOnLine;
				snappedOk = true;
			}
		}

		snapped = snappedOk ? snappedPoint : Vector3.Zero;
		return snappedOk;
	}

	static float DistanceSqrToLine( Vector3 p, Vector3 a, Vector3 b, out float t )
	{
		ClosestPointOnLine( p, a, b, out var closest, out t );
		return p.DistanceSquared( closest );
	}

	static bool ComputePlaneIntersectionSnapPoint( Vector3 vPlaneNormal, Vector3 vPlanePoint, Vector3 vOrginalPoint, Vector3 vEdgePointA, Vector3 vEdgePointB, out Vector3 pOutIntersectionPoint )
	{
		pOutIntersectionPoint = Vector3.Zero;

		const float halfGridSize = 0.125f * 0.5f;

		var vDelta = (vEdgePointA - vEdgePointB) * vPlaneNormal;

		if ( vDelta.Length >= halfGridSize )
		{
			var plane = new Plane( vPlaneNormal, vPlaneNormal.Dot( vPlanePoint ) );
			var vIntersection = plane.IntersectLine( vEdgePointA, vEdgePointB );
			if ( vIntersection.HasValue )
			{
				pOutIntersectionPoint = vIntersection.Value;
				return true;
			}
		}

		return false;
	}

	static bool InsideTriangle( Vector3 a, Vector3 b, Vector3 c, Vector3 p, Vector3 normal )
	{
		const float eps = 1e-7f;
		const float maxEdgeDistSq = eps * eps;

		if ( Vector3.Dot( Vector3.Cross( b - a, p - a ), normal ) >= -eps &&
			 Vector3.Dot( Vector3.Cross( c - b, p - b ), normal ) >= -eps &&
			 Vector3.Dot( Vector3.Cross( a - c, p - c ), normal ) >= -eps
		)
			return true;

		if ( DistanceSqrToLine( p, a, b, out var t ) < maxEdgeDistSq && t is >= 0f and <= 1f ) return true;
		if ( DistanceSqrToLine( p, b, c, out t ) < maxEdgeDistSq && t is >= 0f and <= 1f ) return true;
		if ( DistanceSqrToLine( p, c, a, out t ) < maxEdgeDistSq && t is >= 0f and <= 1f ) return true;

		return false;
	}

	static bool PolygonIntersectLineSegment( List<Vector3> vertices, Span<int> indices, Vector3 a, Vector3 b, out Vector3 intersection )
	{
		intersection = Vector3.Zero;

		for ( var i = 0; i + 2 < indices.Length; i += 3 )
		{
			var v0 = vertices[indices[i]];
			var v1 = vertices[indices[i + 1]];
			var v2 = vertices[indices[i + 2]];

			var normal = Vector3.Cross( v1 - v0, v2 - v0 );
			if ( normal.LengthSquared < 1e-8f ) continue;

			normal = normal.Normal;
			var plane = new Plane( v0, normal );

			var hit = plane.IntersectLine( a, b );
			if ( !hit.HasValue ) continue;

			var p = hit.Value;
			if ( InsideTriangle( v0, v1, v2, p, normal ) )
			{
				intersection = p;
				return true;
			}
		}

		return false;
	}

	static void ClosestPointOnLineSegment( Vector3 p, Vector3 a, Vector3 b, out Vector3 closest )
	{
		var d = b - a;
		var len2 = d.Dot( d );
		var t = len2 < 1e-5f ? 0f : Math.Clamp( d.Dot( p - a ) / len2, 0f, 1f );
		closest = a + d * t;
	}

	static bool IntersectRayWithGrid( Vector3 origin, Vector3 end, out Vector3 intersection )
	{
		var spacing = Gizmo.Settings.GridSpacing;
		var dir = end - origin;

		var cell = new Vector3(
			MathF.Floor( origin.x / spacing ) * spacing,
			MathF.Floor( origin.y / spacing ) * spacing,
			MathF.Floor( origin.z / spacing ) * spacing
		);

		var hit = false;
		var closestT = 1f;

		for ( var axis = 0; axis < 3; axis++ )
		{
			var d = dir[axis];
			if ( d == 0f )
				continue;

			var sign = d > 0f ? 1f : -1f;
			var plane = cell[axis] + (sign > 0f ? spacing : 0f);

			var t = (plane - origin[axis]) / d;
			if ( t > 0f && t <= closestT )
			{
				closestT = t;
				hit = true;
			}
		}

		intersection = hit ? origin + dir * closestT : Vector3.Zero;
		return hit;
	}
}
