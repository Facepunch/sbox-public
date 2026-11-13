using System.Numerics;

namespace Nav;

/// <summary>
/// A set of agents on one mesh and the function that steps them: agents in, velocities out.
/// One owner thread makes every call and no call may overlap a Step. Agent ids are plain indices,
/// reused lowest first, and AgentState.Generation tells one owner of an id from the next; a call with
/// an id that is not in use does nothing. Lengths are in the mesh's units, times in the unit of
/// Step's delta.
/// </summary>
public sealed partial class NavCrowd
{
	const int MaxCorners = 16, MaxWalls = 16, VisitedCapacity = 32, RepairExpansions = 64, Batch = 32;

	NavMesh mesh;
	readonly int workers;
	readonly NavQuery[] queries;

	// Indexed by agent id. corners holds MaxCorners per agent, walls MaxWalls segments of two points.
	AgentParams[] parms = [];
	AgentState[] states = [];
	Agent[] agents = [];
	PolyRef[][] corridors = [];
	Vector3[] corners = [];
	Vector3[] walls = [];

	// One past the highest id in use, and the lowest id not in use.
	int high, lowestFree;

	// SetPath joins a corridor to the agent here before it replaces the agent's own.
	PolyRef[] joined = [];

	// Tolerances, all from the mesh settings: eps is a tenth of a unit at a cell of 4.
	float eps, areaScale, floorGap, deadBand;

	/// <summary>
	/// A Step is cut into substeps of at most SubstepTime, and simulates at most MaxSubsteps of them:
	/// what a longer delta has beyond that is not simulated. AvoidHorizon is how far ahead agents
	/// look for each other and for walls. Both are times in the unit of Step's delta, and the
	/// defaults are meant as seconds: a caller that steps in milliseconds sets 50 and 750.
	/// </summary>
	public float SubstepTime = 0.05f, AvoidHorizon = 0.75f;

	/// <summary>
	/// The most substeps one Step simulates, see SubstepTime. Less than 1 counts as 1.
	/// </summary>
	public int MaxSubsteps = 8;

	/// <summary>
	/// Half extents of the boxes the crowd looks for the mesh in. PlacementExtents is used around an
	/// agent whenever its polygon has to be found: after Add, SetPosition, SetPath and CompleteLink,
	/// and when the mesh under it changed. TargetExtents is used around a target. Neither is ever
	/// taken smaller than 2.1 radii and 1.51 heights of the agent itself. The defaults are 2.1 radii
	/// and 1.51 heights of the mesh's agent, and 16 of its radii each way. SetMesh moves a value to
	/// the new mesh's default only if it still is the old one. A new value is used from the next
	/// lookup on; nothing is looked up or planned again because of it.
	/// </summary>
	public Vector3 PlacementExtents, TargetExtents;

	/// <summary>
	/// workers is the most bodies a Step hands to its run hook at a time. A small crowd gets fewer.
	/// </summary>
	public NavCrowd( NavMesh mesh, int workers = 1 )
	{
		this.workers = Math.Max( 1, workers );
		queries = new NavQuery[this.workers];
		tallies = new Tally[this.workers];
		work = Work;
		this.mesh = null!;
		SetMesh( mesh );
	}

	/// <summary>
	/// Moves the crowd to another mesh. Agents keep their targets and rest points, find their place on
	/// the new mesh in the next Step and plan again. An agent on a link is taken off it.
	/// </summary>
	public void SetMesh( NavMesh mesh )
	{
		var (oldPlacement, oldTarget) = this.mesh == null ? default : DefaultExtents( this.mesh.Settings );
		this.mesh = mesh;
		pinned = null;
		for ( int i = 0; i < workers; i++ ) queries[i] = new NavQuery( mesh );

		var settings = mesh.Settings;
		eps = mesh.Epsilon;
		areaScale = mesh.AreaScale;
		deadBand = settings.CellSize / 4f;
		// Two floors are never closer than this.
		floorGap = settings.AgentHeight;
		var (placement, target) = DefaultExtents( settings );
		if ( PlacementExtents == oldPlacement ) PlacementExtents = placement;
		if ( TargetExtents == oldTarget ) TargetExtents = target;

		for ( int i = 0; i < high; i++ )
		{
			ref var a = ref agents[i];
			if ( !a.Has( AgentFlags.Active ) ) continue;
			parms[i] = Sanitize( parms[i] );
			LeaveLink( i );
			a.Unseat();
			Publish( i );
		}
	}

