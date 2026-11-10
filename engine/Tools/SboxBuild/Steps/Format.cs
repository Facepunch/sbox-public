using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class Format( Constants.Solutions solution, Format.Mode mode = Format.Mode.Full, bool verifyOnly = false )
{
	public enum Mode
	{
		Full = 0,
		Whitespace = 1,
	}

	internal ExitCode Run()
	{
		var solutionDir = Constants.GetSolutionDir( solution );
		GitHubActions.Group( $"{solution} ({mode.ToString().ToLowerInvariant()})" );

		var modeArgs = mode == Mode.Whitespace ? "whitespace --folder" : "";
		if ( verifyOnly )
		{
			modeArgs += " --verify-no-changes";
		}
		if ( !Utility.RunDotnetCommand( solutionDir, $"format {modeArgs}" ) )
		{
			GitHubActions.EndGroup();
			Log.Error( verifyOnly ? $"{solution} needs formatting. Run tools/FormatAll.bat and commit the result." : $"Formatting {solution} failed." );
			if ( verifyOnly ) GitHubActions.Note( $"{solution} needs formatting" );
			return ExitCode.Failure;
		}

		Log.Info( $"Format completed successfully for {solution} in mode {mode}" );
		return ExitCode.Success;
	}
}
