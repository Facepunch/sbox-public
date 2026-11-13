using System.Numerics;
using System.Runtime.Intrinsics;

namespace Nav;

public sealed partial class NavCrowd
{
	// The frame: what agents see of each other, sorted by grid bucket. Bucket b holds the frame slots
	// bucketStart[b] .. bucketStart[b + 1] in id order, and the buckets of one row of cells follow each
	// other, so the cells looked at in a row are one run of slots.
	Frame frame;
	int[] bucketStart = [];

	// What each agent shows the others, by agent id and written by the body that last moved it, so
	// that binning reads a few packed lines and not every agent's own. Id is -1 for an agent that is
	// not in the frame. slots and buckets, also by id, are where the last binning put it.
	Frame shown;
	int[] slots = [], buckets = [];

	// What each body of the plan stage saw of the agents it put up for the frame.
	struct Tally
	{
		public float Radius, Look;
		public int Count;
	}

	readonly Tally[] tallies;
	int walkers, gridBits, gridMask;
	float cellSize, largestRadius;

	static readonly Vector2[] SamplePattern = CreatePattern();

	// The time over which a speed counts as a distance in neighbour and wall ranges.
	float Reach => AvoidHorizon * (4f / 3f);

	// The end of the plan stage for one agent: whether the others have to mind it, and what they see
	// of it. Those are the agents on the mesh and not on a link, and every External agent wherever it
	// is: a body on a table or in the air is in the way of whoever overlaps it in height.
	void Show( int i, ref Tally tally, float reach )
	{
		ref var a = ref agents[i];
		ref readonly var p = ref parms[i];
		ref readonly var state = ref states[i];
		if ( !p.External && (a.Count == 0 || state.Link >= 0) )
		{
			shown.Id[i] = -1;
			return;
		}
		a.Push = default;
		shown.Id[i] = 0;
		shown.Radius[i] = p.Radius;
		shown.VelocityX[i] = state.Velocity.X;
		shown.VelocityY[i] = state.Velocity.Y;
		shown.Yields[i] = !p.External;
		Place( i );
		tally.Radius = MathF.Max( tally.Radius, p.Radius );
		tally.Look = MathF.Max( tally.Look, p.MaxSpeed * reach );
		tally.Count++;
	}

	// Where the next binning takes the agent to be: where it stands plus the push it has collected.
	void Place( int i )
	{
		ref readonly var state = ref states[i];
		var push = agents[i].Push;
		shown.X[i] = state.Position.X + push.X;
		shown.Y[i] = state.Position.Y + push.Y;
		shown.Z[i] = state.Position.Z;
		shown.Top[i] = state.Position.Z + parms[i].Height;
	}

	// Snapshot: size the grid for the agents the plan stage put up and bin them. A cell is at least
	// two radii wide, so agents that touch are in neighbouring cells, and at least a sixth of the
	// widest range an agent avoids others in. Buckets wrap, with more per axis than any lookup is
	// wide: the cells of one lookup never share a bucket, and agents of far cells that do share one
	// fail the distance test.
	void Snapshot()
	{
		float radius = 0, look = 0;
		int count = 0;
		foreach ( ref readonly var tally in tallies.AsSpan() )
		{
			radius = MathF.Max( radius, tally.Radius );
			look = MathF.Max( look, tally.Look );
			count += tally.Count;
		}
		walkers = count;
		largestRadius = radius;
		if ( count == 0 )
		{
			slots.AsSpan( 0, high ).Fill( -1 );
			return;
		}

		float widest = radius * 4 + look;
		cellSize = MathF.Max( radius * 2, widest / 6 );
		int width = 2 * (int)MathF.Ceiling( widest / cellSize ) + 2;
		gridBits = 2;
		while ( 1 << gridBits < width || 1 << (gridBits * 2) < count * 4 ) gridBits++;
		gridMask = (1 << gridBits) - 1;
		if ( bucketStart.Length < (1 << (gridBits * 2)) + 2 ) bucketStart = new int[(1 << (gridBits * 2)) + 2];
		if ( frame.Capacity < count ) frame = new Frame( Math.Max( count, frame.Capacity * 2 ) );
		Bin();
	}

