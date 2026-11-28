using System.Numerics;
using System.Runtime.Intrinsics;

namespace Nav;

public sealed partial class NavCrowd
{
	// Steer and move, per agent: avoid neighbours and walls, accelerate towards the result and walk
	// the surface. A Steered External agent is steered alike, once a Step over the time the Step covers, from the
	// velocity the crowd steered it at before, and left where its owner put it: an owner that lags or is held up does
	// not change how it speeds up and turns.
	void Walk( int i, MeshState s, NavQuery query )
	{
		ref readonly var a = ref agents[i];
		ref readonly var p = ref parms[i];
		ref var state = ref states[i];

		float span = p.External ? stepTime : dt;
		var wish = Flat( state.DesiredVelocity );
		var steered = SteerWalls( i, s, query, Avoid( i, wish, span ), a.End, a.CornerDistance );
		// A Steered External agent turns through the Step's substeps in substep-sized turns, as a crowd-moved one does.
		var velocity = Going( i );
		for ( int k = p.External ? substeps : 1; k > 0; k-- ) velocity = Accelerate( velocity, steered, p.Acceleration * dt, TurnRate( p ) * dt );

		// Land on the end of the route instead of stepping over or past it: in this substep, or for a Steered External
		// agent, over the Step its owner moves it by.
		if ( p.External )
		{
			if ( velocity.Length() * stepDelta > a.Stride ) velocity = Flat( a.End - state.Position ) / stepDelta;
			state.SteeredVelocity = new Vector3( velocity, 0 );
			return;
		}
		var displacement = velocity * dt;
		if ( displacement.Length() > a.Stride ) displacement = Flat( a.End - state.Position );
		state.SteeredVelocity = new Vector3( displacement / dt, 0 );

		var old = state.Position;
		MoveOnMesh( i, s, query, displacement );
		state.Velocity = (state.Position - old) / dt;
		Place( i );
	}

	// The velocity the agent goes at as far as its turns and speed go: the crowd's own for a Steered External agent,
	// whose owner may not follow it exactly, else its movement.
	Vector2 Going( int i ) => parms[i].External && parms[i].Steered ? Flat( states[i].SteeredVelocity ) : Flat( states[i].Velocity );

	// How long the agent goes at that before it is planned from a new position: a substep, or the Step for a Steered
	// External agent, whose owner moves it once a Step while the crowd plans it from the same place every substep.
	float Ahead( int i ) => parms[i].External && parms[i].Steered ? stepDelta : dt;

	// The velocity after one substep: the speed changes by at most most, the heading by at most turn (an angle,
	// small) or what most allows across the agent's way if that is more. While the agent moves and steered is less
	// than a right angle off its way, it turns towards it and speeds up only once it heads that way, so it follows
	// the arc the corner speeds are planned for instead of cutting the corner of velocity space and losing speed.
	static Vector2 Accelerate( Vector2 velocity, Vector2 steered, float most, float turn )
	{
		float speed = velocity.Length(), target = steered.Length();
		if ( speed > most && target > 0 )
		{
			var way = velocity / speed;
			float cos = Vector2.Dot( way, steered ), sin = way.X * steered.Y - way.Y * steered.X;
			if ( cos > 0 )
			{
				var side = sin > 0 ? new Vector2( -way.Y, way.X ) : new Vector2( way.Y, -way.X );
				float aim = MathF.Abs( sin ) / cos;
				turn = MathF.Min( aim, MathF.Max( turn, most / speed ) );
				float next = target < speed ? MathF.Max( target, speed - most ) : turn < aim ? speed : MathF.Min( target, speed + most );
				return (way + side * turn) * (next / MathF.Sqrt( 1 + turn * turn ));
			}
		}
		var change = steered - velocity;
		float wanted = change.Length();
		return wanted > most ? velocity + change * (most / wanted) : steered;
	}

	// Slides the agent along the surface and makes the corridor start at the polygon it ends on.
	void MoveOnMesh( int i, MeshState s, NavQuery query, Vector2 displacement )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref var corridor = ref corridors[i];
		Span<PolyRef> visited = stackalloc PolyRef[VisitedCapacity];
		var poly = corridor[0];
		var position = state.Position;
		if ( !query.MoveOnSurface( s, ref poly, ref position, new Vector3( displacement, 0 ), parms[i].Filter, visited, out int count ) )
		{
			a.Flags |= AgentFlags.Validate;
			return;
		}
		state.Position = position;
		if ( count <= 1 ) return;
		state.Poly = poly;

		// The corridor goes on from the furthest of its polygons the move passed through.
		int common = -1, at = -1;
		for ( int k = a.Count - 1; k >= 0 && common < 0; k-- )
		{
			for ( int v = count - 1; v >= 0; v-- )
			{
				if ( corridor[k] != visited[v] ) continue;
				common = k;
				at = v;
				break;
			}
		}
		if ( common < 0 || visited[count - 1] != poly )
		{
			// The move crossed more polygons than fit the buffer: start over from where it ended.
			corridor[0] = poly;
			a.Count = 1;
			a.Flags = a.Flags & ~AgentFlags.HasRoute | AgentFlags.NeedsPlan;
			return;
		}
		Advance( i, common );

