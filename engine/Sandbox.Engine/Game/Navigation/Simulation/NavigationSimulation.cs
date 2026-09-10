using Sandbox.Navigation.Pathfinding;

namespace Sandbox.Navigation;

/// <summary>
/// Scene-independent simulation. All coordinates are in navigation space.
/// Commands and published state share the navmesh gate; workers only read the
/// captured frame and write their own agent. No component callbacks run here.
/// </summary>
internal sealed class NavigationSimulation
{
	internal readonly object Gate;
	private readonly NavMeshGraph mesh;
	private readonly MeshQuery planningQuery;
	private readonly List<SimulationAgent> agents = new();
	private readonly Dictionary<(int, int), List<int>> grid = new();
	private readonly Stack<List<int>> buckets = new();
	private FrameAgent[] frame = [];
	private float cellSize;
	internal readonly Vector3 PlacementExtents;
	internal long Revision;
	private long update;
	private float stepDelta;
	private readonly Action<int> stepWorker;
	private readonly Action<int> separationWorker;
	private readonly Action<int> constrainWorker;
	internal int Count { get { lock ( Gate ) return agents.Count; } }

	internal NavigationSimulation( NavMeshGraph mesh, object gate, float radius, float height )
	{
		this.mesh = mesh;
		stepWorker = index => Step( index, stepDelta );
		separationWorker = index => agents[index].HadOverlap = ResolveOverlap( index );
		constrainWorker = index => ConstrainPosition( agents[index] );
		planningQuery = new MeshQuery( mesh );
		Gate = gate;
		PlacementExtents = new Vector3( radius * 2.1f, height * 1.51f, radius * 2.1f );
	}

	internal SimulationAgent Add( Vector3 position, SimulationSettings settings )
	{
		lock ( Gate )
		{
			var agent = new SimulationAgent( this, mesh, position, settings );
			agents.Add( agent );
			return agent;
		}
	}

	internal void Remove( SimulationAgent agent )
	{
		lock ( Gate ) { agents.Remove( agent ); agent.Removed = true; }
	}

	internal void Invalidate()
	{
		lock ( Gate )
		{
			foreach ( var agent in agents )
			{
				agent.Removed = true;
				agent.Velocity = agent.WishVelocity = default;
				agent.Link = null;
			}
			agents.Clear();
		}
	}

	internal void Update( float delta )
	{
		if ( !float.IsFinite( delta ) || delta <= 0 ) return;
		lock ( Gate )
		{
			update++;
			// Bound local movement so MoveAlongSurface cannot cross an arbitrary
			// number of polygons after a paused frame.
			int steps = Math.Clamp( (int)MathF.Ceiling( delta / 0.05f ), 1, 8 );
			float dt = MathF.Min( delta, 0.4f ) / steps;
			stepDelta = dt;
			for ( int step = 0; step < steps; step++ )
			{
				foreach ( var agent in agents ) Prepare( agent );
				CaptureFrame();
				if ( agents.Count < 64 )
				{
					for ( int i = 0; i < agents.Count; i++ ) Step( i, dt );
				}
				else
				{
					Parallel.For( 0, agents.Count, stepWorker );
				}
				if ( agents.Count > 1 ) ResolveOverlaps();
			}
		}
	}

