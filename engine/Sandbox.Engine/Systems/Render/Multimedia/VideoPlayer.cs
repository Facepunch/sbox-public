using NativeEngine;
using Sandbox.Audio;

namespace Sandbox;

/// <summary>
/// Enables video playback and access to the video texture and audio.
/// </summary>
public sealed class VideoPlayer : IDisposable, IWeakInteropHandle
{
	internal CVideoPlayer native;

	/// <summary>
	/// Video successfully loaded.
	/// </summary>
	public Action OnLoaded { get; set; }

	/// <summary>
	/// Event that is invoked when the audio stream is created and ready to use.
	/// </summary>
	public Action OnAudioReady { get; set; }

	/// <summary>
	/// Video finished playing.
	/// </summary>
	public Action OnFinished { get; set; }

	/// <summary>
	/// Video started playing again after looping.
	/// </summary>
	public Action OnRepeated { get; set; }

	public delegate void TextureChangedDelegate( ReadOnlySpan<byte> span, Vector2 size );

	/// <summary>
	/// If this event is set, texture data will be provided instead of rendering to the texture.
	/// </summary>
	public TextureChangedDelegate OnTextureData { get; set; }

	/// <summary>
	/// Sets whether the video should loop when it reaches the end.
	/// </summary>
	public bool Repeat
	{
		get => native.GetRepeat();
		set => native.SetRepeat( value );
	}

	/// <summary>
	/// Gets the total duration of the video in seconds.
	/// </summary>
	public float Duration => native.GetDuration();

	/// <summary>
	/// Gets the current playback time in seconds.
	/// </summary>
	public float PlaybackTime => native.GetPlaybackTime();

	/// <summary>
	/// Audio sample rate.
	/// </summary>
	public int SampleRate { get; private set; }

	/// <summary>
	/// Number of audio channels.
	/// </summary>
	public int Channels { get; private set; }

	/// <summary>
	/// Does the loaded video have audio?
	/// </summary>
	public bool HasAudio => native.HasAudioStream();

	/// <summary>
	/// Has the video been paused?
	/// </summary>
	public bool IsPaused => native.IsPaused();

	/// <summary>
	/// Texture of the video frame.
	/// </summary>
	public Texture Texture { get; private set; }

	/// <summary>
	/// Width of the video.
	/// </summary>
	public int Width => native.GetWidth();

	/// <summary>
	/// Height of the video.
	/// </summary>
	public int Height => native.GetHeight();

	uint IWeakInteropHandle.InteropHandle { get; set; }

	private SoundHandle Sound;

	/// <summary>
	/// Access audio properties for this video playback.
	/// </summary>
	public AudioAccessor Audio { get; internal init; }

	public class AudioAccessor
	{
		private readonly VideoPlayer Player;
		private SoundHandle Sound => Player.Sound;

		private bool listenLocal = true;
		private Vector3 position = Vector3.Forward * 64.0f;
		private float volume = 1.0f;
		private bool lipSync = false;
		private Mixer targetMixer;

		internal AudioAccessor( VideoPlayer player )
		{
			Player = player;
		}

		/// <summary>
		/// Place the listener at 0,0,0 facing 1,0,0.
		/// </summary>
		public bool ListenLocal
		{
			get => listenLocal;
			set
			{
				listenLocal = value;
				if ( Sound.IsValid() && Sound.IsPlaying )
					Sound.ListenLocal = listenLocal;
			}
		}

		/// <summary>
		/// Position of the sound.
		/// </summary>
		public Vector3 Position
		{
			get => position;
			set
			{
				position = value;
				if ( Sound.IsValid() && Sound.IsPlaying )
					Sound.Position = position;
			}
		}

		/// <summary>
		/// Which mixer do we want to write to
		/// </summary>
		public Mixer TargetMixer
		{
			get => targetMixer;
			set
			{
				if ( value == targetMixer )
					return;

				targetMixer = value;
				if ( Sound.IsValid() )
					Sound.TargetMixer = targetMixer;
			}
		}

		/// <summary>
		/// Volume of the sound.
		/// </summary>
		public float Volume
		{
			get => volume;
			set
			{
				if ( value == volume )
					return;

				volume = value;
				if ( Sound.IsValid() )
					Sound.Volume = volume;
			}
		}

		/// <summary>
		/// Enables lipsync processing.
		/// </summary>
		public bool LipSync
		{
			get => lipSync;
			set
			{
				if ( value == lipSync )
					return;

				lipSync = value;
				if ( Sound.IsValid() )
					Sound.LipSync.Enabled = lipSync;
			}
		}

		private float distance = 15_000f;

		/// <inheritdoc cref="SoundHandle.Distance"/>
		public float Distance
		{
			get => distance;
			set
			{
				if ( value == distance )
					return;

				distance = value;
				if ( Sound.IsValid() )
					Sound.Distance = distance;
			}
		}

		private Curve falloff = new( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

		/// <inheritdoc cref="SoundHandle.Falloff"/>
		public Curve Falloff
		{
			get => falloff;
			set
			{
				falloff = value;
				if ( Sound.IsValid() )
					Sound.Falloff = falloff;
			}
		}

		/// <summary>
		/// A list of 15 lipsync viseme weights. Requires <see cref="LipSync"/> to be enabled.
		/// </summary>
		public IReadOnlyList<float> Visemes => Sound.IsValid() ? Sound.LipSync.Visemes : Array.Empty<float>();

		internal ReadOnlySpan<float> GetSpectrum()
		{
			CUtlVectorFloat spectrumVector = CUtlVectorFloat.Create( 0, 0 );
			Player.native.GetSpectrum( spectrumVector );

			var spectrum = new float[spectrumVector.Count()];

			for ( var i = 0; i < spectrum.Length; ++i ) spectrum[i] = spectrumVector.Element( i );

			spectrumVector.DeleteThis();

			return spectrum;
		}

		internal float GetAmplitude()
		{
			return Player.native.GetAmplitude();
		}
	}

