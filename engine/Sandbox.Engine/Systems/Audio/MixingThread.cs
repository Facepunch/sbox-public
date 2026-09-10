using Sandbox.Utility;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Audio;

/// <summary>
/// Bridges the native audio callback and the managed mixing graph.
/// Main thread calls UpdateGlobals, ApplyWritebacks, QueueSamplerDisposal, QueueAcousticModelDisposal.
/// Mix thread calls MixOneBuffer from the native audio callback.
/// </summary>
static class MixingThread
{
	static readonly Superluminal _sampleVoices = new( "SampleVoices", "#4d5e73" );
	static readonly Superluminal _mixVoices = new( "Mix", "#4d5e73" );
	static readonly Superluminal _finishVoices = new( "FinishVoices", "#4d5e73" );

	// Set to 1 by main after publishing; atomically read-and-cleared by mix thread.
	// int (not volatile) — all accesses go through Interlocked which provides full memory barriers.
	static int _freshSnapFlag;

	// PackageLoader acquires this during hotload to pause mixing.
	static readonly Lock _lockObject = new Lock();
	internal static Lock LockObject => _lockObject;

	// Triple-buffer rotation: main writes → ready hand-off → mix reads.
	// Each slot is always owned by exactly one of: _writableSnap, _readySnap, _activeMixSnap.
	static readonly VoiceFrameSnapshot _snap0 = new();
	static readonly VoiceFrameSnapshot _snap1 = new();
	static readonly VoiceFrameSnapshot _snap2 = new();

	static VoiceFrameSnapshot _writableSnap = _snap2;
	static VoiceFrameSnapshot _readySnap = _snap1;
	static VoiceFrameSnapshot _activeMixSnap = _snap0;

	// _completedSnapshot: decoupled from the rotation so writebacks survive when mix outruns main.
	static VoiceFrameSnapshot _completedSnapshot = _snap0;

	// Disposals are staged on the main thread then flushed after the new snapshot is published,
	// ensuring the mix thread never encounters a disposed object mid-mix.
	static readonly ConcurrentQueue<AudioSampler> _samplerDisposalQueue = new();
	static readonly ConcurrentQueue<DirectSoundModel> _acousticModelDisposalQueue = new();
	static readonly ConcurrentQueue<BinauralEffect> _binauralDisposalQueue = new();
	static readonly ConcurrentQueue<NativeReverbEffect> _reverbDisposalQueue = new();
	static readonly List<AudioSampler> _pendingSamplerDisposals = new();
	static readonly List<DirectSoundModel> _pendingAcousticModelDisposals = new();
	static readonly List<BinauralEffect> _pendingBinauralDisposals = new();
	static readonly List<NativeReverbEffect> _pendingReverbDisposals = new();

	static readonly List<SoundHandle> _buildVoiceList = new();
	static readonly Dictionary<Mixer, int> _voicesPerMixer = new( ReferenceEqualityComparer.Instance );

	static void FlushPendingDisposals()
	{
		foreach ( var s in _pendingSamplerDisposals ) _samplerDisposalQueue.Enqueue( s );
		foreach ( var s in _pendingAcousticModelDisposals ) _acousticModelDisposalQueue.Enqueue( s );
		foreach ( var b in _pendingBinauralDisposals ) _binauralDisposalQueue.Enqueue( b );
		foreach ( var r in _pendingReverbDisposals ) _reverbDisposalQueue.Enqueue( r );
		_pendingSamplerDisposals.Clear();
		_pendingAcousticModelDisposals.Clear();
		_pendingBinauralDisposals.Clear();
		_pendingReverbDisposals.Clear();
	}

	internal static void DrainDisposals()
	{
		FlushPendingDisposals();

		lock ( _lockObject )
		{
			_snap0.Reset();
			_snap1.Reset();
			_snap2.Reset();
			_writableSnap = _snap2;
			_readySnap = _snap1;
			_activeMixSnap = _snap0;
			_freshSnapFlag = 0;

			while ( _samplerDisposalQueue.TryDequeue( out var s ) ) s.Dispose();
			while ( _acousticModelDisposalQueue.TryDequeue( out var s ) ) s.Dispose();
			while ( _binauralDisposalQueue.TryDequeue( out var b ) ) b.Dispose();
			while ( _reverbDisposalQueue.TryDequeue( out var r ) ) r.Dispose();
		}


		SoundHandle.LipSyncAccessor.DrainDestructionQueue();
	}