	// Sorts the agents of the snapshot into the frame by the cell they are shown in now.
	void Bin()
	{
		// Count into bucket + 2, sum, then place through bucket + 1: that entry ends up as the start of
		// the next bucket, which is where it has to be.
		var starts = bucketStart.AsSpan( 0, (1 << (gridBits * 2)) + 2 );
		starts.Clear();
		float perCell = 1f / cellSize;
		for ( int i = 0; i < high; i++ )
		{
			if ( shown.Id[i] < 0 )
			{
				slots[i] = -1;
				continue;
			}
			int bucket = buckets[i] = ((int)MathF.Floor( shown.X[i] * perCell ) & gridMask) | (((int)MathF.Floor( shown.Y[i] * perCell ) & gridMask) << gridBits);
			starts[bucket + 2]++;
		}
		for ( int b = 2; b < starts.Length; b++ ) starts[b] += starts[b - 1];
		for ( int i = 0; i < high; i++ )
		{
			if ( shown.Id[i] < 0 ) continue;
			int slot = slots[i] = starts[buckets[i] + 1]++;
			frame.X[slot] = shown.X[i];
			frame.Y[slot] = shown.Y[i];
			frame.Z[slot] = shown.Z[i];
			frame.Top[slot] = shown.Top[i];
			frame.Radius[slot] = shown.Radius[i];
			frame.VelocityX[slot] = shown.VelocityX[i];
			frame.VelocityY[slot] = shown.VelocityY[i];
			frame.Id[slot] = i;
			frame.Yields[slot] = shown.Yields[i];
		}
	}

	// The frame slots of the cells within reach of the bucket's, as runs: for n = 0, 1, 2 ... until
	// false. Each row of cells is one run, or two where it wraps around the grid; the second is empty
	// where it does not.
	bool Cells( int bucket, int reach, int n, out int first, out int last )
	{
		first = last = 0;
		if ( n > reach * 4 + 1 ) return false;
		int row = (((bucket >> gridBits) - reach + n / 2) & gridMask) << gridBits, from = (bucket - reach) & gridMask, to = (bucket + reach) & gridMask;
		if ( n % 2 == 0 )
		{
			first = bucketStart[row | from];
			last = bucketStart[(row | (from <= to ? to : gridMask)) + 1];
		}
		else if ( from > to )
		{
			first = bucketStart[row];
			last = bucketStart[(row | to) + 1];
		}
		return true;
	}

	// What one agent collects from its neighbours while steering.
	struct Avoidance
	{
		public float X, Y, Z, Top, Radius, WishX, WishY, Look, Horizon, PerStep, Spacing, StillSquared, Tiny, Least;
		public int Id, Slot, Neighbours;
		public Vector2 Correction;

		// Only the eight-wide pass adds to these, lane by lane.
		public Vector256<float> SumX, SumY;
	}

	// Steers the wanted velocity around neighbours: a push out of overlaps, a sidestep away from a
	// predicted collision, and a spacing push that grows with Separation.
	internal Vector2 Avoid( int i, Vector2 wish, float dt )
	{
		ref readonly var p = ref parms[i];
		float wishSpeed = wish.Length(), still = 1e-5f * p.MaxSpeed;
		int self = slots[i], bucket = buckets[i];
		var scan = new Avoidance
		{
			X = frame.X[self],
			Y = frame.Y[self],
			Z = frame.Z[self],
			Top = frame.Top[self],
			Radius = p.Radius,
			WishX = wish.X,
			WishY = wish.Y,
			Look = p.MaxSpeed * Reach,
			Horizon = AvoidHorizon,
			PerStep = 0.5f / MathF.Max( dt, SubstepTime * 0.2f ),
			Spacing = p.Separation * wishSpeed,
			StillSquared = still * still,
			Tiny = eps * 0.01f,
			Least = eps * 0.1f,
			Id = i,
			Slot = self
		};

		int reach = (int)MathF.Ceiling( ((p.Radius + largestRadius) * 2 + scan.Look) / cellSize );
		for ( int n = 0; Cells( bucket, reach, n, out int slot, out int last ); n++ )
		{
			if ( EightWide ) slot = AvoidEight( ref scan, slot, last );
			for ( ; slot < last; slot++ )
			{
				// In range, on the same level, and not the agent itself.
				float dx = scan.X - frame.X[slot], dy = scan.Y - frame.Y[slot], sum = scan.Radius + frame.Radius[slot], range = sum * 2 + scan.Look;
				if ( slot == scan.Slot || !(dx * dx + dy * dy <= range * range) || !(scan.Top > frame.Z[slot]) || !(frame.Top[slot] > scan.Z) ) continue;
				scan.Neighbours++;
				Avoid( ref scan, slot );
			}
		}

		// Averaging keeps a dense group from multiplying the push or overriding arrival braking.
		var correction = scan.Correction + new Vector2( Vector256.Sum( scan.SumX ), Vector256.Sum( scan.SumY ) );
		var result = wish + correction / Math.Max( 1, scan.Neighbours );
		// Avoidance may slow the approach but not cancel it: a quarter of the progress always remains.
		float wishSquared = wishSpeed * wishSpeed, floor = wishSquared * 0.25f, forward = Vector2.Dot( result, wish );
		float slowest = 1e-4f * p.MaxSpeed;
		if ( forward < floor ) result += wish * ((floor - forward) / MathF.Max( wishSquared, slowest * slowest ));
		float speed = result.Length();
		return speed > wishSpeed ? result * (wishSpeed / speed) : result;
	}