	/// <summary>
	/// Get meta data string.
	/// </summary>
	internal string GetMeta( string key )
	{
		return native.GetMetadata( key );
	}

	public VideoPlayer()
	{
		InteropSystem.AllocWeak( this );
		native = CVideoPlayer.Create( this );

		Texture = Texture.Create( 1, 1 )
			.WithName( "video-placeholder" )
			.WithData( new byte[4] { 0, 0, 0, 0 } )
			.Finish();

		Texture.IsLoaded = false;
		Texture.ParentObject = this;

		Audio = new AudioAccessor( this );
	}

	~VideoPlayer()
	{
		MainThread.QueueDispose( this );
	}

	internal void OnInitAudioInternal( int sampleRate, int channels )
	{
		SampleRate = sampleRate;
		Channels = channels;

		MainThread.Queue( OnAudioReadyInternal );
	}

	internal void OnFreeAudioInternal()
	{
		MainThread.Queue( FreeAudio );
	}

	private void FreeAudio()
	{
		Sound?.Dispose();
		Sound = null;
	}

	internal void OnAudioReadyInternal()
	{
		FreeAudio();

		var stream = native.GetAudioStream();
		if ( stream is not null )
		{
			Sound = stream.Play();
			if ( Sound is not null )
			{
				Sound.Position = Audio.Position;
				Sound.ListenLocal = Audio.ListenLocal;
				Sound.TargetMixer = Audio.TargetMixer;
				Sound.Volume = Audio.Volume;
				Sound.Distance = Audio.Distance;
				Sound.Falloff = Audio.Falloff;
				Sound.LipSync.Enabled = Audio.LipSync;
			}
		}

		OnAudioReady?.Invoke();
	}

	internal void OnFinishedInternal()
	{
		MainThread.Queue( () =>
		{
			OnFinished?.Invoke();
		} );
	}

	internal void OnRepeatedInternal()
	{
		MainThread.Queue( () =>
		{
			OnRepeated?.Invoke();
		} );
	}

	internal void OnTextureCreatedInternal()
	{
		Texture.CopyFrom( Texture.FromNative( native.GetTexture() ) );
		Texture.IsLoaded = true;
	}

	internal bool WantsTextureData() => OnTextureData != null;

	internal unsafe void OnTextureDataInternal( IntPtr data, int width, int height )
	{
		var size = new Vector2( width, height );
		var dataSpan = new ReadOnlySpan<byte>( data.ToPointer(), width * height * 4 );

		OnTextureData?.Invoke( dataSpan, size );
	}

	internal void OnLoadedInternal()
	{
		MainThread.Queue( () =>
		{
			OnLoaded?.Invoke();
		} );
	}

	public void Dispose()
	{
		FreeAudio();

		Texture.ParentObject = null;

		if ( native.IsValid )
		{
			native.Destroy();
			native = IntPtr.Zero;
		}

		InteropSystem.FreeWeak( this );
		GC.SuppressFinalize( this );
	}


	/// <summary>
	/// Plays a video file from a URL. If there's already a video playing, it will stop.
	/// </summary>
	public void Play( string url )
	{
		if ( string.IsNullOrWhiteSpace( url ) )
			return;

		url = url.Trim();

		if ( !Uri.TryCreate( url, UriKind.Absolute, out var uri ) ) return;

		if ( uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps )
		{
			Log.Warning( $"Url Scheme not allowed [{uri.Scheme}]" );
			return;
		}

		if ( !Http.IsAllowed( uri ) )
		{
			throw new InvalidOperationException( $"Access to '{uri}' is not allowed." );
		}

		var ext = System.IO.Path.GetExtension( url ).ToLower();

		native.Play( url, ext );
	}

	/// <summary>
	/// Plays a video file from a relative path. If there's already a video playing, it will stop.
	/// </summary>
	public void Play( BaseFileSystem filesystem, string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return;

		// if it looks like a url, play it as a url
		if ( Uri.TryCreate( path, UriKind.Absolute, out var uri ) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) )
		{
			Play( path );
			return;
		}

		var ext = System.IO.Path.GetExtension( path ).ToLower();

		if ( !filesystem.FileExists( path ) )
			return;

		path = filesystem.GetFullPath( path );
		if ( string.IsNullOrWhiteSpace( path ) )
			return;

		native.Play( path, ext );
	}

	/// <summary>
	/// Resumes video playback.
	/// </summary>
	public void Resume()
	{
		native.Resume();
	}

	/// <summary>
	/// Stops video playback.
	/// </summary>
	public void Stop()
	{
		native.Stop();
	}

	/// <summary>
	/// Pauses video playback.
	/// </summary>
	public void Pause()
	{
		native.Pause();
	}

	/// <summary>
	/// Toggle video playback
	/// </summary>
	public void TogglePause()
	{
		if ( IsPaused ) native.Resume();
		else native.Pause();
	}

	/// <summary>
	/// Sets the playback position to a specified time in the video, given in seconds.
	/// </summary>
	public void Seek( float time )
	{
		native.Seek( time );
	}

	/// <summary>
	/// Present a video frame.
	/// </summary>
	public void Present()
	{
		native.Update();
	}

	/// <summary>
	/// The video is muted
	/// </summary>
	public bool Muted
	{
		get => native.IsMuted();
		set => native.SetMuted( value );
	}

	internal void SetVideoOnly()
	{
		native.SetVideoOnly();
	}
}
