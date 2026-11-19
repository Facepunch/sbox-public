namespace Sandbox.Audio;

/// <summary>
/// Direct path kernels: transmission through walls along the straight line, and diffraction paths around them.
/// </summary>
internal static class AcousticOcclusion
{
	public const int DiffractionRays = 10;
	public const int MaxUpdatesPerTick = 18;
	public const int MaxPrimesPerFrame = 8;
	public const int MaxSearchTracesPerTick = 96;
	const int DiffractionInterval = 3;
	const int ExploreInterval = 16;

	const float GoldenAngle = 2.399963f;
	const float OriginOffset = 1f;
	const float OcclusionStepPast = 6f;
	const int MaxOcclusionHits = 3;

	public struct Result
	{
		public FrequencyBands Transmission;
		public float Walls;
		public bool DiffractionUpdated;
		public FrequencyBands Diffraction;
		public int ProbesFound;
		public int ProbesTotal;
	}

	/// <summary>
	/// Scheduling priority: never traced first (nearest first), then by ticks waited scaled down with distance.
	/// </summary>
	public static float Priority( bool neverTraced, int ticksWaited, float dist )
		=> neverTraced ? 1e6f - dist / 512f : ticksWaited / (1f + dist / 512f);

	public const int MaxBends = 4;

	[System.Runtime.CompilerServices.InlineArray( MaxBends )]
	public struct Bends { Vector3 _e; }

	/// <summary>
	/// A diffraction path around the geometry between source and listener, up to <see cref="MaxBends"/> bends ordered from the source.
	/// Kept per source/listener pair, revalidated and pulled taut each update, searched again only when it breaks.
	/// </summary>
	public struct DiffractionPath
	{
		public Bends Points;
		public int Count;
		public float Step;
		public int Move;
		public int Misses;
		public int Failures;
		public Vector3 Source;
		public Vector3 Listener;

		public readonly Vector3 Node( int i, Vector3 source, Vector3 listener ) => i < 0 ? source : i >= Count ? listener : Points[i];
	}

	// Scales the Fresnel number. Maekawa's thin screen is about 20; lower stands in for reflections into the shadow we don't trace.
	const float DetourSoftness = 3f;

	/// <summary>
	/// Per band gain for a path bending around one edge with the given extra length. Softened Maekawa: 10 log10(1 + k 2d/lambda) dB
	/// for 400 Hz, 2.5 kHz and 8 kHz.
	/// </summary>
	public static FrequencyBands DetourGain( float detourMeters )
	{
		static float Band( float d, float lambda ) => 1f / MathF.Sqrt( 1f + MathF.Min( DetourSoftness * 2f * d / lambda, 1000f ) );
		detourMeters = MathF.Max( detourMeters, 0f );
		return new FrequencyBands( Band( detourMeters, 0.86f ), Band( detourMeters, 0.137f ), Band( detourMeters, 0.043f ) );
	}

	static float Detour( Vector3 a, Vector3 v, Vector3 b ) => (a.Distance( v ) + v.Distance( b ) - a.Distance( b )) / 39.37f;

	/// <summary>
	/// Product of the detour gain of every edge the path bends around. Bends within a meter of each other are one edge,
	/// like the two corners of a wall top or a door frame.
	/// </summary>
	static FrequencyBands PathGain( Vector3 source, Vector3 listener, in DiffractionPath path )
	{
		var gain = FrequencyBands.One;
		int first = 0;
		while ( first < path.Count )
		{
			int last = first;
			float through = 0f;
			while ( last + 1 < path.Count && path.Points[last].Distance( path.Points[last + 1] ) < 39.37f )
			{
				through += path.Points[last].Distance( path.Points[last + 1] );
				last++;
			}

			var before = path.Node( first - 1, source, listener );
			var after = path.Node( last + 1, source, listener );
			through += before.Distance( path.Points[first] ) + path.Points[last].Distance( after );
			gain *= DetourGain( (through - before.Distance( after )) / 39.37f );
			first = last + 1;
		}
		return gain;
	}

