using System.Numerics;

namespace Nav;

public sealed partial class NavCrowd
{
	// Plan, per agent: check and repair the corridor, find the polygon and search when needed, then
	// decide what the agent does this substep.
	void Plan( int i, MeshState s, NavQuery query )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		a.Stride = -1;

		if ( a.Has( AgentFlags.Validate ) )
		{
			a.Flags &= ~AgentFlags.Validate;
			Check( i, s );
		}

		if ( a.Has( AgentFlags.NeedsRepair ) )
		{
			a.Flags &= ~AgentFlags.NeedsRepair;
			if ( state.Poly.IsNone )
			{
				// The caller moved the agent. Without its polygon or a way back into the corridor it starts over.
				if ( a.Has( AgentFlags.NeedsPlan ) || !Reattach( i, s, query ) ) a.Unseat();
			}
			else if ( a.Count > 1 && a.Has( AgentFlags.HasRoute ) && !a.Has( AgentFlags.NeedsPlan ) )
			{
				// A move stepped out of the corridor. Failing to join it further on leaves the way back in.
				Rejoin( i, s, query, corridors[i][0] );
			}
		}

		if ( a.Count == 0 )
		{
			// Off the mesh. Looked for again when the mesh, the position or the params change.
			if ( !a.Has( AgentFlags.NeedsPlan ) ) return;
			if ( !query.Nearest( s, state.Position, PlacementBox( p ), p.Filter, null, 0, out uint id, out var point ) )
			{
				a.Flags &= ~AgentFlags.NeedsPlan;
				Halt( ref state, p );
				return;
			}
			if ( !p.External ) state.Position = point;
			corridors[i][0] = s.RefOf( id );
			a.Count = 1;
		}

		if ( a.Has( AgentFlags.NeedsPlan ) )
		{
			a.Flags &= ~AgentFlags.NeedsPlan;
			if ( a.Has( AgentFlags.HasTarget | AgentFlags.AtEnd ) ) Search( i, s, query );
		}

