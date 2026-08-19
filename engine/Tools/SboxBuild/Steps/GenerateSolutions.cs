using Facepunch.Native;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Step to generate the native Visual Studio projects and solutions from src/**/*.build.cs
/// </summary>
internal class GenerateSolutions( BuildConfiguration configuration = BuildConfiguration.Developer, string module = null, string platform = null )
{
	internal ExitCode Run()
	{
		var options = new Options
		{
			Platform = platform ?? NativePlatform.Host().Name,
			Retail = configuration == BuildConfiguration.Retail,
			MemoryDebug = configuration == BuildConfiguration.DeveloperMemoryDebug,
			Buildbot = Utility.IsCi()
		};

		if ( options.MemoryDebug ) Log.Info( "Using Memory Debug settings (ASAN + allocation tracking)" );

		return Generate.Run( options, module ) ? ExitCode.Success : ExitCode.Failure;
	}
}