	internal static void QueueSamplerDisposal( AudioSampler sampler )
	{
		if ( sampler is not null ) _pendingSamplerDisposals.Add( sampler );
	}

	internal static void QueueAcousticModelDisposal( DirectSoundModel source )
	{
		if ( source is not null ) _pendingAcousticModelDisposals.Add( source );
	}

	internal static void QueueBinauralDisposal( BinauralEffect binaural )
	{
		if ( binaural is not null ) _pendingBinauralDisposals.Add( binaural );
	}

	internal static void QueueReverbDisposal( NativeReverbEffect reverb )
	{
		if ( reverb is not null ) _pendingReverbDisposals.Add( reverb );
	}

	internal static void ApplyWritebacks()
	{
		var snapshot = Volatile.Read( ref _completedSnapshot );
		var writebackCount = snapshot.WritebacksReady;
		if ( writebackCount == 0 ) return;

		var voices = CollectionsMarshal.AsSpan( snapshot.Voices );
		for ( var i = 0; i < writebackCount; i++ )
		{
			ref readonly var vs = ref voices[i];
			var handle = vs.Handle;
			if ( handle is null || !handle.IsValid ) continue;

			handle.Amplitude = vs.OutputAmplitude;
			if ( vs.OutputFadeInComplete ) handle.IsFadingIn = false;
			if ( vs.OutputFinished ) handle.Finished = true;
		}
	}

	/// <summary>Apply writebacks, build a fresh snapshot, publish, flush disposals.</summary>
	internal static void UpdateGlobals()
	{
		ApplyWritebacks();
		BuildSnapshot( _writableSnap );
		_writableSnap = Interlocked.Exchange( ref _readySnap, _writableSnap );
		Interlocked.Exchange( ref _freshSnapFlag, 1 );

		FlushPendingDisposals();
	}

	static void BuildSnapshot( VoiceFrameSnapshot snapshot )
	{
		snapshot.Reset();

		Sandbox.SoundSimulationSystem.BeginPrimeFrame();

		foreach ( var listener in Listener.ActiveList )
		{
			listener.MixTransform = listener.Transform;
			snapshot.Listeners.Add( new ListenerState
			{
				Listener = listener,
				MixTransform = listener.MixTransform,
				Scene = listener.Scene
			} );
		}

		snapshot.RemovedListeners.AddRange( Listener.RemovedList );
		Listener.RemovedList.Clear();

		// Snapshot the active set (PreTick may Dispose, which mutates it), then compact in place.
		_buildVoiceList.Clear();
		SoundHandle.CopyActiveUnfiltered( _buildVoiceList );

		var write = 0;
		var span = CollectionsMarshal.AsSpan( _buildVoiceList );
		for ( var i = 0; i < span.Length; i++ )
		{
			var handle = span[i];
			if ( !handle.PreTick() ) continue;
			span[write++] = handle;
		}
		CollectionsMarshal.SetCount( _buildVoiceList, write );

		// Sort newest-first to match Mixer.MixVoices' priority order.
		_buildVoiceList.Sort( static ( a, b ) => b._CreatedTime.CompareTo( a._CreatedTime ) );

		CollectionsMarshal.SetCount( snapshot.Voices, write );
		var voices = CollectionsMarshal.AsSpan( snapshot.Voices );

		_voicesPerMixer.Clear();
		for ( var i = 0; i < write; i++ )
		{
			var handle = _buildVoiceList[i];
			var mixer = handle.GetEffectiveMixer();
			var rank = mixer is null ? int.MaxValue : _voicesPerMixer.GetValueOrDefault( mixer );
			if ( mixer is not null ) _voicesPerMixer[mixer] = rank + 1;

			if ( mixer is not null && rank < mixer.MaxVoices )
			{
				handle.TickForSnapshot( snapshot.RemovedListeners );
				voices[i] = handle.BuildVoiceState( snapshot );
			}
			else
			{
				voices[i] = handle.BuildSampleOnlyVoiceState();
			}
		}

		snapshot.MasterVolume = Sound.MasterVolume;
	}

