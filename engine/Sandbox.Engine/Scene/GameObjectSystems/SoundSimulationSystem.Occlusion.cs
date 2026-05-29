using Sandbox.Audio;

namespace Sandbox;

partial class SoundSimulationSystem
{
	[ConVar] internal static bool snd_occlusion_enable { get; set; } = true;
	[ConVar] internal static bool snd_diffraction_enable { get; set; } = true;

	const int OcclusionRays = 3;
	const int DiffractionRays = 10;

	const float GoldenAngle = 2.399963f;
	const float OriginOffset = 1f;
	const int MaxOcclusionHits = 5;
	const int MaxOcclusionsPerFrame = 16;

	[System.Runtime.CompilerServices.InlineArray( MaxOcclusionHits + 1 )]
	private struct TraceResultBuffer { private PhysicsTraceResult _e; }

	readonly ref struct TraceCtx
	{
		public readonly PhysicsWorld World;
		public readonly ReadOnlySpan<PhysicsBody> SourceIgnore;
		public readonly ReadOnlySpan<PhysicsBody> ListenerIgnore;
		public readonly SoundHandle Handle;

		public TraceCtx( PhysicsWorld world, ReadOnlySpan<PhysicsBody> sourceIgnore, ReadOnlySpan<PhysicsBody> listenerIgnore, SoundHandle handle )
		{
			World = world;
			SourceIgnore = sourceIgnore;
			ListenerIgnore = listenerIgnore;
			Handle = handle;
		}
	}

	static (Vector3 ProbePos, Vector3 Dir) GetProbe( Vector3 anchor, int i, int count, float phiOffset, float mfpUnits, float dist, bool hemisphere, float distFraction = 0.95f )
	{
		float cosZ = hemisphere ? (i + 0.5f) / count : 1f - (2f * i + 1f) / count;
		float sinZ = MathF.Sqrt( 1f - cosZ * cosZ );
		float phi = GoldenAngle * i + phiOffset;
		var dir = new Vector3( sinZ * MathF.Cos( phi ), sinZ * MathF.Sin( phi ), cosZ );
		float maxRadius = MathF.Max( 16f, MathF.Min( mfpUnits, dist * distFraction ) );
		float t = ((i + 0.5f) / count + (Random.Shared.NextSingle() - 0.5f) / count).Clamp( 0f, 1f );
		// Volume-uniform: cbrt biases probes toward the outer shell instead of clustering inside.
		float radius = MathF.Max( 16f, maxRadius * MathF.Cbrt( t ) );
		return (anchor + dir * radius, dir);
	}

	struct OccPendingUpdate
	{
		public SoundHandle Handle;
		public Audio.DirectSoundModel Source;
		public Vector3 SoundPosition;
		public Vector3 ListenerPosition;
		public float SourceRoomMfpUnits;
		public float ListenerRoomMfpUnits;
		public float Priority;
		public EscapeBodyBuffer SourceEscapeBodies;
		public int SourceEscapeCount;
		public EscapeBodyBuffer ListenerEscapeBodies;
		public int ListenerEscapeCount;
		public FrequencyBands Transmission;
		public FrequencyBands Diffraction;
		public bool DiffractionUpdated;
		public float Walls;
		public int DiffractionProbesFound;
		public int DiffractionProbesTotal;
	}

	readonly List<OccPendingUpdate> _occPendingUpdates = new();
	readonly List<(EscapeBodyBuffer Buf, int Count)> _listenerEscape = new();

	internal static float AvgOccWaitFrames { get; private set; }

