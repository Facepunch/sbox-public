namespace Sandbox;

public partial class SoundFile
{
	/// <summary>
	/// Name of the custom resource block holding the subtitle words in a compiled
	/// sound. Injected at compile time by the managed sound compiler from the
	/// subtitles authored in the sound editor (stored in the sound's .meta).
	/// </summary>
	internal const string SubtitleBlockName = "SUBT";

	/// <summary>
	/// The subtitle track for this sound, or null if it doesn't have one. Authored
	/// in the sound editor's timeline as individual words, so you can show them
	/// word by word as the sound plays. Read from the compiled file when the sound
	/// resource loads - check <see cref="IsValidForPlayback"/> to tell "no track"
	/// apart from "not loaded yet".
	/// </summary>
	public SubtitleTrack Subtitles { get; private set; }

	/// <summary>
	/// Read the subtitle track out of the sound's compiled file data. Called when
	/// the sound resource loads or reloads.
	/// </summary>
	void ReadSubtitles( ResourceLoadContext context )
	{
		var words = context.ReadJson<List<SubtitleTrack.Word>>( SubtitleBlockName );

		Subtitles = words is { Count: > 0 } ? new SubtitleTrack( words ) : null;
	}
}