	private void Prepare( SimulationAgent agent )
	{
		if ( agent.Link is not null ) return;
		var query = agent.Query;
		var settings = agent.Options;
		if ( agent.Path.Count == 0 || !query.IsValidPolyRef( agent.Path[0], settings.Filter ) )
		{
			agent.HasRoute = false;
			var extents = Vector3.Max( PlacementExtents, new Vector3( settings.Radius * 2.1f, settings.Height * 1.51f, settings.Radius * 2.1f ) );
			var status = query.FindNearestPoly( agent.Position, extents, settings.Filter, out var poly, out var point, out _ );
			if ( status.Failed() || poly == 0 )
			{
				agent.Path.Clear();
				agent.CornerCount = 0;
				agent.NeedsPath = (agent.Target ?? agent.RestingPosition).HasValue;
				agent.Velocity = agent.WishVelocity = default;
				return;
			}
			agent.Position = point;
			agent.Path.Clear();
			agent.Path.Add( poly );
			agent.NeedsPath = (agent.Target ?? agent.RestingPosition).HasValue;
		}
		if ( agent.PathRevision != Revision )
		{
			if ( (agent.Target ?? agent.RestingPosition).HasValue )
			{
				agent.NeedsPath |= agent.Partial;
				foreach ( var polygon in agent.Path )
					if ( !query.IsValidPolyRef( polygon, settings.Filter ) ) { agent.NeedsPath = true; agent.HasRoute = false; break; }
			}
			agent.PathRevision = Revision;
		}
		if ( agent.NeedsPath && (agent.Target ?? agent.RestingPosition) is Vector3 target )
		{
			var status = query.FindNearestPoly( target, new Vector3( 256 ), settings.Filter, out var poly, out var point, out _ );
			if ( status.Failed() || poly == 0 ) { agent.HasRoute = false; return; }
			long start = agent.Path[0];
			status = planningQuery.FindPath( start, poly, agent.Position, point, settings.Filter, agent.Path );
			if ( status.Failed() || agent.Path.Count == 0 ) { agent.Path.Clear(); agent.Path.Add( start ); agent.HasRoute = false; return; }
			agent.Partial = agent.Path[^1] != poly;
			if ( agent.Partial ) query.ClosestPointOnPoly( agent.Path[^1], point, out point, out _ );
			agent.PathTarget = point;
			agent.PathRequestedTarget = target;
			agent.NeedsPath = false;
			agent.HasRoute = true;
		}
	}

	private void CaptureFrame( bool collisions = false )
	{
		if ( frame.Length < agents.Count ) Array.Resize( ref frame, Math.Max( agents.Count, frame.Length * 2 ) );
		foreach ( var bucket in grid.Values ) { bucket.Clear(); buckets.Push( bucket ); }
		grid.Clear();
		cellSize = 1;
		for ( int i = 0; i < agents.Count; i++ )
		{
			var agent = agents[i];
			frame[i] = new FrameAgent( agent.Position, agent.Velocity, agent.Options.Radius, agent.Options.Height, agent.Link is null && agent.Path.Count > 0, agent.Options.MaxSpeed > 0 );
			cellSize = MathF.Max( cellSize, collisions ? agent.Options.Radius * 2 : agent.Options.Radius * 4 + agent.Options.MaxSpeed );
		}
		for ( int i = 0; i < agents.Count; i++ )
		{
			if ( !frame[i].Walking ) continue;
			var key = Cell( frame[i].Position );
			if ( !grid.TryGetValue( key, out var bucket ) )
			{
				bucket = buckets.Count > 0 ? buckets.Pop() : new List<int>();
				grid.Add( key, bucket );
			}
			bucket.Add( i );
		}
	}

	private (int x, int z) Cell( Vector3 position ) => ((int)MathF.Floor( position.x / cellSize ), (int)MathF.Floor( position.z / cellSize ));

