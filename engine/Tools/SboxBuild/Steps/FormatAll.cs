using static Facepunch.Constants;

namespace Facepunch.Steps;

internal static class FormatAll
{
	internal static ExitCode Run( bool verifyOnly = false )
	{
		if ( new Format( Solutions.Engine, Format.Mode.Full, verifyOnly ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		if ( new Format( Solutions.Toolbase, Format.Mode.Whitespace, verifyOnly ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		if ( new Format( Solutions.Menu, Format.Mode.Whitespace, verifyOnly ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		if ( new Format( Solutions.BuildTools, Format.Mode.Full, verifyOnly ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		return ExitCode.Success;
	}
}
