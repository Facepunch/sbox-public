using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class InteropGen( bool skipNative = false )
{
	internal ExitCode Run()
	{
		Facepunch.InteropGen.Program.ProcessManifest( "engine", skipNative );
		return ExitCode.Success;
	}
}
