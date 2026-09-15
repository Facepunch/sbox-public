using Sandbox.Navigation.Pathfinding;
using System.Buffers;

namespace Sandbox.Navigation;

/// <summary>
/// Navigation Mesh - allowing AI to navigate a world
/// </summary>
public sealed partial class NavMesh
{
	private readonly List<long> pathPolygons = new();
	private TraversalFilter defaultFilter = TraversalFilter.Unrestricted;

	[Obsolete( "Use CalculatePath instead" )]
	public List<Vector3> GetSimplePath( Vector3 from, Vector3 to )
	{
		var path = CalculatePath( new() { Start = from, Target = to } );
		return path.Points?.Select( point => point.Position ).ToList() ?? [];
	}

	/// <summary>
	/// Computes a navigation path between the specified start and target positions on the navmesh.
	/// Uses the same pathfinding algorithm as <see cref="NavMeshAgent"/>, taking agent configuration into account if provided.
	/// The result is suitable for direct use with <see cref="NavMeshAgent.SetPath"/>.
	/// If a complete path cannot be found, the result may indicate an incomplete or failed path.
	/// </summary>
	public NavMeshPath CalculatePath( CalculatePathRequest request )
	{
		lock ( SyncRoot )
		{
			if ( query is null ) return new() { Status = NavMeshPathStatus.StartNotFound };
			var (filter, extents) = CapturePathSettings( request );
			return CalculatePathCore( request.Start, request.Target, extents, filter, default );
		}
	}

	/// <summary>
	/// Captures agent constraints when called and calculates on a worker. Cancellation
	/// is checked during path search.
	/// </summary>
	public Task<NavMeshPath> CalculatePathAsync( CalculatePathRequest request, System.Threading.CancellationToken cancellationToken = default )
	{
		TraversalFilter filter;
		Vector3 extents;
		long generation;
		lock ( SyncRoot )
		{
			(filter, extents) = CapturePathSettings( request );
			generation = Generation;
		}
		return Task.Run( () =>
		{
			lock ( SyncRoot )
			{
				cancellationToken.ThrowIfCancellationRequested();
				if ( query is null || generation != Generation ) return new NavMeshPath { Status = NavMeshPathStatus.PathNotFound };
				return CalculatePathCore( request.Start, request.Target, extents, filter, cancellationToken );
			}
		}, cancellationToken );
	}

	internal TraversalFilter CreateFilter( NavMeshAgent agent = null )
	{
		var filter = agent is null ? defaultFilter : agent.QueryFilter;
		if ( FilterMatches( filter, agent ) ) return filter;

		uint allowed = 0;
		var costs = new float[32];
		for ( int area = 0; area < costs.Length; area++ )
		{
			costs[area] = AreaIdToDefinition( area )?.CostMultiplier ?? 1;
			if ( CanTraverseArea( agent, area ) ) allowed |= 1u << area;
		}
		filter = new TraversalFilter( allowed, costs );
		if ( agent is null ) defaultFilter = filter;
		else agent.QueryFilter = filter;
		return filter;
	}

	private bool CanTraverseArea( NavMeshAgent agent, int id )
	{
		if ( agent is null ) return true;
		var area = AreaIdToDefinition( id );
		if ( area is null ) return agent.AllowDefaultArea;
		return (agent.AllowedAreas.Count == 0 || agent.AllowedAreas.Contains( area )) && !agent.ForbiddenAreas.Contains( area );
	}

	internal bool FilterMatches( TraversalFilter filter, NavMeshAgent agent )
	{
		for ( int area = 0; area < 32; area++ )
			if ( filter.Allows( area ) != CanTraverseArea( agent, area ) || filter.CostMultiplier( area ) != (AreaIdToDefinition( area )?.CostMultiplier ?? 1) ) return false;
		return true;
	}

	private (TraversalFilter Filter, Vector3 Extents) CapturePathSettings( CalculatePathRequest request )
	{
		float radius = AgentRadius, height = AgentHeight;
		TraversalFilter filter;
		if ( request.Agent?.agentInternal is SimulationAgent agent && agent.Owner == Simulation )
		{
			// The mesh gate already protects this settings snapshot. Calls from
			// workers never enumerate mutable component area collections.
			radius = MathF.Max( radius, agent.Options.Radius );
			height = MathF.Max( height, agent.Options.Height );
			filter = ThreadSafe.IsMainThread
				? CreateFilter( request.Agent )
				: agent.Options.Filter;
		}
		else
		{
			if ( request.Agent is not null && !ThreadSafe.IsMainThread )
				throw new InvalidOperationException( "Worker queries require an active agent on this navmesh." );
			filter = CreateFilter( request.Agent );
			radius = MathF.Max( radius, request.Agent?.Radius ?? radius );
			height = MathF.Max( height, request.Agent?.Height ?? height );
		}
		return (filter, new Vector3( radius * 2.1f, height * 1.51f, radius * 2.1f ));
	}