	private void Step( int index, float dt )
	{
		var agent = agents[index];
		var settings = agent.Options;
		if ( agent.Link is SimulationLink link )
		{
			// Publish every link entry for at least one complete update, including
			// links shorter than a movement substep, so component events see it.
			if ( agent.LinkStartedUpdate == update ) return;
			if ( settings.AutoTraverseLinks && settings.MaxSpeed > 0 )
			{
				var offset = link.End - agent.Position;
				float distance = offset.Length;
				if ( distance <= settings.MaxSpeed * dt )
				{
					agent.Position = link.End;
					agent.CompleteLinkCore();
				}
				else agent.Position += offset / distance * settings.MaxSpeed * dt;
			}
			return;
		}
		if ( agent.Path.Count == 0 || !(agent.Target ?? agent.RestingPosition).HasValue ) return;
		if ( agent.NeedsPath )
		{
			agent.Velocity = agent.WishVelocity = default;
			return;
		}

		if ( agent.RestingPosition is Vector3 resting && Geometry.DistanceBetween2DSqr( agent.Position, resting ) < settings.Radius * settings.Radius * 0.0625f )
		{
			agent.Velocity = agent.WishVelocity = default;
			return;
		}

		// End the funnel at the first link entrance. A vertical link has zero
		// horizontal width and can otherwise disappear from a 2D funnel.
		Vector3 end = agent.PathTarget;
		int count = agent.Path.Count;
		long linkRef = 0;
		Vector3 linkEnd = default;
		for ( int i = 1; i < count; i++ )
		{
			if ( mesh.GetTileAndPolyByRef( agent.Path[i], out _, out var poly ).Failed() ) { agent.NeedsPath = true; return; }
			if ( poly.type != PolyTypes.POLYTYPE_OFFMESH_CONNECTION ) continue;
			if ( mesh.GetOffMeshConnectionPolyEndPoints( agent.Path[i - 1], agent.Path[i], ref end, ref linkEnd ).Failed() ) { agent.NeedsPath = true; return; }
			linkRef = agent.Path[i];
			count = i;
			break;
		}
		if ( linkRef != 0 && Geometry.DistanceBetween2DSqr( agent.Position, end ) <= MathF.Max( 0.1f, settings.Radius ) * MathF.Max( 0.1f, settings.Radius ) )
		{
			agent.Link = new SimulationLink( linkRef, end, linkEnd, agent.Position );
			agent.LinkStartedUpdate = update;
			agent.Path.RemoveRange( 0, count + 1 );
			agent.Velocity = agent.WishVelocity = default;
			return;
		}
		var status = agent.Query.FindStraightPath( agent.Position, end, agent.Path, count, agent.Corners, out agent.CornerCount, agent.Corners.Length, 0 );
		if ( status.Failed() ) { agent.NeedsPath = true; return; }
		Vector3 direction = default;
		for ( int i = 0; i < agent.CornerCount; i++ )
		{
			direction = agent.Corners[i].pos - agent.Position;
			direction.y = 0;
			if ( direction.LengthSquared > 0.01f ) break;
		}
		float distanceToEnd = Geometry.DistanceBetween2D( agent.Position, end );
		if ( linkRef == 0 && distanceToEnd < (agent.RestingPosition.HasValue ? settings.Radius * 0.25f : 0.1f) )
		{
			// A partial route must retain its destination so tile changes can resume it.
			if ( !agent.Partial ) { agent.RestingPosition = agent.PathTarget; agent.Target = null; }
			agent.Velocity = agent.WishVelocity = default;
			return;
		}
		float speed = MathF.Min( settings.MaxSpeed, MathF.Sqrt( 2 * settings.Acceleration * distanceToEnd ) );
		var wish = direction.Normal * speed;
		agent.WishVelocity = wish;
		var avoidance = WallSteering.Steer( agent, Avoid( index, wish, dt ), end );
		var change = avoidance - agent.Velocity;
		float maxChange = settings.Acceleration * dt;
		if ( change.Length > maxChange ) change = change.Normal * maxChange;
		var velocity = agent.Velocity + change;
		var displacement = velocity * dt;
		if ( displacement.Length > distanceToEnd && linkRef == 0 ) displacement = displacement.Normal * distanceToEnd;
		var old = agent.Position;
		MoveAlongSurface( agent, displacement );
		agent.Velocity = (agent.Position - old) / dt;
	}

	private void MoveAlongSurface( SimulationAgent agent, Vector3 displacement )
	{
		var old = agent.Position;
		Span<long> visited = stackalloc long[32];
		var status = agent.Query.MoveAlongSurface( agent.Path[0], old, old + displacement, agent.Options.Filter, out var position, visited, out int visitedCount, visited.Length );
		if ( status.Failed() ) { agent.NeedsPath = true; return; }
		// Keep the corridor suffix after the furthest common polygon.
		int commonPath = -1, commonVisited = -1;
		for ( int p = agent.Path.Count - 1; p >= 0 && commonPath < 0; p-- )
			for ( int v = visitedCount - 1; v >= 0; v-- )
				if ( agent.Path[p] == visited[v] ) { commonPath = p; commonVisited = v; break; }
		if ( commonPath >= 0 )
		{
			agent.Path.RemoveRange( 0, commonPath );
			for ( int v = commonVisited + 1; v < visitedCount; v++ ) agent.Path.Insert( 0, visited[v] );
			// Sliding across a seam or avoiding an agent can leave the planned
			// corridor. Replan from that polygon instead of steering backwards
			// through the old entrance, which can create a loop at tile corners.
			agent.NeedsPath |= commonVisited < visitedCount - 1;
		}
		if ( agent.Query.GetPolyHeight( agent.Path[0], position, out float height ).Succeeded() ) position.y = height;
		agent.Position = position;
	}

