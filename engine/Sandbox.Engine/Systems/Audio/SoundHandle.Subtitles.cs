namespace Sandbox;

public partial class SoundHandle
{
	/// <summary>
	/// The sound file this handle is playing, when we know it - sounds played from
	/// a <see cref="SoundFile"/> or a <see cref="SoundEvent"/> do, procedural
	/// streams don't.
	/// </summary>
	public SoundFile SoundFile { get; internal set; }

	/// <summary>
	/// The subtitle track of the sound being played, or null if it doesn't have
	/// one. Sample <see cref="SubtitleTrack.WordIndexAt"/> at <see cref="Time"/>
	/// while the sound plays to show the words as they're spoken, karaoke style -
	/// or let <see cref="SubtitlesGameObjectSystem"/> do it for you.
	/// </summary>
	public SubtitleTrack Subtitles => SoundFile?.Subtitles;
}