	/// <summary>
	/// One occlusion update for a source/listener pair. <paramref name="diffractionTick"/> is per source state,
	/// <paramref name="path"/> per source/listener pair, <paramref name="searchBudget"/> is the search traces left this tick, shared by every update.
	/// </summary>
	public static Result Update<T>( ref T tracer, Vector3 source, Vector3 listener, float sourceMfpUnits, float listenerMfpUnits,
		bool diffractionEnabled, ref int diffractionTick, ref DiffractionPath path, ref int searchBudget, Random rng ) where T : IAcousticTracer, allows ref struct
	{
		var r = new Result();
		float dist = Vector3.DistanceBetween( source, listener );

		r.Transmission = ComputeOcclusion( ref tracer, source, listener, out r.Walls, out int directHops );

		if ( diffractionEnabled && directHops > 0 && dist > 8f )
		{
			// A known path is cheap to keep, so it's followed every update. Searching is throttled, counted in selections
			// (not frames) so it applies regardless of how often the source is picked, and backs off while it keeps failing
			// until either end moves.
			if ( path.Count == 0 )
			{
				if ( path.Failures > 0 && (source.DistanceSquared( path.Source ) + listener.DistanceSquared( path.Listener ) > 64f * 64f) ) path.Failures = 0;
				if ( diffractionTick++ % (DiffractionInterval << Math.Min( path.Failures, 2 )) != 0 ) return r;
				// Searches are the expensive part, their traces are capped per tick across all sources. Shared by parallel updates.
				if ( System.Threading.Volatile.Read( ref searchBudget ) <= 0 ) return r;
			}

			// Multi bend paths only improve locally and often wrap the wrong corners; now and then look for a better one.
			bool explore = path.Count >= 2 && diffractionTick++ % ExploreInterval == 0 && System.Threading.Volatile.Read( ref searchBudget ) > 0;

			int diffRays = DiffractionRayCount( dist );
			r.Diffraction = ComputeDiffraction( ref tracer, source, listener, diffRays, sourceMfpUnits, listenerMfpUnits, rng.NextSingle() * MathF.Tau, rng, ref path, explore, ref searchBudget, out r.ProbesFound );
			r.ProbesTotal = diffRays;
			r.DiffractionUpdated = true;
		}
		else
		{
			r.Diffraction = directHops == 0 ? FrequencyBands.One : FrequencyBands.Zero;
			r.DiffractionUpdated = true;
		}

		return r;
	}

	public static int DiffractionRayCount( float dist )
		=> (int)(DiffractionRays * (1f - MathX.Remap( dist, 197f, 3000f, 0f, 1f ).Clamp( 0f, 1f )));

	static Vector3 GetProbe( Vector3 anchor, int i, int count, float phiOffset, float mfpUnits, float dist, Random rng )
	{
		float cosZ = 1f - (2f * i + 1f) / count;
		float sinZ = MathF.Sqrt( 1f - cosZ * cosZ );
		float phi = GoldenAngle * i + phiOffset;
		var dir = new Vector3( sinZ * MathF.Cos( phi ), sinZ * MathF.Sin( phi ), cosZ );
		float maxRadius = MathF.Max( 16f, MathF.Min( mfpUnits, dist * 0.95f ) );
		// Radius independent of direction, otherwise the steepest probe always stays closest.
		float t = rng.NextSingle();
		// Volume-uniform: cbrt biases probes toward the outer shell instead of clustering inside.
		float radius = MathF.Max( 16f, maxRadius * MathF.Cbrt( t ) );
		return anchor + dir * radius;
	}

	static bool Clear<T>( ref T tracer, Vector3 a, Vector3 b, AcousticIgnore ignore ) where T : IAcousticTracer, allows ref struct
		=> !tracer.Trace( a, b, ignore, out _ );

	public static FrequencyBands ComputeOcclusion<T>( ref T tracer, Vector3 source, Vector3 listener, out float avgWalls, out int directHops )
		where T : IAcousticTracer, allows ref struct
	{
		source += Vector3.Up * OriginOffset;
		listener += Vector3.Up * OriginOffset;

		// No endpoint jitter: grazing cases are handled by diffraction, whose gain is continuous across the edge.
		var energy = OcclusionTrace( ref tracer, source, listener, out directHops );
		avgWalls = directHops;

		if ( directHops == 0 ) return FrequencyBands.One;
		return FrequencyBands.Min( energy, FrequencyBands.One );
	}