	private void ResolveOverlaps()
	{
		foreach ( var agent in agents ) agent.CollisionOrigin = agent.Position;
		for ( int iteration = 0; iteration < 4; iteration++ )
		{
			// Penetration only needs touching neighbours, not the velocity look-ahead range.
			CaptureFrame( collisions: true );
			bool overlap = false;
			if ( agents.Count < 64 )
			{
				for ( int i = 0; i < agents.Count; i++ ) overlap |= ResolveOverlap( i );
			}
			else
			{
				Parallel.For( 0, agents.Count, separationWorker );
				foreach ( var agent in agents ) overlap |= agent.HadOverlap;
			}
			if ( !overlap ) break;
		}
		// Keep intermediate corrections private, then constrain the combined displacement once.
		if ( agents.Count < 64 )
		{
			foreach ( var agent in agents ) ConstrainPosition( agent );
		}
		else Parallel.For( 0, agents.Count, constrainWorker );
	}

	private void ConstrainPosition( SimulationAgent agent )
	{
		var displacement = agent.Position - agent.CollisionOrigin;
		if ( displacement.LengthSquared == 0 ) return;
		agent.Position = agent.CollisionOrigin;
		MoveAlongSurface( agent, displacement );
	}

	private bool ResolveOverlap( int index )
	{
		var self = frame[index];
		if ( !self.Walking || !self.CanYield ) return false;
		var cell = Cell( self.Position );
		Vector3 displacement = default;
		int count = 0;
		for ( int x = cell.x - 1; x <= cell.x + 1; x++ )
			for ( int z = cell.z - 1; z <= cell.z + 1; z++ )
			{
				if ( !grid.TryGetValue( (x, z), out var bucket ) ) continue;
				foreach ( int otherIndex in bucket )
				{
					if ( otherIndex == index ) continue;
					var other = frame[otherIndex];
					if ( self.Position.y + self.Height <= other.Position.y || other.Position.y + other.Height <= self.Position.y ) continue;
					var offset = (self.Position - other.Position).WithY( 0 );
					float distance = offset.Length, radius = self.Radius + other.Radius;
					if ( distance >= radius ) continue;
					var normal = distance > 0.001f ? offset / distance : new Vector3( index < otherIndex ? -1 : 1, 0, 0 );
					displacement += normal * ((radius - distance) * (other.CanYield ? 0.5f : 1));
					count++;
				}
			}
		if ( count == 0 ) return false;
		agents[index].Position += displacement * (0.7f / count);
		return true;
	}

	private Vector3 Avoid( int index, Vector3 wish, float dt )
	{
		var self = frame[index];
		var settings = agents[index].Options;
		var cell = Cell( self.Position );
		var correction = Vector3.Zero;
		int neighbourCount = 0;
		for ( int x = cell.x - 1; x <= cell.x + 1; x++ )
			for ( int z = cell.z - 1; z <= cell.z + 1; z++ )
			{
				if ( !grid.TryGetValue( (x, z), out var bucket ) ) continue;
				foreach ( int otherIndex in bucket )
				{
					if ( index == otherIndex ) continue;
					var other = frame[otherIndex];
					if ( self.Position.y + self.Height <= other.Position.y || other.Position.y + other.Height <= self.Position.y ) continue;
					var offset = self.Position - other.Position;
					offset.y = 0;
					float radius = self.Radius + other.Radius;
					float distance = offset.Length;
					if ( distance > radius * 2 + settings.MaxSpeed ) continue;
					neighbourCount++;
					var normal = distance > 0.001f ? offset / distance : new Vector3( index < otherIndex ? -1 : 1, 0, 0 );
					if ( distance < radius ) correction += normal * ((radius - distance) / MathF.Max( dt, 0.01f )) * 0.5f;
					else
					{
						var relative = wish - other.Velocity;
						float closing = -Vector3.Dot( relative, normal );
						if ( closing > 0 && distance - radius < closing * 0.75f )
						{
							// Consistent passing side breaks head-on symmetry.
							var side = new Vector3( -normal.z, 0, normal.x );
							correction += (normal + side) * (closing - (distance - radius) / 0.75f) * 0.5f;
						}
						if ( distance < radius * 2 ) correction += normal * (1 - (distance - radius) / MathF.Max( radius, 0.01f )) * settings.Separation * wish.Length;
					}
				}
			}
		// Keep dense groups from multiplying steering forces or overriding arrival braking.
		var result = wish + correction / Math.Max( 1, neighbourCount );
		return result.Length > wish.Length ? result.Normal * wish.Length : result;
	}