	void GatherOcclusionWork( PhysicsWorld world )
	{
		var occEnabled = snd_simulation_enable && snd_occlusion_enable;
		DirectSoundModel.GlobalOcclusionEnabled = occEnabled;
		_occPendingUpdates.Clear();

		if ( !occEnabled || _sceneListeners.Count == 0 ) return;

		_listenerEscape.Clear();
		for ( int li = 0; li < _sceneListeners.Count; li++ )
		{
			EscapeBodyBuffer buf = default;
			int count = GatherListenerEscapeBodies( _sceneListeners[li].Position, buf );
			_listenerEscape.Add( (buf, count) );
		}

		foreach ( var handle in _culledHandles )
		{
			if ( !handle.Occlusion ) continue;

			var soundPos = handle.Transform.Position;

			for ( int li = 0; li < _sceneListeners.Count; li++ )
			{
				var listener = _sceneListeners[li];
				var source = handle.GetDirectSoundModel( listener );

				if ( source is null ) continue;

				var distSq = Vector3.DistanceBetweenSquared( soundPos, listener.Position );
				var dist = MathF.Sqrt( distSq );
				var neverTraced = !source.HasFirstTrace;
				float priority = neverTraced ? 1e6f - dist / 512f : (_tick - handle.OcclusionPhase) / (1f + dist / 512f);

				_occPendingUpdates.Add( new OccPendingUpdate
				{
					Handle = handle,
					Source = source,
					SoundPosition = soundPos,
					ListenerPosition = listener.Position,
					SourceRoomMfpUnits = MathF.Max( handle.SourceRoom.MfpMeters * 39.37f, 128f ),
					ListenerRoomMfpUnits = MathF.Max( ListenerRoom.MfpMeters * 39.37f, 128f ),
					Priority = priority,
					ListenerEscapeBodies = _listenerEscape[li].Buf,
					ListenerEscapeCount = _listenerEscape[li].Count,
				} );
			}
		}

		_occPendingUpdates.Sort( static ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		if ( _occPendingUpdates.Count > MaxOcclusionsPerFrame ) _occPendingUpdates.RemoveRange( MaxOcclusionsPerFrame, _occPendingUpdates.Count - MaxOcclusionsPerFrame );

		int waitTotal = 0, waitCount = 0;
		foreach ( ref var u in System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _occPendingUpdates ) )
		{
			if ( u.Handle.OcclusionPhase >= 0 ) { waitTotal += _tick - u.Handle.OcclusionPhase; waitCount++; }
		}
		AvgOccWaitFrames = MathX.Lerp( AvgOccWaitFrames, waitCount > 0 ? (float)waitTotal / waitCount : 0f, 0.05f );
	}

	void OcclusionUpdate( int i, PhysicsWorld world )
	{
		ref var u = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _occPendingUpdates )[i];

		u.SourceEscapeCount = GatherSourceEscapeBodies( u.SoundPosition, u.SourceEscapeBodies );

		float dist = Vector3.DistanceBetween( u.SoundPosition, u.ListenerPosition );

		var ctx = new TraceCtx( world,
			((Span<PhysicsBody>)u.SourceEscapeBodies)[..u.SourceEscapeCount],
			((Span<PhysicsBody>)u.ListenerEscapeBodies)[..u.ListenerEscapeCount],
			u.Handle );

		int occRays = OcclusionRays;
		occRays = (int)(occRays * (1f - (dist / 3937f).Clamp( 0f, 1f )));

		u.Transmission = ComputeOcclusion( u.SoundPosition, u.ListenerPosition, occRays, u.SourceRoomMfpUnits,
			Random.Shared.NextSingle() * MathF.Tau, ctx, out u.Walls, out int directHops );