	static AcousticIgnore SegmentIgnore( int segment, int count ) => segment == 0 ? AcousticIgnore.Source : segment == count ? AcousticIgnore.Listener : AcousticIgnore.None;

	/// <summary>
	/// Index of the first blocked segment (0 leaves the source, Count reaches the listener), -1 when the whole path is clear.
	/// </summary>
	static int BlockedSegment<T>( ref T tracer, Vector3 source, Vector3 listener, in DiffractionPath path ) where T : IAcousticTracer, allows ref struct
	{
		for ( int i = 0; i <= path.Count; i++ )
		{
			if ( !Clear( ref tracer, path.Node( i - 1, source, listener ), path.Node( i, source, listener ), SegmentIgnore( i, path.Count ) ) ) return i;
		}
		return -1;
	}

	/// <summary>
	/// An end moved behind something: bend the broken end segment around it with one more vertex instead of searching from scratch.
	/// </summary>
	static bool Repair<T>( ref T tracer, Vector3 source, Vector3 listener, ref DiffractionPath path, int blocked, float phase ) where T : IAcousticTracer, allows ref struct
	{
		if ( path.Count >= MaxBends || (blocked != 0 && blocked != path.Count) ) return false;

		bool atListener = blocked == path.Count;
		var from = atListener ? path.Points[path.Count - 1] : path.Points[0];
		var to = atListener ? listener : source;
		var toIgnore = atListener ? AcousticIgnore.Listener : AcousticIgnore.Source;
		if ( !tracer.Trace( from, to, toIgnore, out var blocker ) ) return false;

		float detour = float.MaxValue;
		var best = Vector3.Zero;
		int found = 0;
		PartialBuffer unused = default;
		int unusedCount = 0;
		EdgeSearch( ref tracer, from, to, blocker.Position, AcousticIgnore.None, toIgnore, phase, ref detour, ref best, ref found, unused, ref unusedCount );
		if ( found == 0 ) return false;

		if ( atListener )
		{
			path.Points[path.Count++] = best;
		}
		else
		{
			for ( int i = path.Count; i > 0; i-- ) path.Points[i] = path.Points[i - 1];
			path.Points[0] = best;
			path.Count++;
		}

		path.Step = 0.5f;
		path.Misses = 0;
		return true;
	}

	/// <summary>
	/// Move one vertex per update part of the way down the path length gradient, keeping it if both its segments stay clear.
	/// Moves cycle between free, horizontal only and vertical only, so a vertex can slide along a wall top or door frame.
	/// The step halves after a full round of misses and resets when either end moves.
	/// </summary>
	static void Tighten<T>( ref T tracer, Vector3 source, Vector3 listener, ref DiffractionPath path ) where T : IAcousticTracer, allows ref struct
	{
		if ( source.DistanceSquared( path.Source ) + listener.DistanceSquared( path.Listener ) > 32f * 32f )
		{
			path.Step = 0.5f;
			path.Misses = 0;
			path.Source = source;
			path.Listener = listener;
		}

		if ( path.Step < 1f / 16f ) return;

		int i = path.Move % path.Count;
		int move = path.Move / path.Count % 3;
		path.Move++;

		var prev = path.Node( i - 1, source, listener );
		var next = path.Node( i + 1, source, listener );

		// Drop bends that stopped being needed, a repaired path picks some up while an end walks around a corner.
		if ( path.Count > 1 && Clear( ref tracer, prev, next, SegmentIgnore( i, path.Count - 1 ) ) )
		{
			for ( int k = i; k < path.Count - 1; k++ ) path.Points[k] = path.Points[k + 1];
			path.Count--;
			return;
		}

		bool moved = false;
		for ( float scale = 1f; scale >= 0.25f && !moved; scale *= 0.25f )
		{
			var c = Toward( path.Points[i], prev, next, path.Step * scale, move );
			if ( c == path.Points[i] ) break;
			if ( !Clear( ref tracer, prev, c, SegmentIgnore( i, path.Count ) ) || !Clear( ref tracer, c, next, SegmentIgnore( i + 1, path.Count ) ) ) continue;
			path.Points[i] = c;
			moved = true;
		}

		if ( moved )
		{
			path.Misses = 0;
			path.Step = MathF.Min( path.Step * 1.5f, 0.5f );
		}
		else if ( ++path.Misses >= 3 * path.Count )
		{
			path.Misses = 0;
			path.Step *= 0.5f;
		}
	}