	// One neighbour in range.
	void Avoid( ref Avoidance scan, int slot )
	{
		float dx = scan.X - frame.X[slot], dy = scan.Y - frame.Y[slot];
		float squared = dx * dx + dy * dy, sum = scan.Radius + frame.Radius[slot];
		float relativeX = scan.WishX - frame.VelocityX[slot], relativeY = scan.WishY - frame.VelocityY[slot];
		float towards = -(relativeX * dx + relativeY * dy);
		// Only a neighbour within two radii, or one the agent is closing in on, changes anything.
		bool near = squared < sum * sum * 4;
		if ( !near && !(towards > 0) ) return;

		float distance = MathF.Sqrt( squared ), gap = distance - sum;
		var normal = distance > scan.Tiny ? new Vector2( dx / distance, dy / distance ) : new Vector2( scan.Id < frame.Id[slot] ? -1 : 1, 0 );
		// The push out of an overlap comes on top of the rest, not instead of it: two that touch
		// head-on beside a wall are only ever pushed back to touching, and need the sidestep to pass.
		if ( distance < sum ) scan.Correction += normal * (-gap * scan.PerStep);
		if ( !(distance > scan.Tiny) ) return;

		// Closing in alone would divert the agent around neighbours its course already clears: the
		// two must also be predicted to overlap, within the horizon.
		float relativeSquared = relativeX * relativeX + relativeY * relativeY;
		float approach = relativeSquared > scan.StillSquared ? towards / relativeSquared : 0;
		float missX = dx + relativeX * approach, missY = dy + relativeY * approach;
		float closing = towards / distance;
		if ( towards > 0 && missX * missX + missY * missY < sum * sum && gap < closing * scan.Horizon )
		{
			// Always the same passing side, which breaks the symmetry of a head-on meeting.
			var side = new Vector2( -normal.Y, normal.X );
			scan.Correction += (normal + side) * ((closing - gap / scan.Horizon) * 0.5f);
		}
		if ( near ) scan.Correction += normal * ((1 - gap / MathF.Max( sum, scan.Least )) * scan.Spacing);
	}

