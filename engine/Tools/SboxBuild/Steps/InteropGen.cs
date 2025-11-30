using static Facepunch.Constants;

namespace Facepunch.Steps;

internal class InteropGen( string name, bool skipNative = false, bool aot = false ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		Facepunch.InteropGen.Program.ProcessManifest( "engine", skipNative );
		return ExitCode.Success;
	}
}
