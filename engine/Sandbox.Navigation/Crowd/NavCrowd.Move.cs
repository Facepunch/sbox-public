using System.Numerics;

namespace Nav;

public sealed partial class NavCrowd
{
	// Set by any body of the separate stage that pushed an agent.
	int overlapped;

	// Steer and move, per agent: avoid neighbours and walls, accelerate towards the result and walk
	// the surface.
	void Walk( int i, MeshState s, NavQuery query )
	{
		ref readonly var a = ref agents[i];
		ref readonly var p = ref parms[i];
		ref var state = ref states[i];

		var wish = Flat( state.DesiredVelocity );
		var steered = SteerWalls( i, s, query, Avoid( i, wish, dt ), a.End, a.CornerDistance );
		var velocity = Flat( state.Velocity );
		var change = steered - velocity;
		float most = p.Acceleration * dt, wanted = change.Length();
		if ( wanted > most ) change *= most / wanted;
		velocity += change;

		// Stop on the end of the route instead of stepping over it.
		var displacement = velocity * dt;
		float length = displacement.Length();
		if ( length > a.Stride ) displacement *= a.Stride / length;

		var old = state.Position;
		MoveOnMesh( i, s, query, displacement );
		state.Velocity = (state.Position - old) / dt;
		Place( i );
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

	// With AutoLinks the crowd takes the agent across: it walks it along the link in a straight line at
	// full speed, or, for an External agent, points DesiredVelocity at the far end and waits for the
	// caller to bring it within its radius of that. A link entered in this Step is held until the
	// next one, however short it is, so the entry is seen from outside.
	void WalkLink( int i )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		if ( a.LinkStep == step || !p.AutoLinks ) return;

		var offset = state.LinkEnd - state.Position;
		float distance = offset.Length(), stride = p.MaxSpeed * dt;
		if ( p.External && distance > MathF.Max( eps, p.Radius ) )
		{
			state.DesiredVelocity = offset * (p.MaxSpeed / distance);
			return;
		}
		if ( !p.External && distance > stride )
		{
			state.Position += offset * (stride / distance);
			return;
		}

		// The corridor already starts at the polygon the link ends on.
		if ( !p.External ) state.Position = state.LinkEnd;
		state.Link = -1;
		Halt( ref state, p );
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
			overlapped = 0;
			Run( Stage.Separate );
			if ( overlapped == 0 ) break;
			pushed = true;
		}
		if ( pushed ) Run( Stage.Constrain );
	}

	// Moves the agent out of its overlaps by 0.7 of their mean depth: half of each when the other
	// yields too, all of it when the other is External.
	bool Separate( int i )
	{
		int self = slots[i], bucket = buckets[i];
		float x = frame.X[self], y = frame.Y[self], z = frame.Z[self], top = frame.Top[self], radius = frame.Radius[self], tiny = eps * 0.01f;
		var push = Vector2.Zero;
		int count = 0;
		for ( int n = 0; Cells( bucket, 1, n, out int slot, out int last ); n++ )
		{
			for ( ; slot < last; slot++ )
			{
				float dx = x - frame.X[slot], dy = y - frame.Y[slot], sum = radius + frame.Radius[slot], squared = dx * dx + dy * dy;
				if ( squared >= sum * sum || slot == self || top <= frame.Z[slot] || frame.Top[slot] <= z ) continue;
				float distance = MathF.Sqrt( squared );
				var normal = distance > tiny ? new Vector2( dx, dy ) / distance : new Vector2( i < frame.Id[slot] ? -1 : 1, 0 );
				push += normal * ((sum - distance) * (frame.Yields[slot] ? 0.5f : 1));
				count++;
			}
		}
		if ( count == 0 ) return false;
		agents[i].Push += push * (0.7f / count);
		Place( i );
		return true;
	}
}