	static (Vector3 Placement, Vector3 Target) DefaultExtents( in NavSettings settings )
	{
		float r = settings.AgentRadius;
		return (new Vector3( r * 2.1f, r * 2.1f, settings.AgentHeight * 1.51f ), new Vector3( r * 16f ));
	}

	/// <summary>
	/// Returns the lowest free id. The agent finds its polygon in the next Step.
	/// </summary>
	public int Add( Vector3 position, in AgentParams value )
	{
		int id = lowestFree;
		if ( id == agents.Length ) Grow( Math.Max( 16, id * 2 ) );
		parms[id] = Sanitize( value );
		states[id] = new AgentState { Position = position, Link = -1, Status = AgentStatus.Idle, Generation = states[id].Generation + 1 };
		agents[id] = new Agent { Flags = AgentFlags.Active | AgentFlags.NeedsPlan, WallCount = -1, Seen = position, CostKey = CostKey( value.Filter ) };
		corridors[id] ??= new PolyRef[32];
		high = Math.Max( high, id + 1 );
		do lowestFree++; while ( lowestFree < high && agents[lowestFree].Has( AgentFlags.Active ) );
		return id;
	}

	public void Remove( int agent )
	{
		if ( !Live( agent ) ) return;
		agents[agent] = default;
		parms[agent] = default;
		states[agent] = new AgentState { Link = -1, Generation = states[agent].Generation };
		lowestFree = Math.Min( lowestFree, agent );
		while ( high > 0 && !agents[high - 1].Has( AgentFlags.Active ) ) high--;
	}

	/// <summary>
	/// A changed filter plans the route again; a changed size or filter collects the walls again; an
	/// agent that is off the mesh is looked for again whatever changed. Nothing else is planned
	/// again: a new size or speed is simply used from the next Step on. The filter's costs are
	/// compared by content, so a cost array edited in place counts as changed once it is handed in
	/// again. Until then the crowd does not know. Radius and Height are kept above zero, speeds at
	/// zero or more and Separation between 0 and 1.
	/// </summary>
	public void SetParams( int agent, in AgentParams value )
	{
		if ( !Live( agent ) ) return;
		ref var a = ref agents[agent];
		var old = parms[agent];
		var now = parms[agent] = Sanitize( value );
		uint costs = CostKey( now.Filter );
		bool filter = old.Filter.Forbidden != now.Filter.Forbidden || costs != a.CostKey;
		a.CostKey = costs;
		if ( filter ) a.Flags |= AgentFlags.Validate | AgentFlags.NeedsPlan;
		if ( filter || old.Radius != now.Radius || old.Height != now.Height ) a.WallCount = -1;
		// Off the mesh: the new size may find it.
		if ( a.Count == 0 ) a.Flags |= AgentFlags.NeedsPlan;
		// Its velocity is told by its positions from here on.
		if ( now.External && !old.External ) a.Seen = states[agent].Position;
		Publish( agent );
	}

	// The contents of a filter's cost array, to notice an array that was edited in place.
	static uint CostKey( in NavFilter filter )
	{
		uint key = 2166136261;
		foreach ( float cost in filter.Costs ?? [] ) key = (key ^ BitConverter.SingleToUInt32Bits( cost )) * 16777619;
		return key;
	}

	/// <summary>
	/// The params of every agent, indexed by id up to the highest id in use. Ids not in use hold
	/// default values; States tells which they are. The span is over the crowd's own storage: take it
	/// again after Add or Remove.
	/// </summary>
	public ReadOnlySpan<AgentParams> Params => parms.AsSpan( 0, high );

	/// <summary>
	/// The state of every agent, indexed by id up to the highest id in use. An id not in use has
	/// Status Free. The span is over the crowd's own storage: it shows what later calls and Steps
	/// change, and is to be taken again after Add or Remove.
	/// </summary>
	public ReadOnlySpan<AgentState> States => states.AsSpan( 0, high );

