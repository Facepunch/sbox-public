using ExCSS;
using NativeEngine;
using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Enables music playback. Use this for music, not for playing game sounds.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
	/// <summary>
	/// We only use the audio component of the video player.
	/// </summary>
	VideoPlayer native;

	/// <summary>
	/// Sample rate of the audio being played.
	/// </summary>
	public int SampleRate { get; private set; }

	/// <summary>
	/// Number of channels of the audio being played.
	/// </summary>
	public int Channels { get; private set; }

	/// <summary>
	/// Gets the total duration of the video in seconds.
	/// </summary>
	public float Duration => native.Duration;

	/// <summary>
	/// Gets the current playback time in seconds.
	/// </summary>
	public float PlaybackTime => native.PlaybackTime;

	/// <summary>
	/// Invoked when the audio has finished playing.
	/// </summary>
	public Action OnFinished { get; set; }

	/// <summary>
	/// Invoked when the audio has repeated.
	/// </summary>
	public Action OnRepeated { get; set; }

	/// <summary>
	/// Place the listener at 0,0,0 facing 1,0,0.
	/// </summary>
	public bool ListenLocal
	{
		get => native.Audio.ListenLocal;
		set => native.Audio.ListenLocal = value;
	}

	/// <summary>
	/// Position of the sound.
	/// </summary>
	public Vector3 Position
	{
		get => native.Audio.Position;
		set => native.Audio.Position = value;
	}

	/// <summary>
	/// Pause playback of audio.
	/// </summary>
	public bool Paused
	{
		get => native.IsPaused;
		set
		{
			if ( value )
			{
				native.Pause();
			}
			else
			{
				native.Resume();
			}
		}
	}

	/// <summary>
	/// Audio will repeat when reaching the end.
	/// </summary>
	public bool Repeat
	{
		get => native.Repeat;
		set => native.Repeat = value;
	}

	/// <summary>
	/// Change the volume of this music.
	/// </summary>
	public float Volume
	{
		get => native.Audio.Volume;
		set => native.Audio.Volume = value;
	}

	/// <summary>
	/// Enables lipsync processing.
	/// </summary>
	public bool LipSync
	{
		get => native.Audio.LipSync;
		set => native.Audio.LipSync = value;
	}

	/// <summary>
	/// Which mixer do we want to write to
	/// </summary>
	public Mixer TargetMixer
	{
		get => native.Audio.TargetMixer;
		set => native.Audio.TargetMixer = value;
	}

	/// <inheritdoc cref="SoundHandle.Distance"/>
	public float Distance
	{
		get => native.Audio.Distance;
		set => native.Audio.Distance = value;
	}

	/// <inheritdoc cref="SoundHandle.Falloff"/>
	public Curve Falloff
	{
		get => native.Audio.Falloff;
		set => native.Audio.Falloff = value;
	}

	/// <summary>
	/// A list of 15 lipsync viseme weights. Requires <see cref="LipSync"/> to be enabled.
	/// </summary>
	public IReadOnlyList<float> Visemes => native.Audio.Visemes;

	/// <summary>
	/// Get title of the track.
	/// </summary>
	public string Title
	{
		get
		{
			var title = GetMeta( "title" );
			return string.IsNullOrWhiteSpace( title ) ? GetMeta( "StreamTitle" ) : title;
		}
	}

	/// <summary>
	/// 512 FFT magnitudes used for audio visualization.
	/// </summary>
	public ReadOnlySpan<float> Spectrum => GetSpectrum();

	/// <summary>
	/// Approximate measure of audio loudness.
	/// </summary>
	public float Amplitude => native.Audio.GetAmplitude();

	private unsafe ReadOnlySpan<float> GetSpectrum()
	{
		return native.Audio.GetSpectrum();
	}

	internal MusicPlayer()
	{
		native = new VideoPlayer();
		native.OnRepeated += OnRepeatInternal;
		native.OnFinished += OnFinishedInternal;

		// MusicPlayer used to have different defaults than VideoPlayer, need to make sure they are still set
		native.Audio.ListenLocal = false;
		native.Audio.Position = Vector3.Zero;
		native.Audio.Volume = 1.0f;
		native.Audio.LipSync = false;
	}

	~MusicPlayer()
	{
		MainThread.QueueDispose( this );
	}

	public void Dispose()
	{
		if ( native is null )
			return;

		native.OnRepeated -= OnRepeatInternal;
		native.OnFinished -= OnFinishedInternal;
		native.Dispose();
		native = null;

		GC.SuppressFinalize( this );
	}

	/// <summary>
	/// Plays a music stream from a URL.
	/// </summary>
	public static MusicPlayer PlayUrl( string url )
	{
		var player = new MusicPlayer();
		player.native.Play( url );
		return player;
	}

	/// <summary>
	/// Plays a music file from a relative path.
	/// </summary>
	public static MusicPlayer Play( BaseFileSystem filesystem, string path )
	{
		var player = new MusicPlayer();
		player.native.Play( filesystem, path );
		return player;
	}

	/// <summary>
	/// Stops audio playback.
	/// </summary>
	public void Stop()
	{
		native.Stop();
	}

	/// <summary>
	/// Sets the playback position to a specified time in the audio, given in seconds.
	/// </summary>
	public void Seek( float time )
	{
		native.Seek( time );
	}

	/// <summary>
	/// Get meta data string.
	/// </summary>
	internal string GetMeta( string key )
	{
		return native.GetMeta( key );
	}

	internal void OnFinishedInternal()
	{
		MainThread.Queue( () =>
		{
			OnFinished?.Invoke();
		} );
	}

	internal void OnRepeatInternal()
	{
		MainThread.Queue( () =>
		{
			OnRepeated?.Invoke();
		} );
	}
}
