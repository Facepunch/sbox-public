using Sandbox.Audio;

namespace Sandbox.Platform;

partial class VoiceChat
{
	internal static float DefaultDistance => 15_000f;

	internal static Curve DefaultFalloff { get; } = new(
		new( 0, 1, 0, -1.8f ),
		new( 0.05f, 0.22f, 3.5f, -3.5f ),
		new( 0.2f, 0.04f, 0.16f, -0.16f ),
		new( 1, 0 ) );

	internal static Transform DefaultTransform { get; } = new( Vector3.Forward * 10.0f );

	/// <summary>
	/// Plays back one person's voice. Made when they start talking, disposed once they've been quiet for a while.
	/// </summary>
	internal sealed class Speaker( bool isLoopback ) : IDisposable
	{
		/// <summary>
		/// 60ms of silence, queued ahead of voice data when playback has run dry.
		/// </summary>
		private static readonly short[] JitterSilence = new short[Sound.VoiceSampleRate * 60 / 1000];

		private SoundStream _soundStream;

		private RealTimeSince _lastHeard = float.MaxValue;

		public SoundHandle SoundHandle { get; private set; }

		public bool IsTalking => Amplitude > 0f;
		public float Amplitude => SoundHandle.IsValid() ? SoundHandle.Amplitude : 0f;

		/// <summary>
		/// We haven't heard from them for a while, so this can be disposed.
		/// </summary>
		public bool IsIdle => _lastHeard > 1.0f;

		public bool WriteVoiceData( ReadOnlySpan<byte> data )
		{
			if ( !_soundStream.IsValid() )
			{
				_soundStream = new SoundStream( Sound.VoiceSampleRate );
			}

			if ( !SoundHandle.IsValid() )
			{
				SoundHandle = _soundStream.Play();

				if ( SoundHandle is null ) return false;

				SoundHandle.IsVoice = true;
				SoundHandle.Loopback = isLoopback;
				SoundHandle.ListenLocal = true;

				// Voice Volume is applied by this mixer, a Voice component can pick one of its children

				SoundHandle.TargetMixer = Mixer.Voice;

				SoundHandle.Distance = DefaultDistance;
				SoundHandle.Falloff = DefaultFalloff;
				SoundHandle.Transform = DefaultTransform;
			}

			// Packets turn up in bursts, so when we've run dry (first packet, or after a pause)
			// queue a bit of silence first. Otherwise the start of what they say plays in choppy bits.

			if ( _soundStream.QueuedSampleCount == 0 )
			{
				_soundStream.WriteData( JitterSilence );
			}

			if ( _soundStream.WriteVoiceData( data ) <= 0 ) return false;

			_lastHeard = 0;
			return true;
		}

		public void Dispose()
		{
			// Use Finished instead of Dispose() so the audio thread can safely wrap up
			// before TickInternal disposes it - avoids concurrent CBinauralEffect release/create.

			if ( SoundHandle.IsValid() )
			{
				SoundHandle.Finished = true;
			}

			SoundHandle = null;

			_soundStream?.Dispose();
			_soundStream = null;
		}
	}
}
