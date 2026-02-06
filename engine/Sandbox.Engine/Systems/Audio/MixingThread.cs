using Sandbox.Utility;
using System.Threading;

namespace Sandbox.Audio;

/// <summary>
/// This is a real thread! We need to be very careful about what this accesses, and how.
/// </summary>
static class MixingThread
{
	static readonly Superluminal _sampleVoices = new Superluminal( "SampleVoices", "#4d5e73" );
	static readonly Superluminal _mixVoices = new Superluminal( "Mix", "#4d5e73" );
	static readonly Superluminal _finishVoices = new Superluminal( "FinishVoices", "#4d5e73" );

	static readonly Lock _lockObject = new Lock();

	class MixingSettings
	{
		public List<Listener> Listeners;
		public float MasterVolume = 0.1f;
	}

	static MixingSettings _settings = new MixingSettings();

	internal static Lock LockObject => _lockObject;

	internal static void UpdateGlobals()
	{
		var settings = new MixingSettings
		{
			Listeners = new(),
			MasterVolume = Sound.MasterVolume
		};

		Listener.GetActive( settings.Listeners );

		Interlocked.Exchange( ref _settings, settings );
	}

	internal static void MixOneBuffer()
	{
		try
		{
			using ( PerformanceStats.Timings.AudioMixingThread.Scope() )
			{
				lock ( LockObject )
				{
					Mix();
				}
			}
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Sound Mixer Exception: {e.Message}" );
		}
	}

	private static readonly List<SoundHandle> _voiceCollectList = [];
	private static readonly List<Listener> _removedListeners = [];

	private static void Mix()
	{
		_voiceCollectList.Clear();
		SoundHandle.GetActive( _voiceCollectList );
		Mix( _voiceCollectList );
	}

	static float Mix( List<SoundHandle> voices )
	{
		var settings = Volatile.Read( ref _settings );

		_removedListeners.Clear();

		while ( Listener.RemoveQueue.TryDequeue( out var id ) )
		{
			_removedListeners.Add( id );
		}

		SampleVoices( voices );

		//
		// At the moment we're running each mixer then copying their contents to output.
		// What we will have in the future is a sort of dependancy list, because a mixer
		// might want to send its output to another mixer. For now this is fine.
		//

		lock ( Mixer.Master.Lock )
		{
			using ( _mixVoices.Start() )
			{
				// MIX CHILDREN

				Mixer.Master.StartMixing( settings.Listeners, _removedListeners );
				Mixer.Master.MixChildren( voices );
				Mixer.Master.MixVoices( voices );
				Mixer.Master.FinishMixing();
			}
		}

		FinishVoices( voices );

		// output
		{
			using MultiChannelBuffer buffer = new( AudioEngine.ChannelCount );
			buffer.Silence();

			buffer.MixFrom( Mixer.Master.Output, settings.MasterVolume );

			buffer.SendToOutput();
		}

		return 512 * AudioEngine.SecondsPerSample;
	}

	/// <summary>
	/// Read one sample from each voice
	/// </summary>
	static void SampleVoices( List<SoundHandle> voices )
	{
		using var scope = _sampleVoices.Start( $"{voices.Count()}" );

		System.Threading.Tasks.Parallel.ForEach( voices, voice =>
		{
			if ( !voice.IsValid ) return;
			if ( voice.sampler is null ) return;
			if ( voice.Finished ) return;

			voice.sampler.Sample( voice.Pitch );
		} );
	}

	/// <summary>
	/// Mark any finished voices as finished
	/// </summary>
	static void FinishVoices( List<SoundHandle> voices )
	{
		using var scope = _finishVoices.Start();

		System.Threading.Tasks.Parallel.ForEach( voices, voice =>
		{
			if ( voice.sampler is null ) return;
			if ( voice.Finished ) return;
			if ( voice.sampler.ShouldContinueMixing && !voice.IsFadingOut ) return;
			if ( voice.TimeUntilFaded == false ) return;

			voice.Finished = true;
		} );
	}


}
