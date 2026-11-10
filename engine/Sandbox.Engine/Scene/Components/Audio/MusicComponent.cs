namespace Sandbox;

/// <summary>
/// Plays background music through <see cref="Game.Music"/>. Drop it in, pick a track. Only one track plays
/// at a time, so enabling another music component crossfades to it. Music isn't tied to the scene - to keep
/// a track going across scene loads either put this on a DontDestroyOnLoad object or turn off StopOnDisable.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Music" )]
[Icon( "music_note" )]
[Tint( EditorTint.Green )]
public sealed class MusicComponent : Component
{
	/// <summary>
	/// The music to play. Tick Loop in the sound editor for a gapless loop.
	/// </summary>
	[Property] public SoundFile Track { get; set; }

	/// <summary>
	/// Volume of this track, on top of the game's music volume.
	/// </summary>
	[Property, Range( 0, 1 )] public float Volume { get; set; } = 1.0f;

	/// <summary>
	/// Start again when the track ends.
	/// </summary>
	[Property] public bool Loop { get; set; } = true;

	/// <summary>
	/// Start playing as soon as this component is enabled.
	/// </summary>
	[Property] public bool PlayOnStart { get; set; } = true;

	/// <summary>
	/// Stop the music when this component is disabled or destroyed, if it's still the track playing.
	/// Turn off to let it carry on, e.g. across a scene load.
	/// </summary>
	[Property] public bool StopOnDisable { get; set; } = true;

	/// <summary>
	/// Seconds to fade in when starting, and to crossfade from whatever was playing.
	/// </summary>
	[Property] public float FadeIn { get; set; } = 1.0f;

	/// <summary>
	/// Seconds to fade out when stopping.
	/// </summary>
	[Property] public float FadeOut { get; set; } = 1.0f;

	protected override void OnEnabled()
	{
		if ( PlayOnStart )
			Play();
	}

	protected override void OnDisabled()
	{
		if ( StopOnDisable )
			Stop();
	}

	/// <summary>
	/// Play this track, crossfading from whatever is playing.
	/// </summary>
	[Button( "Play", "play_arrow" )]
	public void Play()
	{
		if ( Track is null )
			return;

		Game.Music.Play( Track, FadeIn, Loop, Volume );
	}

	/// <summary>
	/// Fade out and stop, if this track is the one playing.
	/// </summary>
	[Button( "Stop", "stop" )]
	public void Stop()
	{
		if ( Track is null || Game.Music.Track != Track )
			return;

		Game.Music.Stop( FadeOut );
	}
}
