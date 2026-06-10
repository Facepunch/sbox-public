using Sandbox.Audio;

namespace Sandbox;

partial class SoundSimulationSystem
{
	[ConVar] internal static bool snd_occlusion_enable { get; set; } = true;
	[ConVar] internal static bool snd_diffraction_enable { get; set; } = true;

	const int DiffractionRays = 10;

	const float GoldenAngle = 2.399963f;
	const float OriginOffset = 1f;
	const float OcclusionJitter = 64f;
	const float OcclusionJitterMaxDist = 1378f; // ~35m; beyond this the jitter is noise vs. cost.
	const float OcclusionStepPast = 6f;
	const int MaxOcclusionHits = 3;
	const int MaxOcclusionsPerFrame = 16;

	readonly ref struct TraceCtx
	{
		// Prebuilt once per update with sim tags + escape filter applied; per trace we only set endpoints.
		public readonly PhysicsTraceBuilder Trace;
		public readonly ReadOnlySpan<PhysicsBody> SourceIgnore;
		public readonly ReadOnlySpan<PhysicsBody> ListenerIgnore;

		public TraceCtx( in PhysicsTraceBuilder trace, ReadOnlySpan<PhysicsBody> sourceIgnore, ReadOnlySpan<PhysicsBody> listenerIgnore )
		{
			Trace = trace;
			SourceIgnore = sourceIgnore;
			ListenerIgnore = listenerIgnore;
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
		public int ListenerIndex;
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
			if ( !handle.OcclusionEnabled ) continue;
			if ( handle.TargetMixer is { } mixer && mixer.Occlusion <= 0f ) continue;

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
					ListenerIndex = li,
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

		EscapeBodyBuffer sourceEscape = default;
		int sourceEscapeCount = GatherSourceEscapeBodies( u.SoundPosition, sourceEscape );
		ref var listenerEntry = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _listenerEscape )[u.ListenerIndex];

		float dist = Vector3.DistanceBetween( u.SoundPosition, u.ListenerPosition );

		// Sim tags + escape filter resolved once for this update.
		var trace = ApplySimulationTags( world.Trace, u.Handle );
		trace.filterCallback = EscapeFilter;

		var ctx = new TraceCtx( trace,
			((Span<PhysicsBody>)sourceEscape)[..sourceEscapeCount],
			((Span<PhysicsBody>)listenerEntry.Buf)[..listenerEntry.Count] );

		u.Transmission = ComputeOcclusion( u.SoundPosition, u.ListenerPosition,
			Random.Shared.NextSingle() * MathF.Tau, ctx, out u.Walls, out int directHops );

		if ( snd_simulation_enable && snd_diffraction_enable && directHops > 0 && dist > 8f )
		{
			// Run diffraction every 3rd occlusion update for this source.
			// Counted in selections (not frames) so throttling actually applies
			// regardless of how often the source is picked.
			if ( u.Handle.DiffractionTick++ % 3 != 0 ) return;

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
		// Filter skips escape bodies, so a single Run() returns the closest real blocker.
		SetTraceIgnore( ignoreNear );
		try
		{
			var hit = ctx.Trace.FromTo( from, to ).Run();
			firstHit = hit.Hit ? hit.HitPosition : to;
			return hit.Hit;
		}
		finally
		{
			ClearTraceIgnore();
		}
	}

	static FrequencyBands ComputeOcclusion(
		Vector3 source, Vector3 listener,
		float phiOffset,
		in TraceCtx ctx, out float avgWalls, out int directHops )
	{
		source += Vector3.Up * OriginOffset;
		listener += Vector3.Up * OriginOffset;

		// Jitter the source endpoint so grazing/edge cases average out over successive updates.
		// Fall back to the un-jittered point if the jitter lands in solid. Skip both at long
		// distance where the offset has negligible effect on the trace path.
		var sourceJitter = source;
		if ( Vector3.DistanceBetweenSquared( source, listener ) < OcclusionJitterMaxDist * OcclusionJitterMaxDist )
		{
			var jitterDir = new Vector3( MathF.Cos( phiOffset ), MathF.Sin( phiOffset ), 0f );
			sourceJitter = source + jitterDir * OcclusionJitter;
			if ( LosBlocked( source, sourceJitter, ctx, ctx.SourceIgnore ) ) sourceJitter = source;
		}

		var energy = OcclusionTrace( sourceJitter, listener, ctx, out directHops );
		avgWalls = directHops;

		if ( directHops == 0 ) return FrequencyBands.One;
		return FrequencyBands.Min( energy, FrequencyBands.One );
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

		// Filter skips escape bodies, so every hit here is a real surface.
		SetTraceIgnore( ctx.SourceIgnore, ctx.ListenerIgnore );
		try
		{
			while ( true )
			{
				var tr = ctx.Trace.FromTo( pos, end ).Run();
				if ( !tr.Hit ) break;

				if ( ++hops > MaxOcclusionHits ) return FrequencyBands.Zero;
				energy *= AcousticMaterial.GetTransmission( tr.Surface?.AudioSurface ?? AudioSurface.Generic );

				// Step past this surface; the 6u gap keeps a thin double-wall or coincident shapes from counting twice.
				var nextPos = tr.HitPosition + dir * OcclusionStepPast;
				if ( Vector3.Dot( dir, end - nextPos ) <= 0f ) break;
				pos = nextPos;
			}
		}
		finally
		{
			ClearTraceIgnore();
		}

		return energy;
	}
}
