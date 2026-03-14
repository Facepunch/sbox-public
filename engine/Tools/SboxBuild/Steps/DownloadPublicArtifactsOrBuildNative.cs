using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Attempts to download pre-built public artifacts. If the download fails,
/// falls back to a full native build (InteropGen + ShaderProc + GenerateSolutions + BuildNative).
/// </summary>
internal class DownloadPublicArtifactsOrBuildNative( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		Log.Info( "Attempting to download public artifacts..." );

		// Only download native binaries — content files are built separately and
		// are not needed to compile or test C#-only changes.
		var download = new DownloadPublicArtifacts( "Download Public Artifacts", nativeBinariesOnly: true );
		if ( download.Run() == ExitCode.Success )
		{
			// Artifacts downloaded — generate only the managed interop side.
			return new InteropGen( "Interop Gen", skipNative: true ).Run();
		}

		Log.Warning( "Public artifact download failed; falling back to full native build." );

		var fallbackSteps = new Step[]
		{
			new InteropGen( "Interop Gen" ),
			new ShaderProc( "Shader Proc" ),
			new GenerateSolutions( "Generate Solutions", BuildConfiguration.Retail ),
			new BuildNative( "Compile Native", BuildConfiguration.Retail, clean: true ),
		};

		foreach ( var step in fallbackSteps )
		{
			if ( step.Run() != ExitCode.Success )
				return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}
