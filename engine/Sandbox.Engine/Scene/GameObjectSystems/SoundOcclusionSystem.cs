namespace Sandbox;

/// <summary>
/// Updates sound occlusion in parallel during StartFixedUpdate.
/// This moves the expensive physics traces out of the main sound tick loop
/// and parallelizes them across all sounds rather than per-sound.
/// </summary>
internal sealed class SoundOcclusionSystem : GameObjectSystem
{
	/// <summary>
	/// Cached data for parallel occlusion updates.
	/// We cache listener positions on the main thread because Listener.Position
	/// has a main thread assertion and cannot be accessed from worker threads.
	/// </summary>
	record struct PendingOcclusionUpdate( SoundHandle Handle, Audio.SteamAudioSource Source, Vector3 ListenerPosition );

	// Static lists to avoid allocations each frame
	static readonly List<SoundHandle> _tempHandles = new();
	static readonly List<PendingOcclusionUpdate> _pendingUpdates = new();
	static readonly List<Audio.Listener> _sceneListeners = new();
	static readonly Dictionary<Audio.Mixer, int> _voiceCountByMixer = new();

	public SoundOcclusionSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, -50, UpdateSoundOcclusion, "SoundOcclusion" );
	}

	void UpdateSoundOcclusion()
	{
		using var _ = PerformanceStats.Timings.Audio.Scope();

		var world = Scene.PhysicsWorld;
		if ( !world.IsValid() ) return;

		// Get listener positions for this scene - filter directly from ActiveList
		_sceneListeners.Clear();
		var scene = Scene;

		foreach ( var listener in Audio.Listener.ActiveList )
		{
			if ( listener.Scene == scene )
			{
				_sceneListeners.Add( listener );
			}
		}

		if ( _sceneListeners.Count == 0 ) return;

		// Collect all pending updates on the main thread.
		// We must cache listener positions here because Listener.Position 
		// requires main thread access (ThreadSafe.AssertIsMainThread).
		_pendingUpdates.Clear();
		GetPendingUpdates( _sceneListeners );

		if ( _pendingUpdates.Count == 0 ) return;

		// Process all updates in parallel - each does its own sequential ray traces
		Sandbox.Utility.Parallel.ForEach( _pendingUpdates, update =>
		{
			var handle = update.Handle;
			var targetMixer = handle.TargetMixer ?? Audio.Mixer.Default;
			var position = handle.Transform.Position;
			var occlusionSize = handle.OcclusionRadius;
			var listenerPos = update.ListenerPosition;

			var occlusion = ComputeOcclusion( position, listenerPos, occlusionSize, targetMixer, world );

			update.Source.SetTargetOcclusion( occlusion );

			// Schedule next update based on distance - close sounds update more often
			// Distance threshold: 8192 units (~208 meters)
			// Close sounds: ~7 Hz, Far sounds: ~0.33 Hz
			var distance = Vector3.DistanceBetween( position, listenerPos ).Remap( 0, 8192, 1, 0 ).Clamp( 0, 1 );
			update.Source.TimeUntilNextOcclusionCalc = distance.Remap( 3.0f, 0.15f ) * Random.Shared.Float( 0.9f, 1.1f );
		} );
	}

	/// <summary>
	/// Compute how occluded a sound is. Returns 0 if fully occluded, 1 if not occluded.
	/// </summary>
	static float ComputeOcclusion( Vector3 position, Vector3 listener, float occlusionSize, Audio.Mixer targetMixer, PhysicsWorld world )
	{
		var distance = Vector3.DistanceBetween( position, listener ).Remap( 0, 4096, 1, 0 );

		int iRays = (occlusionSize.Remap( 0, 64, 1, 32 ) * distance).CeilToInt().Clamp( 1, 32 );
		int iHits = 0;
		var tags = targetMixer.GetOcclusionTags();

		// tags are defined, but are empty, means hit nothing - so 0% occluded
		// if it is null, then we just use the "world" tag
		if ( tags is not null && tags.Count == 0 ) return 1.0f;

		for ( int i = 0; i < iRays; i++ )
		{
			var startPos = position + Vector3.Random * occlusionSize * 0.5f;

			var tq = world.Trace.FromTo( startPos, listener );

			if ( tags is null )
			{
				tq = tq.WithTag( "world" );
			}
			else
			{
				tq = tq.WithAnyTags( tags );
			}

			var tr = tq.Run();

			if ( tr.Hit )
			{
				iHits++;
			}
		}

		return 1 - (iHits / (float)iRays);
	}

	/// <summary>
	/// Collect all handle/listener pairs that need occlusion updates this frame.
	/// Must be called on main thread to access Listener.Position.
	/// </summary>
	void GetPendingUpdates( List<Audio.Listener> listeners )
	{
		_tempHandles.Clear();
		SoundHandle.GetActive( _tempHandles );

		// Sort by creation time descending (newest first) to match mixer priority
		_tempHandles.Sort( ( a, b ) => b._CreatedTime.CompareTo( a._CreatedTime ) );

		// Track voice count per mixer to respect MaxVoices limits
		_voiceCountByMixer.Clear();

		foreach ( var handle in _tempHandles )
		{
			if ( handle.Scene != Scene ) continue;
			if ( !handle.Occlusion ) continue;
			if ( handle.ListenLocal ) continue;

			// Use shared CanBeMixed check (same as Mixer.ShouldPlay)
			if ( !handle.CanBeMixed() ) continue;

			var mixer = handle.GetEffectiveMixer();
			if ( mixer is null ) continue;

			// Check if we've exceeded MaxVoices for this mixer
			_voiceCountByMixer.TryGetValue( mixer, out var count );
			if ( count >= mixer.MaxVoices ) continue;
			_voiceCountByMixer[mixer] = count + 1;

			foreach ( var listener in listeners )
			{
				var source = handle.GetSource( listener );
				if ( source is null ) continue;

				if ( source.TimeUntilNextOcclusionCalc <= 0 )
				{
					_pendingUpdates.Add( new PendingOcclusionUpdate( handle, source, listener.Position ) );
				}
			}
		}
	}
}
