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

		var modeArgs = mode == Mode.Whitespace ? "whitespace --folder" : "";
		if ( verifyOnly )
		{
			modeArgs += " --verify-no-changes";
		}
		if ( !Utility.RunDotnetCommand( solutionDir, $"format {modeArgs}" ) )
		{
			return ExitCode.Failure;
		}

		Log.Error( $"Format completed successfully for {solution} in mode {mode}" );
		return ExitCode.Success;
	}
}
