using Sandbox.Resources;

namespace Editor;

/// <summary>
/// Sounds are compiled by the native sound compiler, but we get first look at the
/// compile here. We read the lipsync visemes authored in the sound editor from the
/// sound's .meta and inject them into the compiled resource as a custom block, then
/// return false so the native compiler runs as usual - our block rides along into
/// the final file. The engine reads it back out via <see cref="SoundFile.Visemes"/>.
/// </summary>
[Expose]
[ResourceIdentity( "wav" )]
[ResourceIdentity( "mp3" )]
[ResourceIdentity( "ogg" )]
[ResourceIdentity( "flac" )]
public class SoundResourceCompiler : ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		var visemes = Context.ReadMeta<List<VisemeFrame>>( "visemes" );
		if ( visemes is not null && visemes.Count > 0 )
		{
			Context.WriteBlockJson( SoundFile.VisemeBlockName, visemes );
		}

		// We never compile the audio ourselves - returning false hands the
		// compile to the native sound compiler
		return Task.FromResult( false );
	}
}
