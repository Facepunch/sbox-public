using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// The native half of a pull request build. A PR that doesn't touch native code or interop
/// definitions reuses the public native binaries for its base; anything else builds them, as does a
/// failed download.
/// </summary>
internal static class PullRequestNative
{
	internal static ExitCode Run( BuildConfiguration config, bool clean )
	{
		if ( Utility.PrTouchesNativeCode() )
		{
			GitHubActions.Note( "built, the PR touches native code" );
		}
		else if ( new DownloadPublicArtifacts( nativeBinariesOnly: true ).Run() == ExitCode.Success )
		{
			GitHubActions.Note( "downloaded, the PR doesn't touch native code" );
			return ExitCode.Success;
		}
		else
		{
			Log.Warning( "Couldn't download the public native binaries, building them instead." );
			GitHubActions.Note( "built, the public binaries didn't download" );
		}

		return Build.Run( config, clean, skipNative: false, skipManaged: true );
	}
}
