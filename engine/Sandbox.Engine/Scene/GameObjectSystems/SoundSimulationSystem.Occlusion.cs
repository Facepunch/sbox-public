using Sandbox.Audio;

namespace Sandbox;

partial class SoundSimulationSystem
{
	[ConVar] internal static bool snd_occlusion_enable { get; set; } = true;
	[ConVar] internal static bool snd_diffraction_enable { get; set; } = true;

	// Synchronous occlusion traces per snapshot build, so new sounds start occluded not full volume.
	static readonly FrequencyBands HalfOccluded = FrequencyBands.One * 0.5f;
	static int _primesRemaining;

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
		public AcousticOcclusion.Result Result;
	}

	readonly List<OccPendingUpdate> _occPendingUpdates = new();
	int _searchBudget;
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
				float priority = AcousticOcclusion.Priority( neverTraced, _tick - handle.OcclusionPhase, dist );

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

		_searchBudget = AcousticOcclusion.MaxSearchTracesPerTick;
		_occPendingUpdates.Sort( static ( a, b ) => b.Priority.CompareTo( a.Priority ) );
		if ( _occPendingUpdates.Count > AcousticOcclusion.MaxUpdatesPerTick ) _occPendingUpdates.RemoveRange( AcousticOcclusion.MaxUpdatesPerTick, _occPendingUpdates.Count - AcousticOcclusion.MaxUpdatesPerTick );

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

		var tracer = new PhysicsAcousticTracer( world, u.Handle,
			((Span<PhysicsBody>)sourceEscape)[..sourceEscapeCount],
			((Span<PhysicsBody>)listenerEntry.Buf)[..listenerEntry.Count] );

		u.Result = AcousticOcclusion.Update( ref tracer, u.SoundPosition, u.ListenerPosition, u.SourceRoomMfpUnits, u.ListenerRoomMfpUnits,
			snd_simulation_enable && snd_diffraction_enable, ref u.Handle.DiffractionTick, ref u.Source.DiffractionPath, ref _searchBudget, Random.Shared );
	}

	void ApplyOcclusionResults()
	{
		foreach ( ref var u in System.Runtime.InteropServices.CollectionsMarshal.AsSpan( _occPendingUpdates ) )
		{
			u.Source.SetTargetTransmission( u.Result.Transmission, u.Result.Walls );
			if ( u.Result.DiffractionUpdated )
				u.Source.SetTargetDiffraction( u.Result.Diffraction, u.Result.ProbesFound, u.Result.ProbesTotal );
			u.Handle.OcclusionPhase = _tick;
		}
	}

	// Reset the prime budget once per snapshot build.
	internal static void BeginPrimeFrame() => _primesRemaining = AcousticOcclusion.MaxPrimesPerFrame;

	// Seed a new model's occlusion before its first mix so it doesn't start at full volume.
	internal void PrimeNewModel( SoundHandle handle, Audio.Listener listener, Audio.DirectSoundModel model )
	{
		if ( !snd_simulation_enable || !snd_occlusion_enable ) return;
		if ( !handle.OcclusionEnabled ) return;
		if ( handle.TargetMixer is { } mixer && mixer.Occlusion <= 0f ) return;
		if ( !Scene.HasPhysicsWorld ) return;

		if ( _primesRemaining <= 0 )
		{
			model.SetInitialOcclusion( HalfOccluded ); // over budget: guess, left untraced for the sim
			return;
		}
		_primesRemaining--;

		var world = Scene.PhysicsWorld;
		var soundPos = handle.Transform.Position;

		EscapeBodyBuffer sourceEscape = default;
		int sourceEscapeCount = GatherSourceEscapeBodies( soundPos, sourceEscape );
		EscapeBodyBuffer listenerEscape = default;
		int listenerEscapeCount = GatherListenerEscapeBodies( listener.Position, listenerEscape );

		var tracer = new PhysicsAcousticTracer( world, handle,
			((Span<PhysicsBody>)sourceEscape)[..sourceEscapeCount],
			((Span<PhysicsBody>)listenerEscape)[..listenerEscapeCount] );

		var tx = AcousticOcclusion.ComputeOcclusion( ref tracer, soundPos, listener.Position, out _, out _ );
		// Mark traced so the sim re-traces it in turn rather than fast-tracking it.
		model.SetInitialOcclusion( tx, markTraced: true );
		handle.OcclusionPhase = _tick;
	}
}
