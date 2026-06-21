using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class ShaderProc
{
	internal ExitCode Run()
	{
		Facepunch.ShaderProc.Program.Process( "engine" );
		return ExitCode.Success;
	}
}
