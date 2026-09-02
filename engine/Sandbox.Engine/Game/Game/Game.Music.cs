using Sandbox.Audio;

namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// Background music. Plays one track at a time and crossfades between them, through the "Music" mixer.
	/// This lives outside any scene, so music keeps playing (or fading) through loading screens and scene
	/// changes until something plays another track or stops it. <see cref="Sound.StopAll"/> stops it too.
	/// For layered stems or streaming from a url use <see cref="MusicPlayer"/> instead.
	/// </summary>
	public static class Music
	{
		static SoundHandle handle;
		static float trackVolume = 1.0f;
		static float volume = 1.0f;
		static bool loop;
		static RealTimeSince timeSinceStarted;

		/// <summary>
		/// The last track we faded out, so playing it again quickly can just carry on.
		/// </summary>
		static SoundHandle fadingOut;
		static SoundFile fadingOutTrack;

		/// <summary>
		/// The track currently playing, or null.
		/// </summary>
		public static SoundFile Track { get; private set; }

		/// <summary>
		/// True if a track is playing or fading in.
		/// </summary>
		public static bool IsPlaying => handle.IsValid() && handle.IsPlaying;

		/// <summary>
		/// How loud the music is right now, for visualisers. 0 when nothing is playing.
		/// </summary>
		public static float Amplitude => handle.IsValid() ? handle.Amplitude : 0.0f;

		/// <summary>
		/// Music volume, 0 to 1, on top of the track's own volume. This is a straight volume control for
		/// the game to use - a user setting belongs on the Music mixer, and turning the music down for a
		/// moment is what <see cref="Duck"/> is for.
		/// </summary>
		public static float Volume
		{
			get => volume;
			set
			{
				volume = value.Clamp( 0, 1 );
				ApplyVolume();
			}
		}

		/// <summary>
		/// Pause or resume the current track.
		/// </summary>
		public static bool Paused
		{
			get => handle.IsValid() && handle.Paused;
			set
			{
				if ( handle.IsValid() )
					handle.Paused = value;
			}
		}

		/// <summary>
		/// Play a track. Whatever is playing fades out over <paramref name="fade"/> seconds while this fades in.
		/// <paramref name="volume"/> is for this track only, on top of <see cref="Volume"/> - use it to tame a loud file.
		/// Looping is gapless if the sound has loop points (tick Loop in the sound editor), otherwise the track
		/// restarts when it ends. Playing the track that's already playing does nothing.
		/// </summary>
		public static void Play( SoundFile track, float fade = 1.0f, bool loop = true, float volume = 1.0f )
		{
			if ( track is null || !track.IsValid )
			{
				Log.Warning( "Can't play music, the sound file is invalid" );
				return;
			}

			if ( IsPlaying && Track == track )
				return;

			Stop( fade );

			Track = track;
			trackVolume = volume.Clamp( 0, 1 );
			Music.loop = loop;

			// Asked for the track we just started fading out - carry on with it rather than restarting
			if ( fadingOut.IsValid() && fadingOutTrack == track )
			{
				handle = fadingOut;
				handle.IsFadingOut = false;
				fadingOut = null;
				fadingOutTrack = null;
				ApplyVolume();
				return;
			}

			Start( fade );
		}

		/// <summary>
		/// Play a track by path, e.g. "music/theme.mp3". See <see cref="Play(SoundFile, float, bool, float)"/>.
		/// </summary>
		public static void Play( string path, float fade = 1.0f, bool loop = true, float volume = 1.0f )
		{
			var track = SoundFile.Load( path );
			if ( track is null )
			{
				Log.Warning( $"Can't play music, couldn't find '{path}'" );
				return;
			}

			Play( track, fade, loop, volume );
		}

		/// <summary>
		/// Fade out and stop the current track.
		/// </summary>
		public static void Stop( float fade = 1.0f )
		{
			if ( handle.IsValid() )
			{
				handle.Stop( fade );
				fadingOut = handle;
				fadingOutTrack = Track;
			}

			handle = null;
			Track = null;
		}

		static void Start( float fadeIn )
		{
			handle = Sound.PlayFile( Track, FinalVolume, fadeInTime: fadeIn );
			if ( !handle.IsValid() )
			{
				Track = null;
				return;
			}

			handle.ListenLocal = true;
			handle.TargetMixer = Mixer.FindMixerByName( "music" ) ?? Mixer.Default;
			timeSinceStarted = 0;
		}

		/// <summary>
		/// Everything multiplied together - the track's own volume, the game's volume, and however much
		/// we're ducked right now.
		/// </summary>
		static float FinalVolume => trackVolume * volume * (1.0f - ducking);

		static void ApplyVolume()
		{
			if ( handle.IsValid() )
				handle.Volume = FinalVolume;
		}

		/// <summary>
		/// Restart a non-looping sound when it runs out, if we're meant to be looping. Called every frame by the audio engine.
		/// </summary>
		internal static void Tick()
		{
			UpdateDucking();

			if ( fadingOut is not null && !fadingOut.IsValid() )
			{
				fadingOut = null;
				fadingOutTrack = null;
			}

			if ( Track is null || handle.IsValid() )
				return;

			// A track that dies straight away didn't play at all - missing file, failed compile. Don't spin on it.
			if ( timeSinceStarted < 1.0f )
			{
				Log.Warning( $"Music '{Track.ResourcePath}' stopped right after starting - is the sound file missing or broken?" );
				Track = null;
				return;
			}

			if ( loop )
			{
				Start( 0 );
				return;
			}

			Track = null;
		}

		const float DefaultDuckFade = 0.3f;

		static readonly List<DuckHandle> ducks = new();
		static float ducking;
		static float duckFade = DefaultDuckFade;

		/// <summary>
		/// How much the music is turned down right now, 0 (not at all) to 1 (silent). This eases towards
		/// the strongest active duck, so it lags behind while fading.
		/// </summary>
		public static float Ducking => ducking;

		/// <summary>
		/// Turn the music down while something more important plays over it - a trailer, a cutscene, a bit
		/// of dialogue. <paramref name="amount"/> is how much to duck by: 0.9 leaves the music at 10% of its
		/// volume, 1 silences it completely. The track carries on playing underneath either way.
		/// <para>
		/// The music stays ducked until the returned handle is disposed, then fades back up - so hold onto
		/// it for as long as you're playing over the top. If several things duck at once the strongest one
		/// wins, so they don't need to know about each other.
		/// </para>
		/// </summary>
		/// <param name="amount">How much to turn the music down, 0 to 1.</param>
		/// <param name="fade">Seconds to fade down, and back up again afterwards.</param>
		public static IDisposable Duck( float amount = 0.8f, float fade = DefaultDuckFade )
		{
			var duck = new DuckHandle( amount.Clamp( 0, 1 ), fade );
			ducks.Add( duck );
			return duck;
		}

		/// <summary>
		/// Drop every active duck, so a game that forgot to dispose one can't leave the music turned
		/// down forever. Called when a game shuts down.
		/// </summary>
		internal static void ClearDucks()
		{
			ducks.Clear();
		}

		/// <summary>
		/// Ease towards the strongest duck we've been asked for. Called every frame.
		/// </summary>
		static void UpdateDucking()
		{
			var target = 0.0f;

			foreach ( var duck in ducks )
			{
				if ( duck.Amount <= target ) continue;

				target = duck.Amount;
				duckFade = duck.Fade;
			}

			if ( ducking == target )
				return;

			ducking = duckFade > 0.0f ? ducking.Approach( target, RealTime.Delta / duckFade ) : target;
			ApplyVolume();
		}

		sealed class DuckHandle( float amount, float fade ) : IDisposable
		{
			public readonly float Amount = amount;
			public readonly float Fade = fade;

			// UpdateDucking keeps duckFade at the strongest duck's pace, so the fade back up is already set
			public void Dispose() => ducks.Remove( this );
		}
	}
}
