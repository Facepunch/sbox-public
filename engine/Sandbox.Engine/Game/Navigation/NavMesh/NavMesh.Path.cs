using DotRecast.Detour;
using System.Buffers;

namespace Sandbox.Navigation;

/// <summary>
/// Navigation Mesh - allowing AI to navigate a world
/// </summary>
public sealed partial class NavMesh
{
	[Obsolete( "Use CalculatePath instead" )]
	public unsafe List<Vector3> GetSimplePath( Vector3 from, Vector3 to )
	{
		var list = new List<Vector3>();
		int nodeCountMax = 128;

		// find polys
		var fromFound = query.FindNearestPoly( ToNav( from ), ToNav( TileSizeWorldSpace * 3 ), DtQueryNoOpFilter.Shared, out var fromPoly, out var fromPoint, out _ );
		if ( !fromFound.Succeeded() ) return list;

		var toFound = query.FindNearestPoly( ToNav( to ), ToNav( TileSizeWorldSpace * 3 ), DtQueryNoOpFilter.Shared, out var toPoly, out var toPoint, out _ );

		if ( toFound.Failed() ) return list;

		// find path
		List<long> polyPath = new List<long>( 128 );
		var polyPathFound = query.FindPath( fromPoly, toPoly, fromPoint, toPoint, DtQueryNoOpFilter.Shared, ref polyPath, DtFindPathOption.NoOption );

		if ( polyPathFound.Failed() ) return list;

		Span<DtStraightPath> outNodes = stackalloc DtStraightPath[nodeCountMax];
		var straightPathFound = query.FindStraightPath( fromPoint, toPoint, polyPath, polyPath.Count, outNodes, out var straightPathCount, 128, 0 );

		if ( straightPathFound.Failed() ) return list;

		for ( int i = 0; i < straightPathCount; i++ )
		{
			list.Add( FromNav( outNodes[i].pos ) );
		}

		return list;
	}

	/// <summary>
	/// Computes a navigation path between the specified start and target positions on the navmesh.
	/// Uses the same pathfinding algorithm as <see cref="NavMeshAgent"/>, taking agent configuration into account if provided.
	/// The result is suitable for direct use with <see cref="NavMeshAgent.SetPath"/>.
	/// If a complete path cannot be found, the result may indicate an incomplete or failed path.
	/// </summary>
	public NavMeshPath CalculatePath( CalculatePathRequest request )
	{
		NavMeshPath result = new();

		// In navspace
		var searchRadius = request.Agent != null ? new Vector3( request.Agent.Radius * 2.01f, request.Agent.Height * 1.51f, request.Agent.Radius * 2.01f ) : crowd._agentPlacementHalfExtents;
		var filter = request.Agent != null ? request.Agent.agentInternal.option.filter : crowd.GetDefaultFilter();

		var startFound = query.FindNearestPoly( ToNav( request.Start ), searchRadius, filter, out var startPoly, out var startLocation, out _ );
		if ( !startFound.Succeeded() )
		{
			result.Status = NavMeshPathStatus.StartNotFound;
			return result;
		}

		var targetFound = query.FindNearestPoly( ToNav( request.Target ), searchRadius, filter, out var targetPoly, out var targetLocation, out _ );
		if ( !targetFound.Succeeded() )
		{
			result.Status = NavMeshPathStatus.TargetNotFound;
			return result;
		}

		// Quick search towards the goal.
		var dtStatus = query.InitSlicedFindPath( startPoly, targetPoly, startLocation, targetLocation, filter, 0 );
		if ( dtStatus.Failed() )
		{
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}
		do
		{
			dtStatus = query.UpdateSlicedFindPath( crowd.Config().maxFindPathIterations, out var _ );
			if ( dtStatus.Failed() )
			{
				result.Status = NavMeshPathStatus.PathNotFound;
				return result;
			}
		} while ( dtStatus.InProgress() );

		result.Polygons = new( 128 );
		dtStatus = query.FinalizeSlicedFindPath( ref result.Polygons );
		if ( dtStatus.Failed() || result.Polygons.Count == 0 )
		{
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}

		var straightPathCache = ArrayPool<DtStraightPath>.Shared.Rent( 4096 );
		dtStatus = query.FindStraightPath( startLocation, targetLocation, result.Polygons, result.Polygons.Count, straightPathCache, out var filledPointCount, straightPathCache.Length, 0 );
		if ( dtStatus.Failed() )
		{
			ArrayPool<DtStraightPath>.Shared.Return( straightPathCache );
			result.Status = NavMeshPathStatus.PathNotFound;
			return result;
		}

		var points = new List<NavMeshPathPoint>( filledPointCount );
		for ( int i = 0; i < filledPointCount; i++ )
		{
			points.Add( new NavMeshPathPoint { Position = FromNav( straightPathCache[i].pos ) } );
		}
		ArrayPool<DtStraightPath>.Shared.Return( straightPathCache );
		result.Points = points;

		if ( result.Polygons[^1] != targetPoly )
		{
			result.Status = NavMeshPathStatus.Partial;
		}
		else
		{
			result.Status = NavMeshPathStatus.Complete;
		}


		return result;
	}
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
	internal List<long> Polygons;

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
