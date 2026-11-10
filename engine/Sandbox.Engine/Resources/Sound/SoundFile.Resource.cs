namespace Sandbox;

public partial class SoundFile
{
	/// <summary>
	/// The sound's file data has been loaded (or reloaded) - read what we want
	/// out of the compiled file.
	/// </summary>
	internal override void OnLoaded( ResourceLoadContext context )
	{
		ReadVisemes( context );
		ReadSubtitles( context );
	}
}
