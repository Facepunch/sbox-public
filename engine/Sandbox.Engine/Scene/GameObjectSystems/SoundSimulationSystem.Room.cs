using Sandbox.Audio;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class SoundSimulationSystem
{
	[ConVar] internal static bool snd_reverb_enable { get; set; } = true;

	const int SourceRays = 1;
	const int SourceRayBounces = 5;
	const float RoomMaxBounceLen = 8192f;
	const float RoomMaxSourceDist = 4096f;
	const float UnitsPerMeter = 39.37f;
	const float AccumHalfLife = 0.6f;
	const float ResetDist = 256f;
	const int MaxRoomSourcesPerFrame = 10;

	struct RoomWork
	{
		public SoundHandle Handle; // null = listener
		public SourceEstimator Estimator;
		public int RayCount;
		public float Priority;
		public Vector3 Origin;
		public float EscapedWeight;
		public float TotalDist;
		public FrequencyBands TotalRefl;
		public int BounceCount;
		public int SegmentCount;
	}

	sealed class SourceEstimator
	{
		public bool HasBurst;
		public Vector3 LastPos;
		public float LastUpdateTime = -1f;
		public int LastUpdateTick = -1;

		public float SmoothedOpenness = 1f;
		public float SmoothedDecayDist;
		public FrequencyBands SmoothedMaterialTone = FrequencyBands.One;

		public ReverbSnapshot Snapshot;
	}

	readonly Dictionary<SoundHandle, SourceEstimator> _estimators = new( ReferenceEqualityComparer.Instance );
	readonly List<RoomWork> _roomWork = new();

	internal static float AvgRoomWaitFrames { get; private set; }

	readonly SourceEstimator _listenerEst = new();

	internal float ListenerOpenness => _listenerEst.SmoothedOpenness;
	internal ReverbSnapshot ListenerRoom => _listenerEst.Snapshot;

	readonly List<SoundHandle> _deadEstimatorKeys = new();

	void PurgeDeadEstimators()
	{
		_deadEstimatorKeys.Clear();
		foreach ( var key in _estimators.Keys )
		{
			if ( !key.IsValid || key.Scene != Scene ) _deadEstimatorKeys.Add( key );
		}
		foreach ( var key in _deadEstimatorKeys ) _estimators.Remove( key );
	}

	void GatherRoomWork( PhysicsWorld world )
	{
		PurgeDeadEstimators();

		_roomWork.Clear();

		if ( !snd_reverb_enable || !snd_simulation_enable || DspVolumeGameSystem.IsActive )
		{
			foreach ( var key in _estimators.Keys ) key.SourceRoom = default;
			return;
		}

		int pathCount = SourceRays;
		float now = RealTime.Now;

		var listener = _sceneListeners.Count > 0 ? _sceneListeners[0] : null;
		var listenerPos = listener?.Position ?? Vector3.Zero;

		foreach ( var handle in _culledHandles )
		{
			if ( !handle.ReverbEnabled || (handle.TargetMixer is { } mixer && mixer.Reverb <= 0f) )
			{
				handle.SourceRoom = default;
				continue;
			}

			ref var est = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault( _estimators, handle, out _ );
			est ??= new SourceEstimator();

			var pos = handle.Position;
			var distSq = pos.DistanceSquared( listenerPos );
			if ( distSq > RoomMaxSourceDist * RoomMaxSourceDist ) continue;
			var dist = MathF.Sqrt( distSq );

			if ( pos.DistanceSquared( est.LastPos ) > ResetDist * ResetDist ) est.HasBurst = false;
			est.LastPos = pos;

			float distT = dist.Remap( 512f, RoomMaxSourceDist, 0f, 1f ).Clamp( 0f, 1f );
			int count = Math.Max( 2, (int)(pathCount * (1f - distT)) );
			float priority = !est.HasBurst ? 1e6f - dist / 512f : (now - est.LastUpdateTime) / (1f + dist / 512f);

			_roomWork.Add( new RoomWork { Handle = handle, Estimator = est, RayCount = count, Priority = priority } );
		}

		_roomWork.Sort( static ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		if ( _roomWork.Count > MaxRoomSourcesPerFrame ) _roomWork.RemoveRange( MaxRoomSourcesPerFrame, _roomWork.Count - MaxRoomSourcesPerFrame );
		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) ) u.Estimator.HasBurst = true;

		int waitTotal = 0, waitCount = 0;
		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) )
		{
			if ( u.Estimator.LastUpdateTick >= 0 ) { waitTotal += _tick - u.Estimator.LastUpdateTick; waitCount++; }
		}
		AvgRoomWaitFrames = MathX.Lerp( AvgRoomWaitFrames, waitCount > 0 ? (float)waitTotal / waitCount : 0f, 0.05f );

		if ( listener is not null )
		{
			if ( listenerPos.DistanceSquared( _listenerEst.LastPos ) > ResetDist * ResetDist ) _listenerEst.HasBurst = false;
			_listenerEst.LastPos = listenerPos;
			_listenerEst.HasBurst = true;
			_roomWork.Add( new RoomWork { Handle = null, Estimator = _listenerEst, RayCount = pathCount } );
		}

		// Scheduling is final — origin and escape bodies will be filled in parallel.
	}

	void RoomSourceUpdate( int i, PhysicsWorld world )
	{
		ref var u = ref CollectionsMarshal.AsSpan( _roomWork )[i];

		u.Origin = u.Estimator.LastPos + Vector3.Up * 4f;
		EscapeBodyBuffer escape = default;
		int escapeCount = GatherListenerEscapeBodies( u.Estimator.LastPos, escape );

		var escapeIgnore = ((Span<PhysicsBody>)escape)[..escapeCount];

		// Sim tags + escape filter resolved once for this source; the escape set is constant too.
		var trace = ApplySimulationTags( world.Trace, u.Handle );
		trace.filterCallback = EscapeFilter;
		SetTraceIgnore( escapeIgnore );

		try
		{
			for ( int r = 0; r < u.RayCount; r++ )
			{
				var pos = u.Origin;
				var dir = Vector3.Random.Normal;
				int rayBounces = 0;

				for ( int b = 0; b < SourceRayBounces; b++ )
				{
					var tr = trace.FromTo( pos, pos + dir * RoomMaxBounceLen ).Run();
					if ( !tr.Hit )
					{
						u.EscapedWeight += EscapeWeight( rayBounces );
						break;
					}

					u.TotalDist += pos.Distance( tr.HitPosition );
					u.SegmentCount++;
					u.TotalRefl += AcousticMaterial.GetReflectivity( tr.Surface?.AudioSurface ?? AudioSurface.Generic ).Log();
					u.BounceCount++;
					rayBounces++;

					dir = (tr.Normal + Vector3.Random.Normal).Normal;
					pos = tr.HitPosition + tr.Normal * 4f;
				}
			}
		}
		finally
		{
			ClearTraceIgnore();
		}
	}

	void ApplyRoomResults()
	{
		if ( _roomWork.Count == 0 ) return;

		float now = RealTime.Now;
		float listenerEnclosure = 1f - _listenerEst.SmoothedOpenness;

		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) )
		{
			float sampleOpenness = u.EscapedWeight / u.RayCount;
			float sampleDecayDist = u.SegmentCount > 0 ? u.TotalDist / u.SegmentCount : 0f;
			var sampleTone = u.BounceCount > 0 ? (u.TotalRefl / u.BounceCount).Exp() : FrequencyBands.One;

			ApplyEstimatorSample( u.Estimator, sampleOpenness, sampleDecayDist, sampleTone, now );

			var tone = u.Estimator.SmoothedMaterialTone;
			float toneLow = MathF.Max( tone.Low, 0.001f );
			float toneMid = MathF.Max( tone.Mid, 0.001f );
			float toneHigh = MathF.Max( tone.High, 0.001f );
			float absLow = -MathF.Log( toneLow );
			float absMid = -MathF.Log( toneMid );
			float absHigh = -MathF.Log( toneHigh );
			float absEff = -MathF.Log( MathF.Cbrt( toneLow * toneMid * toneHigh ) );
			float mfpMeters = u.Estimator.SmoothedDecayDist / UnitsPerMeter;
			float decayTime = absEff > 1e-6f ? 0.040f * mfpMeters / absEff : 0f;
			float decayLow = mfpMeters > 0f && absLow > 0f ? 0.040f * mfpMeters / absLow : 0f;
			float decayMid = mfpMeters > 0f && absMid > 0f ? 0.040f * mfpMeters / absMid : 0f;
			float decayHigh = mfpMeters > 0f ? 0.040f * mfpMeters / (absHigh + 0.006f * mfpMeters) : 0f;

			if ( u.Handle is null )
			{
				_listenerEst.Snapshot = new ReverbSnapshot
				{
					DecayTime = decayTime,
					MfpMeters = mfpMeters,
					Openness = _listenerEst.SmoothedOpenness,
					MaterialTone = tone,
					DecayTimeLow = decayLow,
					DecayTimeMid = decayMid,
					DecayTimeHigh = decayHigh,
				};
				listenerEnclosure = 1f - _listenerEst.SmoothedOpenness;
				continue;
			}

			float Dc = mfpMeters * MathF.Sqrt( mfpMeters / MathF.Max( 90f * decayTime, 0.001f ) );
			float distMeters = u.Estimator.LastPos.Distance( _listenerEst.LastPos ) / UnitsPerMeter;
			float wetFraction = distMeters / MathF.Sqrt( Dc * Dc + distMeters * distMeters );

			u.Handle.SourceRoom = new ReverbSnapshot
			{
				DecayTime = decayTime,
				MfpMeters = mfpMeters,
				Openness = u.Estimator.SmoothedOpenness,
				MaterialTone = tone,
				Mix = (1f - u.Estimator.SmoothedOpenness) * wetFraction * listenerEnclosure,
				DecayTimeLow = decayLow,
				DecayTimeMid = decayMid,
				DecayTimeHigh = decayHigh,
			};
			u.Estimator.Snapshot = u.Handle.SourceRoom;
			u.Estimator.LastUpdateTick = _tick;
		}
	}

	void ApplyEstimatorSample( SourceEstimator est, float openness, float decayDist, FrequencyBands tone, float now )
	{
		float dt = est.LastUpdateTime < 0f ? 0f : now - est.LastUpdateTime;
		est.LastUpdateTime = now;

		if ( dt <= 0f )
		{
			est.SmoothedOpenness = openness;
			est.SmoothedDecayDist = decayDist;
			est.SmoothedMaterialTone = tone;
		}
		else
		{
			est.SmoothedOpenness = MathX.ExponentialDecay( est.SmoothedOpenness, openness, AccumHalfLife, dt );
			est.SmoothedDecayDist = MathX.ExponentialDecay( est.SmoothedDecayDist, decayDist, AccumHalfLife, dt );
			est.SmoothedMaterialTone = est.SmoothedMaterialTone.Decay( tone, AccumHalfLife, dt );
		}
	}

	static float EscapeWeight( int bounces ) => bounces <= 1 ? 1f : MathF.Pow( 0.4f, bounces - 1 );
}
