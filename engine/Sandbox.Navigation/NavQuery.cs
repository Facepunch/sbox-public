using System.Numerics;

namespace Nav;

/// <summary>
/// Query context for one thread. Any number of them may run at once, also while the mesh commits:
/// each call reads the mesh state once and works on that snapshot. Heights are polygon-plane heights.
/// Every call that takes a filter throws for one not made with NavFilter.Create.
/// </summary>
public sealed partial class NavQuery
{
	readonly MeshAgentType mesh;
	readonly float tileWorld, climb;

	// Lengths: epsilon is a tenth of a unit at a cell of 4. Area-like terms scale with the cell squared.
	readonly float epsilon, areaScale;

	/// <summary>
	/// A query of a mesh of one agent type. Throws for a mesh of several: name the type.
	/// </summary>
	public NavQuery( NavMesh mesh ) : this( mesh.Single )
	{
	}

	/// <summary>
	/// A query of one agent type of the mesh, for as long as it lives. A PolyRef of another type fails in
	/// it like a stale one. Throws for a type the mesh does not have.
	/// </summary>
	public NavQuery( NavMesh mesh, int agentType ) : this( mesh.TypeAt( agentType ) )
	{
	}

	internal NavQuery( MeshAgentType mesh )
	{
		this.mesh = mesh;
		var settings = mesh.Settings;
		tileWorld = mesh.TileWorld;
		climb = settings.AgentClimb;
		epsilon = mesh.Epsilon;
		areaScale = mesh.AreaScale;
	}

	public int AgentType => mesh.Index;

	/// <summary>
	/// The nearest polygon among those whose bounds touch the box. A polygon directly above or below
	/// within the agent climb beats a nearer one sideways. Infinite half extents search the whole mesh.
	/// </summary>
	public bool FindNearest( Vector3 point, Vector3 halfExtents, in NavFilter filter, out PolyRef poly, out Vector3 nearest )
	{
		filter.Check();
		var s = mesh.State;
		bool found = Nearest( s, point, halfExtents, filter, null, 0, out uint id, out nearest );
		poly = found ? s.RefOf( id ) : default;
		return found;
	}

	/// <summary>
	/// False when the point is not over the polygon.
	/// </summary>
	public bool GetHeight( PolyRef poly, Vector3 point, out float z )
	{
		z = 0;
		if ( !mesh.State.TryGet( poly, out var tile, out int index ) ) return false;
		Span<Vector3> v = stackalloc Vector3[Poly.MaxVerts];
		int n = Gather( tile, in tile.Polys[index], v );
		return HeightOver( v[..n], point, out z );
	}

	/// <summary>
	/// The point of a polygon that is nearest to point: on its plane, straight above or below, when
	/// point is over the polygon, else on its nearest edge. False for a stale polygon and for a link.
	/// </summary>
	public bool ClosestPoint( PolyRef poly, Vector3 point, out Vector3 closest )
	{
		closest = point;
		if ( !mesh.State.TryGet( poly, out var tile, out int index ) || !Geo.IsFinite( point ) ) return false;
		closest = ClosestOnPoly( tile, index, point, out _ );
		return true;
	}

	/// <summary>
	/// Whether poly still names a polygon or link of this mesh that the filter allows. For a PolyRef
	/// that came from another mesh the answer means nothing.
	/// </summary>
	public bool IsValid( PolyRef poly, in NavFilter filter )
	{
		filter.Check();
		return mesh.State.IsValid( poly, filter );
	}

	/// <summary>
	/// The ends of a link in travel direction for someone standing at from: a link that can be taken
	/// both ways is entered at the end nearer to from, whatever the corridor says. Pass where the
	/// traveller is when it reaches the link.
	/// </summary>
	public bool GetLink( PolyRef link, Vector3 from, out Vector3 entry, out Vector3 exit )
	{
		entry = exit = default;
		ref readonly var found = ref mesh.State.LinkAt( link.Link );
		if ( found.Serial != link.Serial || !found.Connected ) return false;
		bool reverse = found.Def.Bidirectional && Vector3.DistanceSquared( from, found.EndPoint ) < Vector3.DistanceSquared( from, found.StartPoint );
		entry = reverse ? found.EndPoint : found.StartPoint;
		exit = reverse ? found.StartPoint : found.EndPoint;
		return true;
	}

	/// <summary>
	/// Snaps both points, searches and pulls the corners, all on one mesh state. halfExtents is how far
	/// off the mesh a point may be and still snap.
	/// </summary>
	public PathStatus FindPath( Vector3 start, Vector3 end, Vector3 halfExtents, in NavFilter filter, NavPath path )
	{
		filter.Check();
		var s = mesh.State;
		if ( !Nearest( s, start, halfExtents, filter, null, 0, out uint from, out var fromPoint ) )
		{
			path.Clear( PathStatus.StartNotFound );
			return PathStatus.StartNotFound;
		}
		if ( !Nearest( s, end, halfExtents, filter, null, 0, out uint to, out var toPoint ) )
		{
			path.Clear( PathStatus.EndNotFound );
			return PathStatus.EndNotFound;
		}
		return FindPath( s, from, fromPoint, to, toPoint, filter, path );
	}

