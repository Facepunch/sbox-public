using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Step to build the VPC tool from source. Only needed on non-Windows platforms.
/// </summary>
internal class BuildVpc( string name ) : Step( name )
{
	protected override ExitCode RunInternal()
	{
		if ( OperatingSystem.IsWindows() )
		{
			Log.Info( "Skipping VPC build on Windows (prebuilt binary used)." );
			return ExitCode.Success;
		}

		Log.Info( "Building VPC..." );

		if ( !Utility.RunProcess( "make", $"-C src/utils/vpc/vpc -j" ) )
		{
			Log.Error( "VPC build failed." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}
