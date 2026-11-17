using Nav;
using System.Collections.Concurrent;

namespace Sandbox.Navigation;

/// <summary>
/// Navigation Mesh - allowing AI to navigate a world
/// </summary>
public sealed partial class NavMesh
{
	// One per thread that is asking at the moment. Queries take no lock.
	private sealed record QueryScratch( Nav.NavMesh Mesh, NavQuery Query, NavPath Path );
	private readonly ConcurrentQueue<QueryScratch> queryPool = new();

	private QueryScratch RentQuery()
	{
		var mesh = this.mesh;
		if ( mesh is null ) return null;
		while ( queryPool.TryDequeue( out var scratch ) )
		{
			if ( scratch.Mesh == mesh ) return scratch;
		}
		return new QueryScratch( mesh, new NavQuery( mesh ), new NavPath() );
	}

	private void ReturnQuery( QueryScratch scratch )
	{
		if ( scratch.Mesh == mesh ) queryPool.Enqueue( scratch );
	}

	[Obsolete( "Use CalculatePath instead" )]
	public List<Vector3> GetSimplePath( Vector3 from, Vector3 to )
	{
		var path = CalculatePath( new() { Start = from, Target = to } );
		return path.Points.Select( point => point.Position ).ToList();
	}

	/// <summary>
	/// Computes a navigation path between the specified start and target positions on the navmesh.
	/// Uses the same pathfinding algorithm as <see cref="NavMeshAgent"/>, taking agent configuration into account if provided.
	/// The result is suitable for direct use with <see cref="NavMeshAgent.SetPath"/>.
	/// If a complete path cannot be found, the result may indicate an incomplete or failed path.
	/// </summary>
	public NavMeshPath CalculatePath( CalculatePathRequest request )
	{
		var (filter, extents) = CapturePathSettings( request );
		return CalculatePathCore( RentQuery(), request.Start, request.Target, extents, filter );
	}

	/// <summary>
	/// Captures agent constraints when called and calculates on a worker. Cancellation
	/// is checked before and after the search.
	/// </summary>
	public Task<NavMeshPath> CalculatePathAsync( CalculatePathRequest request, System.Threading.CancellationToken cancellationToken = default )
	{
		var (filter, extents) = CapturePathSettings( request );
		var scratch = RentQuery();
		return Task.Run( () =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			// The mesh was reset, disposed or replaced since the call
			if ( scratch is not null && scratch.Mesh != mesh ) return new NavMeshPath { Status = NavMeshPathStatus.PathNotFound };
			var path = CalculatePathCore( scratch, request.Start, request.Target, extents, filter );
			cancellationToken.ThrowIfCancellationRequested();
			return path;
		}, cancellationToken );
	}

	private (NavFilter Filter, Vector3 Extents) CapturePathSettings( CalculatePathRequest request )
	{
		float height = AgentHeight;
		NavFilter filter;
		if ( request.Agent?.agentInternal is SimulationAgent agent && agent.Owner == Simulation && !agent.Removed )
		{
			// Workers never enumerate the component's area sets
			var options = agent.Options;
			height = MathF.Max( height, options.Height );
			filter = ThreadSafe.IsMainThread ? CreateFilter( request.Agent ) : options.Filter;
		}
		else
		{
			if ( request.Agent is not null && !ThreadSafe.IsMainThread )
				throw new InvalidOperationException( "Worker queries require an active agent on this navmesh." );
			filter = CreateFilter( request.Agent );
			height = MathF.Max( height, request.Agent?.Height ?? height );
		}
		return (filter, new Vector3( SnapDistance, SnapDistance, height * SnapHeights ));
	}

	private NavMeshPath CalculatePathCore( QueryScratch scratch, Vector3 start, Vector3 target, Vector3 extents, NavFilter filter )
	{
		if ( scratch is null ) return new() { Status = NavMeshPathStatus.StartNotFound };

		scratch.Query.FindPath( start, target, extents, filter, scratch.Path );
		var result = CreatePathResult( scratch.Mesh, scratch.Path, target );
		ReturnQuery( scratch );
		return result;
	}

	// Results own their arrays, the library path is scratch
	internal static NavMeshPath CreatePathResult( Nav.NavMesh mesh, NavPath path, Vector3 requestedTarget )
	{
		if ( !path.IsValid ) return new() { Status = (NavMeshPathStatus)path.Status };

		var corners = path.Corners;
		var points = new NavMeshPathPoint[corners.Length];
		for ( int i = 0; i < points.Length; i++ ) points[i] = new() { Position = corners[i] };
		return new() { Mesh = mesh, RequestedTarget = requestedTarget, Polygons = path.Polys.ToArray(), Points = Array.AsReadOnly( points ), Status = (NavMeshPathStatus)path.Status };
	}

	public Vector3? GetRandomPoint()
	{
		return RandomPoint( default, float.PositiveInfinity );
	}

	/// <summary>
	/// Get a random point on the navmesh, within the bounding box.
	/// This will return null if it can't find a point on the navmesh in a few tries.
	/// </summary>
	public Vector3? GetRandomPoint( BBox box )
	{
		// The disc around the box, and no further up or down than the box reaches
		var extents = box.Extents;
		float radius = MathF.Max( MathF.Sqrt( extents.x * extents.x + extents.y * extents.y ), extents.z );
		for ( int i = 0; i < 16; i++ )
		{
			if ( RandomPoint( box.Center, radius ) is Vector3 point && box.Contains( point ) ) return point;
		}

		return null;
	}

	/// <summary>
	/// Get a random point on the navmesh, within the sphere.
	/// This will return null if it can't find a point on the navmesh in a few tries.
	/// </summary>
	public Vector3? GetRandomPoint( Vector3 position, float radius )
	{
		var sphere = new Sphere( position, radius );
		for ( int i = 0; i < 10; i++ )
		{
			if ( RandomPoint( position, radius ) is Vector3 point && sphere.Contains( point ) ) return point;
		}

		return null;
	}

	private Vector3? RandomPoint( Vector3 center, float radius )
	{
		var scratch = RentQuery();
		if ( scratch is null ) return null;

		uint seed = (uint)Random.Shared.Next();
		bool found = scratch.Query.FindRandomPoint( center, radius, default, ref seed, out _, out var point );
		ReturnQuery( scratch );
		return found ? (Vector3)point : null;
	}

	public Vector3? GetClosestPoint( BBox box )
	{
		var scratch = RentQuery();
		if ( scratch is null ) return null;

		bool found = scratch.Query.FindNearest( box.Center, box.Extents, default, out _, out var nearest );
		ReturnQuery( scratch );
		return found ? (Vector3)nearest : null;
	}

	public Vector3? GetClosestPoint( Vector3 position, float radius = 1024.0f ) => GetClosestPoint( BBox.FromPositionAndSize( position, radius * 2.0f ) );

	/// <summary>
	/// The closest point on the outline of the navmesh, seen from the closest point on the navmesh in
	/// the box. Null when there is no navmesh in the box or no outline within its half width of that point.
	/// </summary>
	public Vector3? GetClosestEdge( BBox box )
	{
		var scratch = RentQuery();
		if ( scratch is null ) return null;

		var extents = box.Extents;
		bool found = scratch.Query.FindNearest( box.Center, extents, default, out var poly, out var nearest )
			&& scratch.Query.FindNearestWall( poly, nearest, MathF.Max( extents.x, extents.y ), default, out nearest );
		ReturnQuery( scratch );
		return found ? (Vector3)nearest : null;
	}

	public Vector3? GetClosestEdge( Vector3 position, float radius = 1024.0f ) => GetClosestEdge( BBox.FromPositionAndSize( position, radius * 2.0f ) );
}