	/// <summary>
	/// For points that are already on their polygons. A stale polygon gives NotFound.
	/// </summary>
	public PathStatus FindPath( PolyRef startPoly, Vector3 start, PolyRef endPoly, Vector3 end, in NavFilter filter, NavPath path )
	{
		filter.Check();
		var s = mesh.State;
		if ( !s.TryGet( startPoly, out _, out _ ) || !s.TryGet( endPoly, out _, out _ ) || !Geo.IsFinite( start ) || !Geo.IsFinite( end ) )
		{
			path.Clear( PathStatus.NotFound );
			return PathStatus.NotFound;
		}
		return FindPath( s, startPoly.Id, start, endPoly.Id, end, filter, path );
	}

	PathStatus FindPath( MeshState s, uint from, Vector3 start, uint to, Vector3 end, in NavFilter filter, NavPath path, int maxExpansions = int.MaxValue )
	{
		var status = SearchCorridor( s, from, start, to, ref end, filter, ref path.polys, out int count, maxExpansions );
		if ( status == PathStatus.NotFound )
		{
			path.Clear( PathStatus.NotFound );
			return PathStatus.NotFound;
		}
		path.polyCount = count;

		if ( path.corners.Length < count * 2 + 2 ) path.corners = new Vector3[Math.Max( count * 2 + 2, path.corners.Length * 2 )];
		int corners = Funnel( s, path.Polys, start, end, path.corners, default, false );
		if ( corners == 0 )
		{
			path.Clear( PathStatus.NotFound );
			return PathStatus.NotFound;
		}

		float length = 0;
		for ( int i = 1; i < corners; i++ ) length += Vector3.Distance( path.corners[i - 1], path.corners[i] );
		path.cornerCount = corners;
		path.Start = start;
		path.End = end;
		path.Length = length;
		return path.Status = status;
	}

	// Searches and writes the corridor. On a partial path end becomes the point of the last polygon
	// nearest to it.
	PathStatus SearchCorridor( MeshState s, uint from, Vector3 start, uint to, ref Vector3 end, in NavFilter filter, ref PolyRef[] corridor, out int count, int maxExpansions = int.MaxValue )
	{
		Search( s, from, to, start, end, filter, maxExpansions );
		count = Corridor( s, searchEnd, ref corridor, 0 );
		if ( count == 0 ) return PathStatus.NotFound;
		uint last = corridor[count - 1].Id;
		if ( last == to ) return PathStatus.Complete;
		end = ClosestOnPoly( s.Get( last ), (int)(last & s.PolyMask), end, out _ );
		return PathStatus.Partial;
	}

	// The path search on a pinned state, without corners: the corridor from start to polygon to, or as near
	// as the mesh allows, and where it ends.
	internal PathStatus FindCorridor( MeshState s, PolyRef start, Vector3 from, uint to, Vector3 end, in NavFilter filter, ref PolyRef[] corridor, out int count, out Vector3 reached )
	{
		count = 0;
		reached = end;
		if ( !s.TryGet( start, out _, out _ ) || !Geo.IsFinite( from ) || !Geo.IsFinite( end ) ) return PathStatus.NotFound;
		return SearchCorridor( s, start.Id, from, to, ref reached, filter, ref corridor, out count );
	}

	/// <summary>
	/// String-pulls a corridor. Stops at the entry of the first link, which is then the last corner.
	/// Returns the number of corners written.
	/// </summary>
	public int FindCorners( ReadOnlySpan<PolyRef> corridor, Vector3 from, Vector3 to, Span<Vector3> corners )
	{
		return Funnel( mesh.State, corridor, from, to, corners, default, true );
	}

	/// <summary>
	/// Slides position by delta along the surface, stopping at walls. visited receives the polygons
	/// crossed, first to last. False when the polygon is stale.
	/// </summary>
	public bool Move( ref PolyRef poly, ref Vector3 position, Vector3 delta, in NavFilter filter, Span<PolyRef> visited, out int visitedCount )
	{
		filter.Check();
		return MoveOnSurface( mesh.State, ref poly, ref position, delta, filter, visited, out visitedCount );
	}

	/// <summary>
	/// Collects the nearest wall segments of the locally connected surface, nearest first, two points
	/// each. Closed parts of tile borders count as walls. Returns the number of segments.
	/// </summary>
	public int FindWalls( in WallRequest request, in NavFilter filter, Span<Vector3> segments )
	{
		filter.Check();
		var s = mesh.State;
		if ( !s.TryGet( request.Poly, out var tile, out int index ) || !filter.Allows( tile.Polys[index].Area ) || !Geo.IsFinite( request.Center ) ) return 0;
		return Walls( s, request.Poly.Id, request.Center, request.Radius, request.Height, filter, segments, false );
	}

	/// <summary>
	/// False when no wall is within maxDistance.
	/// </summary>
	public bool FindNearestWall( PolyRef poly, Vector3 point, float maxDistance, in NavFilter filter, out Vector3 wallPoint )
	{
		filter.Check();
		wallPoint = point;
		var s = mesh.State;
		if ( !s.TryGet( poly, out var tile, out int index ) || !filter.Allows( tile.Polys[index].Area ) || !Geo.IsFinite( point ) || !(maxDistance >= 0) ) return false;
		Span<Vector3> segment = stackalloc Vector3[2];
		if ( Walls( s, poly.Id, point, maxDistance, float.PositiveInfinity, filter, segment, true ) == 0 ) return false;
		Geo.DistanceToSegmentSquared( point, segment[0], segment[1], out float t );
		wallPoint = Vector3.Lerp( segment[0], segment[1], t );
		return true;
	}
}