	private readonly record struct FrameAgent( Vector3 Position, Vector3 Velocity, float Radius, float Height, bool Walking, bool CanYield );
}

internal readonly record struct SimulationSettings( float Radius, float Height, float MaxSpeed, float Acceleration, float Separation, bool AutoTraverseLinks, TraversalFilter Filter );
internal readonly record struct SimulationLink( long Polygon, Vector3 Start, Vector3 End, Vector3 Initial );
internal readonly record struct SimulationState( Vector3 Position, Vector3 Velocity, Vector3 WishVelocity, Vector3? Target, bool Navigating, SimulationLink? Link );

internal sealed class SimulationAgent
{
	internal readonly NavigationSimulation Owner;
	internal readonly MeshQuery Query;
	internal SimulationSettings Options;
	internal Vector3 Position, Velocity, WishVelocity, PathTarget, PathRequestedTarget, CollisionOrigin;
	internal Vector3? Target;
	// Retain the resting point after publishing arrival, so neighbours can push past.
	internal Vector3? RestingPosition;
	internal SimulationLink? Link;
	internal readonly List<long> Path = new();
	internal readonly StraightPath[] Corners = new StraightPath[16];
	internal int CornerCount;
	internal readonly MeshQuery.BoundarySegment[] Walls = new MeshQuery.BoundarySegment[16];
	internal Vector3 WallCenter;
	internal long WallRevision = -1;
	internal int WallCount;
	// A requested repair does not invalidate the corridor already being followed.
	internal bool NeedsPath, HasRoute, Partial, Removed, HadOverlap;
	internal long PathRevision = -1;
	internal long LinkStartedUpdate;

	internal SimulationAgent( NavigationSimulation owner, NavMeshGraph mesh, Vector3 position, SimulationSettings settings )
	{
		Owner = owner;
		Query = new MeshQuery( mesh );
		Position = position;
		Options = settings;
	}

	internal SimulationState State
	{
		get { lock ( Owner.Gate ) return new( Position, Velocity, WishVelocity, Target, !Removed && Target.HasValue && HasRoute && (Path.Count > 0 || Link.HasValue), Link ); }
	}

	internal void MoveTo( Vector3 target )
	{
		lock ( Owner.Gate )
		{
			if ( Removed || Target is Vector3 previous && previous.AlmostEqual( target, 1 ) ) return;
			Target = target;
			RestingPosition = null;
			NeedsPath = true;
		}
	}

	internal void SetPosition( Vector3 position )
	{
		lock ( Owner.Gate )
		{
			Position = position;
			WallRevision = -1;
			RestingPosition = null;
			if ( Link is null ) { HasRoute = false; Path.Clear(); NeedsPath = Target.HasValue; }
		}
	}

	internal void Stop()
	{
		lock ( Owner.Gate )
		{
			if ( Link is not null ) Path.Clear();
			Target = RestingPosition = null;
			Link = null;
			NeedsPath = HasRoute = false;
			Velocity = WishVelocity = default;
			CornerCount = 0;
		}
	}

	internal void CompleteLink()
	{
		lock ( Owner.Gate )
		{
			if ( Link is null ) return;
			CompleteLinkCore();
			// Custom traversal can land outside the exit polygon. Place and replan
			// from the supplied position on the next update.
			Path.Clear();
			CornerCount = 0;
			HasRoute = false;
			NeedsPath = Target.HasValue;
		}
	}

	internal void CompleteLinkCore()
	{
		if ( Link is null ) return;
		WallRevision = -1;
		Link = null;
		Velocity = WishVelocity = default;
	}
}