/// <summary>
/// Defines the input for a pathfinding request on the navmesh.
/// </summary>
public struct CalculatePathRequest
{
	/// <summary>
	/// Start position of the path, should be close to the navmesh.
	/// </summary>
	public Vector3 Start;
	/// <summary>
	/// Target/End position of the path, should be close to the navmesh.
	/// </summary>
	public Vector3 Target;

	/// <summary>
	/// Optional agent whose configuration is used for path calculation.
	/// </summary>
	public NavMeshAgent Agent;

}

/// <summary>
/// Contains the result of a pathfinding operation.
/// </summary>
[Expose]
public struct NavMeshPath : IValid
{
	/// <summary>
	/// The outcome of the path calculation.
	/// </summary>
	public NavMeshPathStatus Status { get; internal set; }

	/// <summary>
	/// True if a path was found.
	/// </summary>
	public readonly bool IsValid => Status == NavMeshPathStatus.Partial || Status == NavMeshPathStatus.Complete;

	/// <summary>
	/// Polygons traversed by the path.
	/// Internal for now as you cannot do anything with polygon ids yet.
	/// </summary>
	internal PolyRef[] Polygons;

	// The library mesh the polygons belong to
	internal Nav.NavMesh Mesh;
	internal Vector3 RequestedTarget;

	private IReadOnlyList<NavMeshPathPoint> points;

	/// <summary>
	/// Points along the path. Empty when no path was found.
	/// </summary>
	public IReadOnlyList<NavMeshPathPoint> Points { readonly get => points ?? []; internal set => points = value; }
}

/// <summary>
/// Describes the outcome of a navigation path query.
/// </summary>
[Expose]
public enum NavMeshPathStatus
{
	/// <summary>
	/// Start location was not found on the navmesh.
	/// </summary>
	StartNotFound,
	/// <summary>
	/// Target location was not found on the navmesh.
	/// </summary>
	TargetNotFound,
	/// <summary>
	/// No path could be found.
	/// </summary>
	PathNotFound,
	/// <summary>
	/// Path found, but does not reach the target.
	/// The returned path will be to the closest location that can be reached.
	/// </summary>
	Partial,
	/// <summary>
	/// Path found from start to target.
	/// </summary>
	Complete,
}

/// <summary>
/// Represents a point in a navmesh path, including its position in 3D space.
/// May be extended in the future to hold more information about the point.
/// </summary>
[Expose]
public struct NavMeshPathPoint
{
	/// <summary>
	/// The world position of this point along the path.
	/// </summary>
	public Vector3 Position;
}