	/// <summary>
	/// Step <paramref name="v"/> down the gradient of |a-v| + |v-b|, scaled by how far it is from the line a-b.
	/// </summary>
	static Vector3 Toward( Vector3 v, Vector3 a, Vector3 b, float step, int move )
	{
		var ab = b - a;
		float t = (Vector3.Dot( v - a, ab ) / MathF.Max( ab.LengthSquared, 1e-6f )).Clamp( 0f, 1f );
		float offLine = v.Distance( a + ab * t );
		var delta = -((v - a).Normal + (v - b).Normal);
		if ( move == 1 ) delta.z = 0f;
		else if ( move == 2 ) delta = new Vector3( 0f, 0f, delta.z );
		if ( delta.LengthSquared < 1e-6f ) return v;
		delta = delta.Normal * (offLine * step);
		return delta.LengthSquared < 0.25f ? v : v + delta;
	}

	const int MaxPartials = 8;
	const int MaxPairsPerSide = 3;

	/// <summary>
	/// A point seen from one end that doesn't see the other. Score is how close its line towards the other end got.
	/// </summary>
	struct Partial
	{
		public Vector3 Position;
		public float Score;
	}

	[System.Runtime.CompilerServices.InlineArray( MaxPartials )]
	struct PartialBuffer { Partial _e; }

	/// <summary>
	/// Gain of the best path around the geometry, zero when none is found. Keeps <paramref name="path"/> while it stays clear,
	/// otherwise searches for a point that sees both ends, then a pair that see each other, then chains bends from the source.
	/// </summary>
	public static FrequencyBands ComputeDiffraction<T>( ref T tracer, Vector3 source, Vector3 listener,
		int probeCount, float sourceMfpUnits, float listenerMfpUnits, float phiOffset, Random rng, ref DiffractionPath path, bool explore, ref int searchBudget, out int probesFound )
		where T : IAcousticTracer, allows ref struct
	{
		probesFound = 0;
		source += Vector3.Up * OriginOffset;
		listener += Vector3.Up * OriginOffset;

		float dist = Vector3.DistanceBetween( source, listener );
		if ( dist <= 0f ) return FrequencyBands.One;

		int broken = path.Count > 0 ? BlockedSegment( ref tracer, source, listener, path ) : 0;

		if ( path.Count > 0 && broken < 0 )
		{
			Tighten( ref tracer, source, listener, ref path );
			probesFound = 1;
			var gain = PathGain( source, listener, path );
			if ( !explore ) return gain;

			var budgeted = new BudgetedTracer<T>( tracer, ref searchBudget );
			var fresh = new DiffractionPath();
			var freshGain = Search( ref budgeted, source, listener, probeCount, sourceMfpUnits, listenerMfpUnits, phiOffset, rng, ref fresh, out _ );
			if ( fresh.Count == 0 || freshGain.Mid <= gain.Mid ) return gain;
			path = fresh;
			return freshGain;
		}

		{
			var budgeted = new BudgetedTracer<T>( tracer, ref searchBudget );

			// A broken path is searched for afresh, and only repaired when the search comes up empty.
			if ( path.Count > 0 )
			{
				var previous = path;
				var gain = FrequencyBands.Zero;

				if ( System.Threading.Volatile.Read( ref searchBudget ) > 0 )
				{
					path = default;
					gain = Search( ref budgeted, source, listener, probeCount, sourceMfpUnits, listenerMfpUnits, phiOffset, rng, ref path, out probesFound );
					if ( path.Count > 0 ) return gain;
				}

				// Out of search budget, before or during the search (many paths broke at once, say the listener stepped through
				// a door): mend the old path with real traces, or keep it a tick until a search can run, rather than drop to
				// transmission only.
				if ( System.Threading.Volatile.Read( ref searchBudget ) <= 0 )
				{
					var mended = previous;
					if ( Repair( ref tracer, source, listener, ref mended, broken, phiOffset ) && BlockedSegment( ref tracer, source, listener, mended ) < 0 )
						previous = mended;

					path = previous;
					probesFound = 1;
					return PathGain( source, listener, path );
				}

				// The rest of the old path has to hold too: a trace starting inside a solid doesn't see it.
				if ( !Repair( ref budgeted, source, listener, ref previous, broken, phiOffset ) || BlockedSegment( ref tracer, source, listener, previous ) >= 0 ) return gain;
				path = previous;
				probesFound = 1;
				return PathGain( source, listener, path );
			}

			int failures = path.Failures;
			var found = Search( ref budgeted, source, listener, probeCount, sourceMfpUnits, listenerMfpUnits, phiOffset, rng, ref path, out probesFound );

			// Running out of traces says nothing about the geometry, so it doesn't count toward backing off
			if ( path.Count == 0 && System.Threading.Volatile.Read( ref searchBudget ) <= 0 ) path.Failures = failures;
			return found;
		}
	}