	// Predicts where the wanted velocity and the current one meet a wall within the horizon, and when
	// either does, samples nearby velocities for the one that best trades progress against impact.
	internal Vector2 SteerWalls( int i, MeshState s, NavQuery query, Vector2 desired, Vector3 goal, float cornerDistance )
	{
		ref var a = ref agents[i];
		ref readonly var p = ref parms[i];
		ref readonly var state = ref states[i];
		float speed = desired.Length();
		if ( speed < 1e-5f * p.MaxSpeed ) return desired;
		// The route turns or ends at the next corner: no straight-line prediction beyond it.
		float horizon = MathF.Min( AvoidHorizon, cornerDistance / speed );
		if ( horizon < 1e-4f * AvoidHorizon ) return desired;

		var position = state.Position;
		var wall = walls.AsSpan( i * MaxWalls * 2, MaxWalls * 2 );
		// Walls are kept until the agent has moved a quarter of the wall range from where they were
		// collected. Agents walking in step would then all collect again in the same substep, so the
		// distance varies with the id, from half of that quarter to all of it. Collected is what the
		// agent can meet before it collects again: the quarter plus what it covers within the horizon.
		float range = MathF.Max( p.Radius * 16, p.MaxSpeed * Reach ), keep = range * 0.25f * (1 - (i & 15) / 32f);
		if ( a.WallCount < 0
			|| MathF.Abs( position.Z - a.WallCenter.Z ) > p.Height * 0.25f
			|| Vector2.DistanceSquared( Flat( position ), Flat( a.WallCenter ) ) > keep * keep )
		{
			a.WallCount = query.Walls( s, corridors[i][0].Id, position, range * 0.25f + p.MaxSpeed * AvoidHorizon, p.Height, p.Filter, wall, false );
			a.WallCenter = position;
		}
		if ( a.WallCount == 0 ) return desired;

		// Walls facing the agent, relative to it, two points each.
		Span<Vector2> segments = stackalloc Vector2[MaxWalls * 4];
		int count = 0;
		float radiusSquared = p.Radius * p.Radius, shortest = eps * 0.1f;
		for ( int w = 0; w < a.WallCount; w++ )
		{
			Vector3 from = wall[w * 2], to = wall[w * 2 + 1];
			if ( Geo.TriArea2D( position, from, to ) < 0 ) continue;
			Vector2 start = Flat( from - position ), end = Flat( to - position );
			float goalDistance = Geo.DistanceToSegmentSquared( goal, from, to, out float t );
			if ( goalDistance >= radiusSquared )
			{
				segments[count++] = start;
				segments[count++] = end;
				continue;
			}

			// A destination or link entry on a wall must stay reachable: open the wall around it.
			float lengthSquared = Vector2.DistanceSquared( start, end );
			if ( lengthSquared < shortest * shortest ) continue;
			float chord = MathF.Sqrt( (radiusSquared - goalDistance) / lengthSquared );
			if ( t - chord > 0 )
			{
				segments[count++] = start;
				segments[count++] = Vector2.Lerp( start, end, t - chord );
			}
			if ( t + chord < 1 )
			{
				segments[count++] = Vector2.Lerp( start, end, t + chord );
				segments[count++] = end;
			}
		}
		var facing = segments[..count];

		var current = Flat( state.Velocity );
		if ( CollisionTime( desired, facing, horizon ) >= horizon && CollisionTime( current, facing, horizon ) >= horizon ) return desired;

		var direction = desired / speed;
		var result = desired * 0.4f;
		float spread = speed * 0.6f, fastest = speed + 1e-5f * p.MaxSpeed;
		for ( int depth = 0; depth < 4; depth++ )
		{
			float bestPenalty = float.MaxValue;
			Vector2 best = default;
			foreach ( var point in SamplePattern )
			{
				var candidate = result + new Vector2( direction.X * point.X - direction.Y * point.Y, direction.Y * point.X + direction.X * point.Y ) * spread;
				if ( candidate.LengthSquared() > fastest * fastest ) continue;
				float impact = MathF.Min( horizon, CollisionTime( candidate, facing, horizon ) );
				float penalty = 2 * Vector2.Distance( candidate, desired ) / speed + 0.75f * Vector2.Distance( candidate, current ) / speed + 2.5f / (0.1f + impact / horizon);
				if ( penalty < bestPenalty )
				{
					bestPenalty = penalty;
					best = candidate;
				}
			}
			result = best;
			spread *= 0.5f;
		}
		return result;
	}

	// When a point moving at velocity from the origin first crosses one of the walls from its open side.
	float CollisionTime( Vector2 velocity, ReadOnlySpan<Vector2> segments, float horizon )
	{
		float earliest = float.PositiveInfinity, touching = eps * 0.1f, parallel = 1e-6f * areaScale;
		var reach = velocity * horizon;
		for ( int w = 0; w + 1 < segments.Length; w += 2 )
		{
			Vector2 start = segments[w], edge = segments[w + 1] - start;
			if ( velocity.X * edge.Y - velocity.Y * edge.X >= 0 ) continue;

			// Standing on the wall: sliding around one of its ends is allowed, into it is not.
			float lengthSquared = edge.LengthSquared();
			float along = lengthSquared > 0 ? Math.Clamp( -Vector2.Dot( start, edge ) / lengthSquared, 0, 1 ) : 0;
			if ( (start + edge * along).LengthSquared() < touching * touching )
			{
				float heading = Vector2.Dot( velocity, edge );
				if ( (along <= 0 && heading < 0) || (along >= 1 && heading > 0) ) continue;
				return 0;
			}

			float d = reach.X * edge.Y - reach.Y * edge.X;
			if ( MathF.Abs( d ) < parallel ) continue;
			float time = (start.X * edge.Y - start.Y * edge.X) / d;
			float place = (start.X * reach.Y - start.Y * reach.X) / d;
			if ( time >= 0 && time <= 1 && place >= 0 && place <= 1 ) earliest = MathF.Min( earliest, time * horizon );
		}
		return earliest;
	}

	// The origin, then two rings of seven directions, the inner at half the distance and turned half a step.
	static Vector2[] CreatePattern()
	{
		var pattern = new Vector2[15];
		for ( int ring = 0; ring < 2; ring++ )
		{
			for ( int i = 0; i < 7; i++ )
			{
				float angle = (i + ring * 0.5f) * MathF.Tau / 7;
				pattern[1 + ring * 7 + i] = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * (1 - ring * 0.5f);
			}
		}
		return pattern;
	}
}