	private NavMeshPath CalculatePathCore( Vector3 start, Vector3 target, Vector3 extents, TraversalFilter filter, System.Threading.CancellationToken cancellationToken )
	{
		// The mesh gate protects reusable search scratch for the entire operation.
		var pathQuery = query;
		var status = pathQuery.FindNearestPoly( ToNav( start ), extents, filter, out var startPoly, out var startPoint, out _ );
		if ( status.Failed() || startPoly == 0 ) return new() { Status = NavMeshPathStatus.StartNotFound };
		status = pathQuery.FindNearestPoly( ToNav( target ), extents, filter, out var targetPoly, out var targetPoint, out _ );
		if ( status.Failed() || targetPoly == 0 ) return new() { Status = NavMeshPathStatus.TargetNotFound };
		status = pathQuery.BeginPathSearch( startPoly, targetPoly, startPoint, targetPoint, filter );
		while ( status.InProgress() )
		{
			cancellationToken.ThrowIfCancellationRequested();
			status = pathQuery.AdvancePathSearch( 256 );
		}
		if ( status.Failed() ) return new() { Status = NavMeshPathStatus.PathNotFound };
		var polygons = pathPolygons;
		status = pathQuery.FinishPathSearch( polygons );
		if ( status.Failed() || polygons.Count == 0 ) return new() { Status = NavMeshPathStatus.PathNotFound };
		bool partial = polygons[^1] != targetPoly;
		if ( partial ) pathQuery.ClosestPointOnPoly( polygons[^1], targetPoint, out targetPoint, out _ );
		return CreatePathResult( pathQuery, startPoint, targetPoint, polygons, partial, target );
	}

	internal NavMeshPath CreatePathResult( MeshQuery pathQuery, Vector3 start, Vector3 target, List<long> polygons, bool partial, Vector3 requestedTarget )
	{
		var buffer = ArrayPool<StraightPath>.Shared.Rent( Math.Max( 16, polygons.Count * 2 + 2 ) );
		try
		{
			var status = pathQuery.FindStraightPath( start, target, polygons, polygons.Count, buffer, out int count, buffer.Length, 0 );
			if ( status.Failed() || count == 0 ) return new() { Status = NavMeshPathStatus.PathNotFound };
			var points = new NavMeshPathPoint[count];
			for ( int i = 0; i < count; i++ ) points[i] = new() { Position = FromNav( buffer[i].pos ) };
			return new() { Owner = this, Generation = Generation, RequestedTarget = requestedTarget, Polygons = polygons.ToArray(), Points = Array.AsReadOnly( points ), Status = partial ? NavMeshPathStatus.Partial : NavMeshPathStatus.Complete };
		}
		finally { ArrayPool<StraightPath>.Shared.Return( buffer ); }
	}

	public Vector3? GetRandomPoint()
	{
		lock ( SyncRoot )
		{
			// Can be null if called before Scene.NavMesh.Init has been called
			if ( query == null ) return null;

			var found = query.FindRandomPoint( TraversalFilter.Unrestricted, Random.Shared, out var poly, out var point );

			if ( found.Failed() ) return null;

			return FromNav( point );

		}
	}

	/// <summary>
	/// Get a random point on the navmesh, within the bounding box.
	/// This will return null if it can't find a point on the navmesh in a few tries. Returning false doesn't mean it's impossible, our algorithm here isn't the best.
	/// </summary>
	public Vector3? GetRandomPoint( BBox box )
	{
		lock ( SyncRoot )
		{
			if ( query == null ) return null;

			for ( int i = 0; i < 10; i++ )
			{
				var pos = box.RandomPointInside;
				var p = GetClosestPoint( pos );

				if ( p.HasValue && box.Contains( p.Value ) )
					return p.Value;
			}

			return null;

		}
	}

	/// <summary>
	/// Get a random point on the navmesh, within the sphere.
	/// This will return null if it can't find a point on the navmesh in a few tries. Returning false doesn't mean it's impossible, our algorithm here isn't the best.
	/// </summary>
	public Vector3? GetRandomPoint( Vector3 position, float radius )
	{
		lock ( SyncRoot )
		{
			if ( query == null ) return null;

			var sphere = new Sphere( position, radius );

			for ( int i = 0; i < 10; i++ )
			{
				var pos = position + Random.Shared.VectorInSphere( radius );
				var p = GetClosestPoint( pos );

				if ( p.HasValue && sphere.Contains( p.Value ) )
					return p.Value;
			}

			return default;

		}
	}

	public Vector3? GetClosestPoint( BBox box )
	{
		lock ( SyncRoot )
		{
			if ( query == null ) return null;

			var found = query.FindNearestPoly( ToNav( box.Center ), ToNav( box.Size / 2 ), TraversalFilter.Unrestricted, out var nearestRef, out var nearesPoint, out _ );

			if ( found.Failed() || nearestRef == 0 ) return null;

			return FromNav( nearesPoint );

		}
	}

	public Vector3? GetClosestPoint( Vector3 position, float radius = 1024.0f ) => GetClosestPoint( BBox.FromPositionAndSize( position, radius * 2.0f ) );


	public Vector3? GetClosestEdge( BBox box )
	{
		lock ( SyncRoot )
		{
			if ( query == null ) return null;

			var foundPoly = query.FindNearestPoly( ToNav( box.Center ), ToNav( box.Size / 2 ), TraversalFilter.Unrestricted, out var nearestPoly, out var nearesPoint, out _ );

			if ( foundPoly.Failed() || nearestPoly == 0 ) return null;

			var found = query.FindDistanceToWall( nearestPoly, ToNav( box.Center ), box.Size.Length, TraversalFilter.Unrestricted, out var _, out var hitPos, out var _ );

			if ( found.Failed() ) return null;

			return FromNav( hitPos );

		}
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
	internal long[] Polygons;
	internal NavMesh Owner;
	internal long Generation;
	internal Vector3 RequestedTarget;

	/// <summary>
	/// Points along the path.
	/// </summary>
	public IReadOnlyList<NavMeshPathPoint> Points { get; internal set; }
}

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
public struct NavMeshPathPoint
{
	public Vector3 Position;
}