		Decide( i, s, query );
	}

	// After a mesh or filter change: the corridor ends before its first stale entry, and a route that
	// was cut, stops short or is missing is searched again.
	void Check( int i, MeshState s )
	{
		ref var a = ref agents[i];
		var corridor = corridors[i];
		int valid = 0;
		while ( valid < a.Count && s.IsValid( corridor[valid], parms[i].Filter ) ) valid++;
		if ( valid == 0 )
		{
			a.Unseat();
			return;
		}

		bool goal = a.Has( AgentFlags.HasTarget | AgentFlags.AtEnd );
		if ( valid < a.Count || (goal && (a.Has( AgentFlags.Partial ) || !a.Has( AgentFlags.HasRoute ))) ) a.Flags |= AgentFlags.NeedsPlan;
		a.Count = goal ? valid : 1;
	}

	// After SetPosition: find the polygon under the agent and cut the corridor to it, or join the
	// corridor from there. False when the agent is off the mesh or cannot join.
	bool Reattach( int i, MeshState s, NavQuery query )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		if ( !query.Nearest( s, state.Position, PlacementBox( p ), p.Filter, null, 0, out uint id, out var point ) ) return false;
		if ( !p.External ) state.Position = point;

		var poly = s.RefOf( id );
		int at = corridors[i].AsSpan( 0, a.Count ).IndexOf( poly );
		if ( at < 0 )
		{
			// Off its corridor, perhaps across a wall from where its walls were collected.
			a.WallCount = -1;
			return Rejoin( i, s, query, poly );
		}
		Advance( i, at );
		return true;
	}

	// A short search from a polygon towards the end of the corridor, which then continues from the
	// furthest of its polygons the search reached. False leaves the corridor as it is.
	bool Rejoin( int i, MeshState s, NavQuery query, PolyRef from )
	{
		ref var a = ref agents[i];
		if ( !query.Repair( s, from, states[i].Position, a.RouteEnd, parms[i].Filter, ref corridors[i], ref a.Count, RepairExpansions ) ) return false;
		a.CornerCount = 0;
		return true;
	}

	// Drops the first entries of the corridor.
	void Advance( int i, int entries )
	{
		ref var a = ref agents[i];
		a.Count -= entries;
		Array.Copy( corridors[i], entries, corridors[i], 0, a.Count );
	}

	// Snaps the goal and searches from the agent's polygon. A failure leaves the agent without a route
	// until something changes.
	void Search( int i, MeshState s, NavQuery query )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		ref var corridor = ref corridors[i];
		var start = corridor[0];
		var goal = a.Has( AgentFlags.HasTarget ) ? state.Target : a.RouteEnd;

		// The search writes over the corridor. start is all that is kept of it when nothing is found.
		var status = PathStatus.NotFound;
		int count = 0;
		Vector3 reached = default;
		bool found = query.Nearest( s, goal, TargetBox( p ), p.Filter, null, 0, out uint id, out var point )
			&& (status = query.FindCorridor( s, start, state.Position, id, point, p.Filter, ref corridor, out count, out reached )) >= PathStatus.Partial
			&& corridor[0] == start;

		// A new target is not reached yet, wherever the agent stands. A rest point stays one.
		if ( a.Has( AgentFlags.HasTarget ) ) a.Flags &= ~AgentFlags.AtEnd;
		a.Flags &= ~(AgentFlags.HasRoute | AgentFlags.Partial);
		if ( !found )
		{
			corridor[0] = start;
			a.Count = 1;
			return;
		}

		a.Count = count;
		a.CornerCount = 0;
		a.RouteEnd = reached;
		a.Flags |= AgentFlags.HasRoute | (status == PathStatus.Partial ? AgentFlags.Partial : 0);
		if ( a.Has( AgentFlags.HasTarget ) ) state.RouteTarget = state.Target;
	}

	// Link entry, arrival, or the velocity the agent wants.
	void Decide( int i, MeshState s, NavQuery query )
	{
		ref var a = ref agents[i];
		ref var state = ref states[i];
		ref readonly var p = ref parms[i];
		if ( a.Count == 0 || !a.Has( AgentFlags.HasTarget | AgentFlags.AtEnd ) ) return;
		if ( !a.Has( AgentFlags.HasRoute ) )
		{
			Halt( ref state, p );
			return;
		}

		var position = state.Position;
		float rest = p.Radius * 0.25f;
		if ( a.Has( AgentFlags.AtEnd ) && Vector2.DistanceSquared( Flat( position ), Flat( a.RouteEnd ) ) < rest * rest && MathF.Abs( position.Z - a.RouteEnd.Z ) < floorGap )
		{
			Halt( ref state, p );
			return;
		}

		// The agent heads for the first link entry, else for the end of the route. Corners are pulled
		// up to there: a link has no width and would vanish from a funnel on the ground plane.
		var corridor = corridors[i].AsSpan( 0, a.Count );
		int link = 0;
		while ( link < corridor.Length && !corridor[link].IsLink ) link++;
		bool linkAhead = link < corridor.Length;
		Vector3 end = a.RouteEnd, exit = default;
		if ( link == 0 || (linkAhead && !NavQuery.LinkEnds( s, corridor[link], corridor[link - 1].Id, PolyRef.NoId, out end, out exit )) )
		{
			a.Flags |= AgentFlags.NeedsPlan;
			Halt( ref state, p );
			return;
		}

		float distance = Vector2.Distance( Flat( position ), Flat( end ) );
		bool level = MathF.Abs( position.Z - end.Z ) < floorGap;
		// Every agent gets on within its radius of the link's start: two heading for one start cannot both reach it.
		if ( linkAhead && distance <= MathF.Max( eps, p.Radius ) && level )
		{
			// One link transition per Step, so a caller comparing States between steps sees every one:
			// an agent that left a link in this Step waits here for the next.
			if ( a.LinkStep != step )
			{
				state.Link = corridor[link].Link;
				state.LinkStart = end;
				state.LinkEnd = exit;
				state.LinkEntry = position;
				a.LinkStep = step;
				Advance( i, link + 1 );
			}
			// Crossing on its own, the agent goes onto the link at the speed it arrives with.
			state.DesiredVelocity = default;
			if ( !p.AutoLinks || state.Link < 0 ) Halt( ref state, p );
			return;
		}

		// Close to an end that resting neighbours hold, it rests where it stands: the end is there now. A pack is
		// looked for within two diameters of the two largest round the end.
		if ( !linkAhead && level && !a.Has( AgentFlags.AtEnd ) && walkers > 0 && distance <= 4 * (p.Radius + largestRadius) && Held( i, position, end, distance, state.RouteTarget, p ) )
		{
			a.RouteEnd = position;
			a.Count = 1;
			a.CornerCount = 0;
			a.Flags |= AgentFlags.AtEnd;
			if ( !a.Has( AgentFlags.Partial ) ) a.Flags &= ~AgentFlags.HasTarget;
			Halt( ref state, p );
			return;
		}

		// An agent the crowd moves lands on the end exactly. One that rests, or is moved by the caller,
		// has arrived within a quarter radius.
		if ( !linkAhead && distance < (a.Has( AgentFlags.AtEnd ) || p.External ? rest : eps) && level )
		{
			a.Flags |= AgentFlags.AtEnd;
			if ( !a.Has( AgentFlags.Partial ) ) a.Flags &= ~AgentFlags.HasTarget;
			Halt( ref state, p );
			return;
		}

		// Pulling the corners walks the corridor up to the first of them, every substep, for a result
		// that does not change while the agent walks towards that corner. They are kept while the agent
		// stands on the same polygon with as many ahead and the same end, and is within a quarter
		// radius of the line from where they were pulled to the first corner, and short of it. Without
		// the line an agent pushed aside inside a large polygon would head for a corner that is no
		// longer on its way.
		var corner = corners.AsSpan( i * MaxCorners, MaxCorners );
		int count = a.CornerCount;
		if ( count >= 2 && a.CornerPoly == corridor[0] && a.CornerSpan == link && a.End == end
			&& Geo.DistanceToSegmentSquared( position, new Vector3( a.CornerFrom, 0 ), corner[1], out _ ) <= rest * rest
			&& Vector2.DistanceSquared( Flat( position ), Flat( corner[1] ) ) > eps * eps )
		{
			corner[0] = position;
		}
		else
		{
			count = query.Funnel( s, corridor[..link], position, end, corner, default, false );
			a.CornerCount = count;
			a.CornerPoly = corridor[0];
			a.CornerSpan = link;
			a.CornerFrom = Flat( position );
			a.End = end;
			if ( count == 0 )
			{
				a.Flags |= AgentFlags.NeedsPlan;
				Halt( ref state, p );
				return;
			}
		}

		Vector2 direction = default;
		int next = 0;
		for ( ; next < count - 1; next++ )
		{
			direction = Flat( corner[next] - position );
			if ( direction.LengthSquared() > eps * eps ) break;
		}
		if ( next == count - 1 ) direction = Flat( corner[next] - position );

		float length = direction.Length();
		// The plan acts one substep late: it brakes for where the agent will be by then.
		float speed = length > 0 ? Speed( i, next, direction / length, length - MathF.Min( Going( i ).Length() * Ahead( i ), length ) ) : 0;
		state.DesiredVelocity = length > 0 ? new Vector3( direction * (speed / length), 0 ) : default;
		a.CornerDistance = length;
		a.Stride = distance;
	}

	// Whether the end is held by a neighbour sent to the same target, so the agent stops where it stands: one
	// touching it covers the end (stands where the agent would overlap it there) and rests or goes at under a
	// quarter of the agent's MaxSpeed; or one touching it and nearer to the end rests while the agent, pressing on,
	// has slowed to under that speed. So a group sent to one point packs round whoever reaches it. Read from the
	// frame of the last substep, which no stage writes while agents plan.
	bool Held( int i, Vector3 position, Vector3 end, float distance, Vector3 target, in AgentParams p )
	{
		float gap = p.Radius * 0.25f, still = p.MaxSpeed * 0.25f;
		int bucket = ((int)MathF.Floor( position.X * perCell ) & gridMask) | (((int)MathF.Floor( position.Y * perCell ) & gridMask) << gridBits);
		int reach = (int)MathF.Ceiling( (p.Radius + largestRadius + gap) / cellSize );
		float top = position.Z + p.Height;
		bool slow = Flat( states[i].Velocity ).LengthSquared() < still * still;
		for ( int n = 0; Cells( bucket, reach, n, out int slot, out int last ); n++ )
		{
			for ( ; slot < last; slot++ )
			{
				if ( frame.Id[slot] == i || !(top > frame.Z[slot]) || !(frame.Top[slot] > position.Z) ) continue;
				float touch = p.Radius + frame.Radius[slot];
				float dx = position.X - frame.X[slot], dy = position.Y - frame.Y[slot];
				// Only neighbours sent to the same target.
				float tx = frame.TargetX[slot] - target.X, ty = frame.TargetY[slot] - target.Y;
				if ( dx * dx + dy * dy > (touch + gap) * (touch + gap) || !(tx * tx + ty * ty < gap * gap) ) continue;
				float ex = end.X - frame.X[slot], ey = end.Y - frame.Y[slot], fromEnd = ex * ex + ey * ey;
				float vx = frame.VelocityX[slot], vy = frame.VelocityY[slot];
				if ( fromEnd < touch * touch && (frame.Rest[slot] != 0 || vx * vx + vy * vy < still * still) ) return true;
				// Nearer to the end, the lower id first when as near.
				if ( frame.Rest[slot] != 0 && slow && (fromEnd < distance * distance || (fromEnd == distance * distance && frame.Id[slot] < i)) ) return true;
			}
		}
		return false;
	}

	// The speed towards corner next from which the agent can still slow down to the turn speed of every corner on
	// the way and stop at the end, measured along the corners. Corners beyond the braking distance of the speed so
	// far cannot ask for less.
	float Speed( int i, int next, Vector2 heading, float travelled )
	{
		ref readonly var a = ref agents[i];
		ref readonly var p = ref parms[i];
		var corner = corners.AsSpan( i * MaxCorners, a.CornerCount );
		int last = corner.Length - 1;
		float braking = 2 * p.Acceleration, squared = p.MaxSpeed * p.MaxSpeed;
		if ( next == last )
		{
			// Moving at an angle to the way to the end or a link entry, it has to turn onto it before it gets there.
			var velocity = Going( i );
			float speed = velocity.Length();
			if ( speed > 0 ) squared = MathF.Min( squared, TurnSpeedSquared( velocity / speed, heading * travelled, float.PositiveInfinity, p ) );
		}
		else if ( !(braking * travelled < squared) ) return p.MaxSpeed;
		for ( int k = next; k < last && braking * travelled < squared; k++ )
		{
			var leg = Flat( corner[k + 1] - corner[k] );
			float length = leg.Length();
			if ( length == 0 ) continue;
			leg /= length;
			// Turns the same way less than a diameter apart, as erosion rounds a corner into, are one turn.
			int target = k + 1;
			var on = leg;
			float side = heading.X * leg.Y - heading.Y * leg.X;
			for ( float reach = length; reach < p.Radius * 2 && target < last; target++ )
			{
				var after = Flat( corner[target + 1] - corner[target] );
				reach = after.Length();
				if ( reach == 0 || (on.X * after.Y - on.Y * after.X) * side <= 0 ) break;
				on = after / reach;
			}
			squared = MathF.Min( squared, TurnSpeedSquared( heading, Flat( corner[target] - corner[k] ), p.Radius, p ) + braking * travelled );
			heading = leg;
			travelled += length;
		}
		// The end, or the last corner short of it when the corners filled the buffer.
		return MathF.Sqrt( MathF.Min( squared, braking * (travelled + Vector2.Distance( Flat( corner[last] ), Flat( a.End ) )) ) );
	}

	// The least the agent turns by in a unit of time, whatever its speed: at MaxSpeed it goes round a circle of one
	// and a half times its radius. Where Acceleration across its way turns it faster, at low speed, that counts.
	static float TurnRate( in AgentParams p ) => p.MaxSpeed / (1.5f * p.Radius);

	// The speed squared for a turn from heading towards the point at to. The move turns on an arc (Accelerate),
	// which has to stay within room of where it starts, 2 R sin( angle / 2 ) for a radius R, and to leave the point
	// outside its circle, or the agent would circle it. The speed is the fastest the agent turns on that radius at:
	// with Acceleration across its way, or at its turn rate.
	static float TurnSpeedSquared( Vector2 heading, Vector2 to, float room, in AgentParams p )
	{
		float reach = to.Length(), cos = Vector2.Dot( heading, to ), sin = MathF.Abs( heading.X * to.Y - heading.Y * to.X );
		if ( !(reach - cos > 1e-6f * reach) ) return float.PositiveInfinity;
		float radius = MathF.Min( room / MathF.Sqrt( 2 * (1 - cos / reach) ), reach * reach / (2 * (cos > 0 ? sin : reach)) ), turned = radius * TurnRate( p );
		return MathF.Max( p.Acceleration * radius, turned * turned );
	}
}