	/// <summary>
	/// Look for a path from scratch: a point that sees both ends, then a pair that see each other, then bends chained from the source.
	/// </summary>
	static FrequencyBands Search<T>( ref T tracer, Vector3 source, Vector3 listener,
		int probeCount, float sourceMfpUnits, float listenerMfpUnits, float phiOffset, Random rng, ref DiffractionPath path, out int probesFound )
		where T : IAcousticTracer, allows ref struct
	{
		probesFound = 0;
		float dist = Vector3.DistanceBetween( source, listener );
		int failures = path.Count == 0 ? path.Failures : 0;
		path = new DiffractionPath { Failures = failures + 1, Source = source - Vector3.Up * OriginOffset, Listener = listener - Vector3.Up * OriginOffset };
		float bestDetour = float.MaxValue;
		var best = Vector3.Zero;

		PartialBuffer sourcePartials = default, listenerPartials = default;
		int sourcePartialCount = 0, listenerPartialCount = 0;

		// Edges first: walk out along the face of the blocking surface, seen from each end.
		for ( int pass = 0; pass < 2; pass++ )
		{
			var anchor = pass == 0 ? source : listener;
			var target = pass == 0 ? listener : source;
			var anchorIgnore = pass == 0 ? AcousticIgnore.Source : AcousticIgnore.Listener;
			var targetIgnore = pass == 0 ? AcousticIgnore.Listener : AcousticIgnore.Source;
			if ( !tracer.Trace( anchor, target, AcousticIgnore.Both, out var blocker ) ) continue;

			if ( pass == 0 ) EdgeSearch( ref tracer, anchor, target, blocker.Position, anchorIgnore, targetIgnore, phiOffset, ref bestDetour, ref best, ref probesFound, sourcePartials, ref sourcePartialCount );
			else EdgeSearch( ref tracer, anchor, target, blocker.Position, anchorIgnore, targetIgnore, phiOffset, ref bestDetour, ref best, ref probesFound, listenerPartials, ref listenerPartialCount );
		}

		if ( probesFound == 0 )
		{
			int halfCount = probeCount / 2;
			for ( int pass = 0; pass < 2; pass++ )
			{
				var anchor = pass == 0 ? source : listener;
				var target = pass == 0 ? listener : source;
				float mfpUnits = pass == 0 ? sourceMfpUnits : listenerMfpUnits;
				var anchorIgnore = pass == 0 ? AcousticIgnore.Source : AcousticIgnore.Listener;
				var targetIgnore = pass == 0 ? AcousticIgnore.Listener : AcousticIgnore.Source;

				for ( int i = 0; i < halfCount; i++ )
				{
					var probePos = GetProbe( anchor, i, halfCount, phiOffset, mfpUnits * 1.25f, dist, rng );

					if ( tracer.Trace( anchor, probePos, anchorIgnore, out var hit ) )
					{
						// Pull back from the obstacle with jitter
						float hitDist = Vector3.DistanceBetween( anchor, hit.Position );
						if ( hitDist < 16f ) continue;
						float clearance = MathX.Lerp( 12f, hitDist * 0.25f, rng.NextSingle() ).Clamp( 8f, hitDist * 0.9f );
						probePos = anchor + (hit.Position - anchor).Normal * (hitDist - clearance);
						if ( !Clear( ref tracer, anchor, probePos, anchorIgnore ) ) continue;
					}

					if ( !tracer.Trace( probePos, target, targetIgnore, out var blocked ) )
					{
						probesFound++;
						float detour = Detour( anchor, probePos, target );
						if ( detour < bestDetour ) { bestDetour = detour; best = probePos; }
					}
					else if ( pass == 0 ) AddPartial( sourcePartials, ref sourcePartialCount, probePos, blocked.Position.Distance( target ) );
					else AddPartial( listenerPartials, ref listenerPartialCount, probePos, blocked.Position.Distance( target ) );
				}
			}
		}

		if ( probesFound > 0 )
		{
			path = new DiffractionPath { Count = 1, Step = 0.5f, Source = source, Listener = listener };
			path.Points[0] = best;
			return PathGain( source, listener, path );
		}

		// Two bends: a point seen by the source and one seen by the listener that see each other. Try the ones reaching furthest across.
		SortByScore( sourcePartials, sourcePartialCount );
		SortByScore( listenerPartials, listenerPartialCount );

		var bestGain = FrequencyBands.Zero;
		for ( int i = 0; i < Math.Min( sourcePartialCount, MaxPairsPerSide ); i++ )
		{
			for ( int j = 0; j < Math.Min( listenerPartialCount, MaxPairsPerSide ); j++ )
			{
				var candidate = new DiffractionPath { Count = 2, Step = 0.5f, Source = source, Listener = listener };
				candidate.Points[0] = sourcePartials[i].Position;
				candidate.Points[1] = listenerPartials[j].Position;
				var gain = PathGain( source, listener, candidate );
				if ( gain.Mid <= bestGain.Mid || !Clear( ref tracer, candidate.Points[0], candidate.Points[1], AcousticIgnore.None ) ) continue;
				bestGain = gain;
				path = candidate;
				probesFound = 1;
			}
		}

		if ( probesFound > 0 || sourcePartialCount == 0 ) return bestGain;

		// More bends: from the source side point that got closest, keep walking along whatever blocks the way to the listener.
		var chain = new DiffractionPath { Count = 1, Step = 0.5f, Source = source, Listener = listener };
		chain.Points[0] = sourcePartials[0].Position;
		float score = sourcePartials[0].Score;

		while ( chain.Count < MaxBends )
		{
			var from = chain.Points[chain.Count - 1];
			if ( !tracer.Trace( from, listener, AcousticIgnore.Listener, out var blocker ) )
			{
				path = chain;
				probesFound = 1;
				return PathGain( source, listener, path );
			}

			float stepDetour = float.MaxValue;
			var stepBest = Vector3.Zero;
			int stepFound = 0;
			PartialBuffer next = default;
			int nextCount = 0;
			EdgeSearch( ref tracer, from, listener, blocker.Position, AcousticIgnore.None, AcousticIgnore.Listener, phiOffset, ref stepDetour, ref stepBest, ref stepFound, next, ref nextCount );

			if ( stepFound > 0 )
			{
				chain.Points[chain.Count++] = stepBest;
				path = chain;
				probesFound = 1;
				return PathGain( source, listener, path );
			}

			SortByScore( next, nextCount );
			if ( nextCount == 0 || next[0].Score >= score ) break;
			score = next[0].Score;
			chain.Points[chain.Count++] = next[0].Position;
		}

		return bestGain;
	}