	/// <summary>
	/// The route is planned in the next Step. An agent that is moving or on a link keeps following its
	/// old route until then, and a moving one ignores a target within a quarter cell of the one it has.
	/// </summary>
	public void MoveTo( int agent, Vector3 target )
	{
		if ( !Live( agent ) ) return;
		ref var a = ref agents[agent];
		ref var state = ref states[agent];
		if ( state.Status == AgentStatus.Moving && Geo.AlmostEqual( state.Target, target, deadBand ) ) return;
		if ( state.Status is not (AgentStatus.Moving or AgentStatus.OnLink) ) a.Flags &= ~AgentFlags.HasRoute;
		state.Target = target;
		a.Flags = a.Flags & ~AgentFlags.AtEnd | AgentFlags.HasTarget | AgentFlags.NeedsPlan;
		Publish( agent );
	}

	/// <summary>
	/// Forgets the target, the rest point and the route, and leaves a link where the agent stands.
	/// </summary>
	public void Stop( int agent )
	{
		if ( !Live( agent ) ) return;
		ref var a = ref agents[agent];
		ref var state = ref states[agent];
		// On a link, or moved by SetPosition, the agent is not where its corridor starts.
		bool seated = !state.Poly.IsNone;
		LeaveLink( agent );
		Halt( ref state, parms[agent] );
		a.Flags &= ~(AgentFlags.HasTarget | AgentFlags.HasRoute | AgentFlags.Partial | AgentFlags.AtEnd | AgentFlags.NeedsPlan | AgentFlags.NeedsRepair);
		if ( seated ) a.Count = 1;
		else a.Unseat();
		a.CornerCount = 0;
		Publish( agent );
	}

	/// <summary>
	/// Says where the agent is. What follows depends on who moves it.
	/// An agent the crowd moves is teleported: the next Step puts it on the mesh within
	/// PlacementExtents of the position, or leaves it there, off the mesh, until something changes.
	/// It forgets its rest point, keeps its target and joins its route from the new place.
	/// An External agent is where the caller says, to be said every tick: the position is kept
	/// exactly as given, on the mesh or not, and the crowd only looks up the polygon under it. Its
	/// rest point stays, and its Velocity becomes how far it was moved between two Steps over the
	/// Step's delta, unless SetVelocity was ever called for it.
	/// On a link either kind is just moved; the link goes on.
	/// </summary>
	public void SetPosition( int agent, Vector3 position )
	{
		if ( !Live( agent ) ) return;
		ref var a = ref agents[agent];
		ref var state = ref states[agent];
		state.Position = position;
		a.WallCount = -1;
		if ( state.Link >= 0 ) return;

		if ( !parms[agent].External ) a.Flags &= ~AgentFlags.AtEnd;
		if ( a.Has( AgentFlags.HasTarget | AgentFlags.AtEnd ) && a.Has( AgentFlags.HasRoute ) && a.Count > 0 && !a.Has( AgentFlags.NeedsPlan ) ) a.Flags |= AgentFlags.NeedsRepair;
		else a.Unseat();
		Publish( agent );
		// Which polygon the agent is on now is for the next Step to find out.
		state.Poly = default;
	}

	/// <summary>
	/// The velocity an agent the crowd moves accelerates from. For an External agent, what neighbours
	/// see: once this has been called for it the crowd no longer derives its velocity from its positions.
	/// </summary>
	public void SetVelocity( int agent, Vector3 velocity )
	{
		if ( !Live( agent ) ) return;
		states[agent].Velocity = velocity;
		if ( parms[agent].External ) agents[agent].Flags |= AgentFlags.OwnVelocity;
	}

	/// <summary>
	/// Ends the link the agent is on, wherever the agent is. The next Step finds the mesh there and
	/// plans from there.
	/// </summary>
	public void CompleteLink( int agent )
	{
		if ( !Live( agent ) || states[agent].Link < 0 ) return;
		ref var state = ref states[agent];
		LeaveLink( agent );
		Halt( ref state, parms[agent] );
		agents[agent].Unseat();
		Publish( agent );
	}