	static volatile float _avgMixTimeMs;
	/// <summary>Exponentially weighted average mix buffer time in milliseconds. Safe to read from any thread.</summary>
	internal static float AverageMixTimeMs => _avgMixTimeMs;

	internal static void MixOneBuffer()
	{
		var startTs = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			lock ( _lockObject )
			{
				// Reuse the current snapshot until main publishes a new one.
				if ( Interlocked.Exchange( ref _freshSnapFlag, 0 ) != 0 )
				{
					_activeMixSnap.ResetForPool();
					_activeMixSnap = Interlocked.Exchange( ref _readySnap, _activeMixSnap );
				}

				Mix( _activeMixSnap );
			}
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Sound Mixer Exception: {e.Message}" );
		}

		// Update EWMA mix timing.
		var elapsedMs = (float)(System.Diagnostics.Stopwatch.GetElapsedTime( startTs ).TotalMilliseconds);
		_avgMixTimeMs = _avgMixTimeMs * 0.95f + elapsedMs * 0.05f;

		// Drain disposal queues after mixing so objects are valid for the duration of the mix.
		while ( _samplerDisposalQueue.TryDequeue( out var sampler ) ) sampler.Dispose();
		while ( _acousticModelDisposalQueue.TryDequeue( out var source ) ) source.Dispose();
		while ( _binauralDisposalQueue.TryDequeue( out var binaural ) ) binaural.Dispose();
		while ( _reverbDisposalQueue.TryDequeue( out var reverb ) ) reverb.Dispose();
		SoundHandle.LipSyncAccessor.DrainDestructionQueue();
	}

	static readonly MultiChannelBuffer _mixOutput = new( AudioEngine.ChannelCount );

	static void Mix( VoiceFrameSnapshot snapshot )
	{
		SampleVoices( snapshot );

		using ( _mixVoices.Start() )
		{
			Mixer.Master.StartMixing( snapshot );
			Mixer.Master.MixChildren( snapshot );
			Mixer.Master.MixVoices( snapshot );
			Mixer.Master.FinishMixing();
		}

		FinishVoices( snapshot );

		_mixOutput.Silence();
		_mixOutput.MixFrom( Mixer.Master.Output, snapshot.MasterVolume );
		_mixOutput.HardLimit();
		_mixOutput.SendToOutput();
	}

	/// <summary>Decode one buffer of audio from every active sampler (parallelized).</summary>
	static void SampleVoices( VoiceFrameSnapshot snapshot )
	{
		using var _ = _sampleVoices.Start();

		System.Threading.Tasks.Parallel.For( 0, snapshot.Voices.Count, i =>
		{
			var v = snapshot.Voices[i];
			if ( v.Sampler is null ) return;
			v.Handle.SampleWithSeek( v.Sampler, v.Pitch );
		} );
	}

	static void FinishVoices( VoiceFrameSnapshot snapshot )
	{
		using var _ = _finishVoices.Start();

		var voices = CollectionsMarshal.AsSpan( snapshot.Voices );
		for ( var i = 0; i < voices.Length; i++ )
		{
			ref var v = ref voices[i];
			if ( v.Sampler is null ) continue;

			bool samplerDone = !v.Sampler.ShouldContinueMixing;
			if ( samplerDone && v.Handle is not null && !v.Handle.SamplerEnded )
			{
				v.Handle.SamplerEnded = true;
				if ( v.Reverb is not null && v.Reverb.IsValid && v.ReverbRoom.Mix > 0.08f )
				{
					float tail = MathF.Max( v.ReverbRoom.DecayTimeMid, v.ReverbRoom.DecayTimeHigh );
					if ( tail > 0.1f ) v.Handle.ReverbTailUntil = tail;
				}
			}

			if ( v.Handle is not null && !v.Handle.ReverbTailUntil ) continue;

			if ( v.Sampler.ShouldContinueMixing && !v.IsFadingOut ) continue;
			if ( v.FadeOutTimer == false ) continue;

			v.OutputFinished = true;
		}

		// Volatile write acts as a release barrier so Output* writes are visible to the main thread.
		snapshot.WritebacksReady = snapshot.Voices.Count;
		Volatile.Write( ref _completedSnapshot, snapshot );
	}
}