	/// <summary>
	/// Spends the shared search budget per trace; once it's gone every trace reports blocked, so a search ends with what it has.
	/// </summary>
	ref struct BudgetedTracer<T> : IAcousticTracer where T : IAcousticTracer, allows ref struct
	{
		T _inner;
		readonly ref int _budget;

		public BudgetedTracer( T inner, ref int budget )
		{
			_inner = inner;
			_budget = ref budget;
		}

		public bool Trace( Vector3 from, Vector3 to, AcousticIgnore ignore, out AcousticHit hit )
		{
			if ( System.Threading.Interlocked.Decrement( ref _budget ) < 0 )
			{
				hit = new AcousticHit { Position = from, Normal = Vector3.Up };
				return true;
			}
			return _inner.Trace( from, to, ignore, out hit );
		}
	}

	static void AddPartial( scoped Span<Partial> partials, ref int count, Vector3 position, float score )
	{
		if ( count < partials.Length ) partials[count++] = new Partial { Position = position, Score = score };
	}

	static readonly float[] EdgeRadii = [48f, 128f, 320f];

	/// <summary>
	/// From where the line to the target hits, step outwards along the blocking surface in four directions around the line.
	/// The first point the anchor sees that also sees the target is a bend around that surface's edge (over a wall, past a
	/// door frame). The furthest point of each direction that only the anchor sees is kept as a partial.
	/// </summary>
	static void EdgeSearch<T>( ref T tracer, Vector3 anchor, Vector3 target, Vector3 hit, AcousticIgnore anchorIgnore, AcousticIgnore targetIgnore, float phase,
		ref float bestDetour, ref Vector3 best, ref int found, scoped Span<Partial> partials, ref int partialCount )
		where T : IAcousticTracer, allows ref struct
	{
		var axis = (target - anchor).Normal;
		var u = Vector3.Up - axis * Vector3.Dot( Vector3.Up, axis );
		if ( u.LengthSquared < 0.01f ) u = Vector3.Forward - axis * Vector3.Dot( Vector3.Forward, axis );
		u = u.Normal;
		var v = Vector3.Cross( axis, u );
		var origin = hit - axis * 8f;

		for ( int k = 0; k < 4; k++ )
		{
			float angle = phase + k * MathF.PI * 0.5f;
			var dir = u * MathF.Cos( angle ) + v * MathF.Sin( angle );
			bool hasPartial = false;
			Partial partial = default;

			foreach ( var r in EdgeRadii )
			{
				var p = origin + dir * r;
				if ( !Clear( ref tracer, anchor, p, anchorIgnore ) ) break;

				if ( !tracer.Trace( p, target, targetIgnore, out var blocked ) )
				{
					found++;
					float detour = Detour( anchor, p, target );
					if ( detour < bestDetour ) { bestDetour = detour; best = p; }
					hasPartial = false;
					break;
				}

				float score = blocked.Position.Distance( target );
				if ( !hasPartial || score < partial.Score ) partial = new Partial { Position = p, Score = score };
				hasPartial = true;
			}

			if ( hasPartial ) AddPartial( partials, ref partialCount, partial.Position, partial.Score );
		}
	}

	static void SortByScore( scoped Span<Partial> points, int count )
	{
		for ( int i = 1; i < count; i++ )
		{
			var p = points[i];
			int j = i - 1;
			while ( j >= 0 && points[j].Score > p.Score ) { points[j + 1] = points[j]; j--; }
			points[j + 1] = p;
		}
	}

	static FrequencyBands OcclusionTrace<T>( ref T tracer, Vector3 start, Vector3 end, out int hops ) where T : IAcousticTracer, allows ref struct
	{
		hops = 0;
		var energy = FrequencyBands.One;

		var pos = start;
		var dir = (end - start).Normal;

		// The tracer skips escape bodies, so every hit here is a real surface.
		while ( tracer.Trace( pos, end, AcousticIgnore.Both, out var hit ) )
		{
			if ( ++hops > MaxOcclusionHits ) return FrequencyBands.Zero;
			energy *= AcousticMaterial.GetTransmission( hit.Surface );

			// Step past this surface; the 6u gap keeps a thin double-wall or coincident shapes from counting twice.
			var nextPos = hit.Position + dir * OcclusionStepPast;
			if ( Vector3.Dot( dir, end - nextPos ) <= 0f ) break;
			pos = nextPos;
		}

		return energy;
	}
}