	/// <summary>
	/// Makes the agent follow a corridor, for example the Polys of a NavPath. end is where it leads on
	/// the mesh, target what was asked for, partial whether it stops short of that. The corridor is cut
	/// to start at the agent's polygon or joined to it by a short search; an agent the crowd moves is
	/// put on the mesh, and one on a link leaves it. False, with nothing changed, when an entry is
	/// stale or not allowed by the agent's filter, a link is first, last or next to another link, the
	/// agent is not within PlacementExtents of the mesh, or it cannot be joined to the corridor.
	/// The corridor has to come from this crowd's mesh. A PolyRef means something only to the mesh
	/// that made it, and one from another mesh is not told apart: it may pass as valid and name
	/// some other polygon.
	/// </summary>
	public bool SetPath( int agent, ReadOnlySpan<PolyRef> corridor, Vector3 end, Vector3 target, bool partial )
	{
		if ( !Live( agent ) || corridor.IsEmpty || !Geo.IsFinite( end ) ) return false;
		ref var a = ref agents[agent];
		ref var state = ref states[agent];
		ref readonly var p = ref parms[agent];
		var s = mesh.State;
		for ( int k = 0; k < corridor.Length; k++ )
		{
			if ( !s.IsValid( corridor[k], p.Filter ) ) return false;
			if ( corridor[k].IsLink && (k == 0 || k == corridor.Length - 1 || corridor[k - 1].IsLink) ) return false;
		}

		var query = queries[0];
		if ( !query.Nearest( s, state.Position, PlacementBox( p ), p.Filter, null, 0, out uint id, out var point ) ) return false;
		var poly = s.RefOf( id );

		int at = corridor.IndexOf( poly );
		var route = at >= 0 ? corridor[at..] : default;
		if ( at < 0 )
		{
			if ( joined.Length < corridor.Length ) joined = new PolyRef[corridor.Length];
			corridor.CopyTo( joined );
			int length = corridor.Length;
			if ( !query.Repair( s, poly, point, end, p.Filter, ref joined, ref length, RepairExpansions ) ) return false;
			route = joined.AsSpan( 0, length );
		}

		ref var own = ref corridors[agent];
		int count = route.Length;
		if ( own.Length < count ) own = new PolyRef[Math.Max( count, own.Length * 2 )];
		route.CopyTo( own );

		if ( !p.External ) state.Position = point;
		LeaveLink( agent );
		state.Target = state.RouteTarget = target;
		a.RouteEnd = end;
		a.Count = count;
		a.CornerCount = 0;
		a.WallCount = -1;
		a.Flags = a.Flags & (AgentFlags.Active | AgentFlags.OwnVelocity) | AgentFlags.HasTarget | AgentFlags.HasRoute | (partial ? AgentFlags.Partial : 0);
		Publish( agent );
		return true;
	}

	/// <summary>
	/// The route being followed, from where the agent is now. While a new target waits to be planned
	/// this is still the old route. NotFound when the agent has no target or no route.
	/// </summary>
	public PathStatus GetPath( int agent, NavPath path )
	{
		path.Clear( PathStatus.NotFound );
		if ( !Live( agent ) ) return PathStatus.NotFound;
		ref readonly var a = ref agents[agent];
		if ( !a.Has( AgentFlags.HasTarget ) || !a.Has( AgentFlags.HasRoute ) || a.Count == 0 ) return PathStatus.NotFound;

		var corridor = corridors[agent].AsSpan( 0, a.Count );
		if ( path.polys.Length < corridor.Length ) path.polys = new PolyRef[Math.Max( corridor.Length, path.polys.Length * 2 )];
		if ( path.corners.Length < corridor.Length * 2 + 2 ) path.corners = new Vector3[Math.Max( corridor.Length * 2 + 2, path.corners.Length * 2 )];
		corridor.CopyTo( path.polys );

		int count = queries[0].Funnel( mesh.State, corridor, states[agent].Position, a.RouteEnd, path.corners, default, false );
		if ( count == 0 ) return PathStatus.NotFound;

		float length = 0;
		for ( int i = 1; i < count; i++ ) length += Vector3.Distance( path.corners[i - 1], path.corners[i] );
		path.polyCount = corridor.Length;
		path.cornerCount = count;
		path.Start = path.corners[0];
		path.End = a.RouteEnd;
		path.Length = length;
		return path.Status = a.Has( AgentFlags.Partial ) ? PathStatus.Partial : PathStatus.Complete;
	}

