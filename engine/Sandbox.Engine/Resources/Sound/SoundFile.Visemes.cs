namespace Sandbox;

public partial class SoundFile
{
	/// <summary>
	/// Name of the custom resource block holding the viseme frames in a compiled
	/// sound. Injected at compile time by the managed sound compiler from the
	/// visemes authored in the sound editor (stored in the sound's .meta).
	/// </summary>
	internal const string VisemeBlockName = "LIPS";

	/// <summary>
	/// The lipsync track for this sound, or null if it doesn't have one. Authored
	/// in the sound editor's viseme timeline. Read from the compiled file when the
	/// sound resource loads - check <see cref="IsValidForPlayback"/> to tell
	/// "no track" apart from "not loaded yet".
	/// </summary>
	public VisemeTrack Visemes { get; private set; }

	/// <summary>
	/// Read the viseme track out of the sound's compiled file data. Called when
	/// the sound resource loads or reloads.
	/// </summary>
	void ReadVisemes( ResourceLoadContext context )
	{
		var frames = context.ReadJson<List<VisemeFrame>>( VisemeBlockName );

		Visemes = frames is { Count: > 0 } ? new VisemeTrack( frames ) : null;
	}
}