		if ( snd_simulation_enable && snd_diffraction_enable && directHops > 0 && dist > 8f )
		{
			// Run diffraction on every other occlusion update for this source.
			// Counted in selections (not frames) so throttling actually halves the work
			// regardless of how often the source is picked.
			if ( (u.Handle.DiffractionTick++ & 1) != 0 ) return;

			int diffRays = DiffractionRays;
			diffRays = (int)(diffRays * (1f - MathX.Remap( dist, 197f, 3000f, 0f, 1f ).Clamp( 0f, 1f )));

			u.Diffraction = ComputeDiffraction( u.SoundPosition, u.ListenerPosition, diffRays,
				u.SourceRoomMfpUnits, u.ListenerRoomMfpUnits, Random.Shared.NextSingle() * MathF.Tau, ctx, out u.DiffractionProbesFound );
			u.DiffractionProbesTotal = diffRays;
			u.DiffractionUpdated = true;
		}
		else if ( directHops == 0 )
		{
			u.Diffraction = FrequencyBands.One;
			u.DiffractionUpdated = true;
		}
		else
		{
			u.Diffraction = FrequencyBands.Zero;
			u.DiffractionUpdated = true;
		}
	}

	void ApplyOcclusionResults()
	{
		foreach ( ref var u in System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _occPendingUpdates ) )
		{
			u.Source.SetTargetTransmission( u.Transmission, u.Walls );
			if ( u.DiffractionUpdated )
				u.Source.SetTargetDiffraction( u.Diffraction, u.DiffractionProbesFound, u.DiffractionProbesTotal );
			u.Handle.OcclusionPhase = _tick;
		}
	}

	static bool LosBlocked( Vector3 from, Vector3 to, in TraceCtx ctx, ReadOnlySpan<PhysicsBody> ignoreNear )
		=> LosBlocked( from, to, ctx, ignoreNear, out _ );

	static bool LosBlocked( Vector3 from, Vector3 to, in TraceCtx ctx, ReadOnlySpan<PhysicsBody> ignoreNear, out Vector3 firstHit )
	{
		firstHit = to;
		TraceResultBuffer hitBuf = default;
		int hitCount = ApplySimulationTags( ctx.World.Trace.FromTo( from, to ), ctx.Handle ).RunAll( hitBuf );
		float closestFrac = float.MaxValue;
		bool blocked = false;
		for ( int i = 0; i < hitCount; i++ )
		{
			ref var hit = ref ((Span<PhysicsTraceResult>)hitBuf)[i];
			if ( hit.HasTag( "world" ) || !IgnoredBody( hit.Body, ignoreNear ) )
			{
				if ( hit.Fraction < closestFrac )
				{
					closestFrac = hit.Fraction;
					firstHit = hit.HitPosition;
				}
				blocked = true;
			}
		}
		return blocked;
	}

	static FrequencyBands ComputeOcclusion(
		Vector3 source, Vector3 listener,
		int probeCount, float sourceMfpUnits, float phiOffset,
		in TraceCtx ctx, out float avgWalls, out int directHops )
	{
		source += Vector3.Up * OriginOffset;
		listener += Vector3.Up * OriginOffset;

		var directDir = (listener - source).Normal;
		var directTx = OcclusionTrace( source - directDir * 0.05f, listener, ctx, out directHops );
		avgWalls = directHops;

		if ( directHops == 0 ) return FrequencyBands.One;

		var accum = directTx;
		float totalWeight = 1f;
		float wallAccum = directHops;
		float dist = Vector3.DistanceBetween( source, listener );

		for ( int i = 0; i < probeCount; i++ )
		{
			var (probePos, dir) = GetProbe( source, i, probeCount, phiOffset, sourceMfpUnits, dist, hemisphere: true, distFraction: 0.25f );

			if ( LosBlocked( source, probePos, ctx, ctx.SourceIgnore ) ) continue;

			var probeTx = OcclusionTrace( listener, probePos, ctx, out int probeWalls );
			float cosAngle = Vector3.Dot( dir, directDir ).Clamp( -1f, 1f );
			float w = MathX.Remap( cosAngle, 0f, 1f, 0.3f, 1.0f ) * (probeWalls == 0 ? 1.5f : 1f);

			accum += probeTx * w;
			wallAccum += probeWalls * w;
			totalWeight += w;
		}

		avgWalls = wallAccum / totalWeight;
		return FrequencyBands.Min( accum / totalWeight, FrequencyBands.One );
	}

	static FrequencyBands ComputeDiffraction(
		Vector3 source, Vector3 listener,
		int probeCount, float sourceMfpUnits, float listenerMfpUnits, float phiOffset,
		in TraceCtx ctx, out int probesFound )
	{
		probesFound = 0;
		source += Vector3.Up * OriginOffset;
		listener += Vector3.Up * OriginOffset;

		float dist = Vector3.DistanceBetween( source, listener );
		if ( dist <= 0f ) return FrequencyBands.One;

		var directDir = (listener - source).Normal;
		int halfCount = probeCount / 2;
		var accum = FrequencyBands.Zero;
		float totalWeight = 0f;

		for ( int pass = 0; pass < 2; pass++ )
		{
			var anchor = pass == 0 ? source : listener;
			var target = pass == 0 ? listener : source;
			float mfpUnits = pass == 0 ? sourceMfpUnits : listenerMfpUnits;
			float dirMul = pass == 0 ? 1f : -1f;
			var anchorIgnore = pass == 0 ? ctx.SourceIgnore : ctx.ListenerIgnore;
			var targetIgnore = pass == 0 ? ctx.ListenerIgnore : ctx.SourceIgnore;

			for ( int i = 0; i < halfCount; i++ )
			{
				var (probePos, dir) = GetProbe( anchor, i, halfCount, phiOffset, mfpUnits * 1.25f, dist, hemisphere: false );

				if ( LosBlocked( anchor, probePos, ctx, anchorIgnore, out var hitPos ) )
				{
					// Pull back from the obstacle with jitter;
					float hitDist = Vector3.DistanceBetween( anchor, hitPos );
					if ( hitDist < 16f ) continue;
					float clearance = MathX.Lerp( 12f, hitDist * 0.25f, Random.Shared.NextSingle() ).Clamp( 8f, hitDist * 0.9f );
					probePos = anchor + (hitPos - anchor).Normal * (hitDist - clearance);
					if ( LosBlocked( anchor, probePos, ctx, anchorIgnore ) ) continue;
				}

				if ( LosBlocked( target, probePos, ctx, targetIgnore ) ) continue;

				float cosAngle = Vector3.Dot( dir, directDir * dirMul ).Clamp( -1f, 1f );
				float detour = (Vector3.DistanceBetween( anchor, probePos ) + Vector3.DistanceBetween( target, probePos ) - dist) / dist;
				// Knife-edge HF rolloff: extra path length around the obstacle drives high-freq loss.
				float hfLoss = (detour * 1.5f).Clamp( 0f, 1f );

				var probeTx = new FrequencyBands( 1f, 1f - hfLoss * 0.1f, 1f - hfLoss * 0.2f );
				float w = MathX.Remap( cosAngle, -1f, 1f, 0.3f, 1.0f ) / (1f + detour);
				accum += probeTx * w;
				totalWeight += w;
				probesFound++;
			}
		}

		if ( totalWeight <= 0f ) return FrequencyBands.Zero;

		// Bias toward less muffling when any path exists: add a phantom "pass-through" sample.
		const float PhantomWeight = 3.25f;
		accum += FrequencyBands.One * PhantomWeight;
		totalWeight += PhantomWeight;

		return FrequencyBands.Min( accum / totalWeight, FrequencyBands.One );
	}

	static FrequencyBands OcclusionTrace( Vector3 start, Vector3 end, in TraceCtx ctx, out int hops )
	{
		hops = 0;
		var energy = FrequencyBands.One;

		var pos = start;
		var dir = (end - start).Normal;

		while ( true )
		{
			var tr = ApplySimulationTags( ctx.World.Trace.FromTo( pos, end ), ctx.Handle ).Run();
			if ( !tr.Hit ) break;

			if ( !tr.HasTag( "world" ) && (IgnoredBody( tr.Body, ctx.SourceIgnore ) || IgnoredBody( tr.Body, ctx.ListenerIgnore )) )
			{
				var next = tr.HitPosition + dir * 4f;
				if ( Vector3.Dot( dir, end - next ) <= 0f ) break;
				pos = next;
				continue;
			}

			if ( ++hops > MaxOcclusionHits ) return FrequencyBands.Zero;
			energy *= AcousticMaterial.GetTransmission( tr.Surface?.AudioSurface ?? AudioSurface.Generic );

			var nextPos = tr.HitPosition + dir * 4f;
			if ( Vector3.Dot( dir, end - nextPos ) <= 0f ) break;
			pos = nextPos;
		}

		return energy;
	}
}