	/// <summary>
	/// Up to 16 corners of the route ahead as the agent's last walking substep saw them, starting where
	/// the agent was then and ending at the next link entry or the end of the route. An agent that
	/// has stopped keeps the last ones; a replaced route has none until the next Step. The span is
	/// over the crowd's own storage: take it again after a Step or Add.
	/// </summary>
	public ReadOnlySpan<Vector3> GetCorners( int agent )
	{
		return Live( agent ) ? corners.AsSpan( agent * MaxCorners, agents[agent].CornerCount ) : default;
	}

	bool Live( int agent ) => (uint)agent < (uint)high && agents[agent].Has( AgentFlags.Active );

	// A call takes the agent off its link. The next Step enters none, so States show it off the link once.
	void LeaveLink( int agent )
	{
		if ( states[agent].Link < 0 ) return;
		states[agent].Link = -1;
		agents[agent].LinkStep = step + 1;
	}

	void Grow( int capacity )
	{
		Array.Resize( ref parms, capacity );
		Array.Resize( ref states, capacity );
		Array.Resize( ref agents, capacity );
		Array.Resize( ref corridors, capacity );
		Array.Resize( ref corners, capacity * MaxCorners );
		Array.Resize( ref walls, capacity * MaxWalls * 2 );
		Array.Resize( ref slots, capacity );
		Array.Resize( ref buckets, capacity );
		shown = new Frame( capacity );
	}

	AgentParams Sanitize( AgentParams value )
	{
		float least = eps / 10f;
		value.Radius = value.Radius > least ? value.Radius : least;
		value.Height = value.Height > least ? value.Height : least;
		value.MaxSpeed = value.MaxSpeed > 0 ? value.MaxSpeed : 0;
		value.Acceleration = value.Acceleration > 0 ? value.Acceleration : 0;
		value.Separation = value.Separation > 0 ? MathF.Min( value.Separation, 1 ) : 0;
		return value;
	}

	// The boxes an agent and its target are looked for in, never smaller than the agent's own size asks for.
	Vector3 PlacementBox( in AgentParams p ) => Vector3.Max( PlacementExtents, OwnBox( p ) );
	Vector3 TargetBox( in AgentParams p ) => Vector3.Max( TargetExtents, OwnBox( p ) );
	static Vector3 OwnBox( in AgentParams p ) => new( p.Radius * 2.1f, p.Radius * 2.1f, p.Height * 1.51f );

	// An agent that does not walk. The velocity of an External agent is the caller's.
	static void Halt( ref AgentState state, in AgentParams p )
	{
		state.DesiredVelocity = default;
		if ( !p.External ) state.Velocity = default;
	}

	// Writes the two fields of AgentState that summarise private state.
	void Publish( int i )
	{
		ref readonly var a = ref agents[i];
		ref var state = ref states[i];
		// After SetPosition the polygon stays unknown, whatever is published in between, until the plan has looked.
		bool moved = a.Has( AgentFlags.NeedsRepair ) && state.Poly.IsNone;
		state.Poly = state.Link < 0 && a.Count > 0 && !moved ? corridors[i][0] : default;
		state.Status = state.Link >= 0 ? AgentStatus.OnLink
			: !a.Has( AgentFlags.HasTarget ) ? (a.Has( AgentFlags.AtEnd ) ? AgentStatus.Arrived : AgentStatus.Idle)
			: a.Has( AgentFlags.HasRoute ) ? (a.Has( AgentFlags.AtEnd ) ? AgentStatus.Blocked : AgentStatus.Moving)
			: a.Has( AgentFlags.NeedsPlan ) ? AgentStatus.Pending : AgentStatus.Unreachable;
	}

	static Vector2 Flat( Vector3 v ) => new( v.X, v.Y );
}
