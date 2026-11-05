using Sandbox.Audio;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class SoundSimulationSystem
{
	[ConVar] internal static bool snd_reverb_enable { get; set; } = true;

	struct RoomWork
	{
		public SoundHandle Handle; // null = listener
		public AcousticRoom.Estimator Estimator;
		public int RayCount;
		public float Priority;
		public AcousticRoom.Sample Sample;
	}

	readonly Dictionary<SoundHandle, AcousticRoom.Estimator> _estimators = new( ReferenceEqualityComparer.Instance );
	readonly List<RoomWork> _roomWork = new();

	internal static float AvgRoomWaitFrames { get; private set; }

	readonly AcousticRoom.Estimator _listenerEst = new();

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

	/// <summary>
	/// A new sound starts from the nearest room already measured, so rapid one shots in one spot share a settled estimate
	/// instead of each guessing from its first couple of rays.
	/// </summary>
	AcousticRoom.Estimator SeedEstimator( Vector3 pos )
	{
		var est = new AcousticRoom.Estimator { LastPos = pos };
		var nearest = _listenerEst.LastUpdateTime >= 0f ? _listenerEst : null;
		float best = nearest is null ? float.MaxValue : pos.DistanceSquared( _listenerEst.LastPos );

		foreach ( var other in _estimators.Values )
		{
			if ( other.LastUpdateTime < 0f ) continue;
			float d = pos.DistanceSquared( other.LastPos );
			if ( d < best ) { best = d; nearest = other; }
		}

		if ( nearest is not null && best <= AcousticRoom.SeedDist * AcousticRoom.SeedDist ) est.SeedFrom( nearest );
		return est;
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

		int pathCount = AcousticRoom.SourceRays;
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

			if ( !_estimators.TryGetValue( handle, out var est ) )
			{
				est = SeedEstimator( handle.Position );
				_estimators[handle] = est;
				if ( est.HasBurst ) handle.SourceRoom = est.Snapshot = AcousticRoom.BuildSnapshot( est, handle.Position.Distance( _listenerEst.LastPos ), 1f - _listenerEst.SmoothedOpenness );
			}

			var pos = handle.Position;
			var distSq = pos.DistanceSquared( listenerPos );
			if ( distSq > AcousticRoom.MaxSourceDist * AcousticRoom.MaxSourceDist ) continue;
			var dist = MathF.Sqrt( distSq );

			if ( pos.DistanceSquared( est.LastPos ) > AcousticRoom.ResetDist * AcousticRoom.ResetDist ) est.HasBurst = false;
			est.LastPos = pos;

			int count = AcousticRoom.RayCount( dist );
			float priority = AcousticRoom.Priority( est.HasBurst, now - est.LastUpdateTime, dist );

			_roomWork.Add( new RoomWork { Handle = handle, Estimator = est, RayCount = count, Priority = priority } );
		}

		_roomWork.Sort( static ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		if ( _roomWork.Count > AcousticRoom.MaxSourcesPerTick ) _roomWork.RemoveRange( AcousticRoom.MaxSourcesPerTick, _roomWork.Count - AcousticRoom.MaxSourcesPerTick );
		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) ) u.Estimator.HasBurst = true;

		int waitTotal = 0, waitCount = 0;
		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) )
		{
			if ( u.Estimator.LastUpdateTick >= 0 ) { waitTotal += _tick - u.Estimator.LastUpdateTick; waitCount++; }
		}
		AvgRoomWaitFrames = MathX.Lerp( AvgRoomWaitFrames, waitCount > 0 ? (float)waitTotal / waitCount : 0f, 0.05f );

		if ( listener is not null )
		{
			if ( listenerPos.DistanceSquared( _listenerEst.LastPos ) > AcousticRoom.ResetDist * AcousticRoom.ResetDist ) _listenerEst.HasBurst = false;
			_listenerEst.LastPos = listenerPos;
			_listenerEst.HasBurst = true;
			_roomWork.Add( new RoomWork { Handle = null, Estimator = _listenerEst, RayCount = pathCount } );
		}

		// Scheduling is final — origin and escape bodies will be filled in parallel.
	}

	void RoomSourceUpdate( int i, PhysicsWorld world )
	{
		ref var u = ref CollectionsMarshal.AsSpan( _roomWork )[i];

		EscapeBodyBuffer escape = default;
		int escapeCount = GatherListenerEscapeBodies( u.Estimator.LastPos, escape );

		var tracer = new PhysicsAcousticTracer( world, u.Handle, ((Span<PhysicsBody>)escape)[..escapeCount] );
		AcousticRoom.TraceRays( ref tracer, u.Estimator.LastPos + Vector3.Up * 4f, u.RayCount, Random.Shared, ref u.Sample );
	}

	void ApplyRoomResults()
	{
		if ( _roomWork.Count == 0 ) return;

		float now = RealTime.Now;
		float listenerEnclosure = 1f - _listenerEst.SmoothedOpenness;

		foreach ( ref var u in CollectionsMarshal.AsSpan( _roomWork ) )
		{
			AcousticRoom.ApplySample( u.Estimator, u.Sample, now );

			if ( u.Handle is null )
			{
				_listenerEst.Snapshot = AcousticRoom.BuildSnapshot( _listenerEst, null, 0f );
				listenerEnclosure = 1f - _listenerEst.SmoothedOpenness;
				continue;
			}

			u.Handle.SourceRoom = AcousticRoom.BuildSnapshot( u.Estimator, u.Estimator.LastPos.Distance( _listenerEst.LastPos ), listenerEnclosure );
			u.Estimator.Snapshot = u.Handle.SourceRoom;
			u.Estimator.LastUpdateTick = _tick;
		}
	}
}