		// What the move crossed after that is outside the corridor, from sliding along a wall or
		// around a neighbour. It goes in front, so the corridor leads back in, and the next plan looks
		// for a way to join it further on.
		int extra = count - 1 - at;
		if ( extra == 0 ) return;
		if ( corridor.Length < a.Count + extra ) Array.Resize( ref corridor, Math.Max( a.Count + extra, corridor.Length * 2 ) );
		Array.Copy( corridor, 0, corridor, extra, a.Count );
		for ( int v = 0; v < extra; v++ ) corridor[v] = visited[count - 1 - v];
		a.Count += extra;
		a.Flags |= AgentFlags.NeedsRepair;
	}

	// A Steered External agent is not moved: the push separation gave it goes to AgentState.Push for its owner, moved
	// along the surface from the mesh point under the agent, since an owner may stand off the polygon.
	void PushOwner( int i, MeshState s, NavQuery query )
	{
		ref readonly var a = ref agents[i];
		ref var state = ref states[i];
		var push = a.Push;
		if ( a.Count > 0 )
		{
			Span<PolyRef> visited = stackalloc PolyRef[VisitedCapacity];
			var poly = corridors[i][0];
			var from = state.Position;
			if ( query.MoveOnSurface( s, ref poly, ref from, default, parms[i].Filter, visited, out _ ) )
			{
				var to = from;
				if ( query.MoveOnSurface( s, ref poly, ref to, new Vector3( push, 0 ), parms[i].Filter, visited, out _ ) ) push = Flat( to - from );
			}
		}
		state.Push = new Vector3( push, 0 );
	}

	// With AutoLinks the crowd takes the agent across: it walks it along the link in a straight line,
	// speeding up to MaxSpeed, or, for an External agent, points DesiredVelocity at the far end and waits for the
	// caller to bring it within its radius of that. A link entered in this Step is held until the
	// next one, however short it is, so the entry is seen from outside.
	void WalkLink( int i )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		if ( a.LinkStep == step || !p.AutoLinks ) return;

		// Across the link the agent speeds up from the speed it came with to MaxSpeed.
		var offset = state.LinkEnd - state.Position;
		var velocity = state.Velocity;
		float speed = MathF.Min( p.MaxSpeed, MathF.Sqrt( velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z ) + p.Acceleration * dt );
		float distance = offset.Length(), stride = speed * dt;
		if ( p.External && distance > MathF.Max( eps, p.Radius ) )
		{
			state.DesiredVelocity = offset * (p.MaxSpeed / distance);
			return;
		}
		if ( !p.External && distance > stride )
		{
			state.Velocity = offset * (speed / distance);
			state.Position += offset * (stride / distance);
			return;
		}

		// The corridor already starts at the polygon the link ends on. The agent goes on with the way across it
		// has along the ground.
		if ( !p.External ) state.Position = state.LinkEnd;
		state.Link = -1;
		state.DesiredVelocity = default;
		// A Steered External agent goes on as it was told to cross, along the ground.
		if ( p.External && p.Steered ) state.SteeredVelocity = distance > 0 ? new Vector3( offset.X, offset.Y, 0 ) * (p.MaxSpeed / distance) : default;
		if ( !p.External ) state.Velocity = distance > 0 ? new Vector3( offset.X, offset.Y, 0 ) * (speed / distance) : new Vector3( state.Velocity.X, state.Velocity.Y, 0 );
		a.LinkStep = step;
		a.WallCount = -1;
		Publish( i );
	}

	// Separate: push overlapping agents apart. Up to four rounds, each binning the agents where the
	// move or the round before left them, then one move along the surface per agent so nobody is
	// pushed through a wall.
	void Separate()
	{
		if ( walkers < 2 ) return;
		bool pushed = false;
		for ( int round = 0; round < 4; round++ )
		{
			Bin();
			claims.Overlapped = 0;
			Run( Stage.Separate );
			if ( claims.Overlapped == 0 ) break;
			pushed = true;
		}
		if ( pushed ) Run( Stage.Constrain );
	}

	// What one agent collects from its overlaps while separating.
	struct Separation
	{
		public float X, Y, Z, Top, Radius, Tiny;
		public int Id, Slot, Count;
		public Vector2 Push;

		// Only the eight-wide pass writes these, lane by lane.
		public Vector256<float> SumX, SumY;
	}

	// Moves the agent out of its overlaps by 0.7 of their mean depth: half of each when the other
	// yields too, all of it when the other is External.
	bool Separate( int i )
	{
		int self = slots[i], bucket = buckets[i];
		var scan = new Separation
		{
			X = frame.X[self],
			Y = frame.Y[self],
			Z = frame.Z[self],
			Top = frame.Top[self],
			Radius = frame.Radius[self],
			Tiny = eps * 0.01f,
			Id = i,
			Slot = self
		};
		if ( EightWide ) SeparateEight( ref scan, bucket );
		for ( int n = 0; !EightWide && Cells( bucket, 1, n, out int slot, out int last ); n++ )
		{
			for ( ; slot < last; slot++ ) Separate( ref scan, slot );
		}
		if ( scan.Count == 0 ) return false;
		var push = scan.Push + new Vector2( Vector256.Sum( scan.SumX ), Vector256.Sum( scan.SumY ) );
		agents[i].Push += push * (0.7f / scan.Count);
		Place( i );
		return true;
	}

	// One neighbour: its overlap, if any.
	void Separate( ref Separation scan, int slot )
	{
		float dx = scan.X - frame.X[slot], dy = scan.Y - frame.Y[slot], sum = scan.Radius + frame.Radius[slot], squared = dx * dx + dy * dy;
		if ( squared >= sum * sum || slot == scan.Slot || scan.Top <= frame.Z[slot] || frame.Top[slot] <= scan.Z ) return;
		float distance = MathF.Sqrt( squared );
		var normal = distance > scan.Tiny ? new Vector2( dx, dy ) / distance : new Vector2( scan.Id < frame.Id[slot] ? -1 : 1, 0 );
		scan.Push += normal * ((sum - distance) * frame.Part[slot]);
		scan.Count++;
	}
}
