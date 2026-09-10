using Facepunch.Native;
using static Facepunch.Constants;

namespace Facepunch.Steps;

/// <summary>
/// Build type options for the native build process
/// </summary>
public enum BuildConfiguration
{
	/// <summary>
	/// Developer build with all components needed for development
	/// </summary>
	Developer,

	/// <summary>
	/// Developer build with memory debugging enabled (AddressSanitizer + allocation tracking; ~2.5x slower).
	/// </summary>
	DeveloperMemoryDebug,

	/// <summary>
	/// Retail build with optimizations for release
	/// </summary>
	Retail
}

/// <summary>
/// Step to build the native code components. What gets built, and with what, is the target platform's
/// business: it wrote the solutions or the makefile in the first place.
/// </summary>
internal class BuildNative( BuildConfiguration configuration = BuildConfiguration.Developer, bool clean = false )
{
	internal ExitCode Run()
	{
		var platform = NativePlatform.Current;
		var options = new Options
		{
			Platform = platform.Name,
			Retail = configuration == BuildConfiguration.Retail,
			MemoryDebug = configuration == BuildConfiguration.DeveloperMemoryDebug,
			Buildbot = Utility.IsCi()
		};

		Log.Info( $"Starting {configuration} build for {platform.Name}..." );

		// A clean Retail build on CI would be rebuilding what it just checked out.
		var force = clean && !(options.Retail && Utility.IsCi());

		foreach ( var (name, alwaysRebuild) in platform.Solutions( options ) )
		{
			if ( platform.Build( name, force || alwaysRebuild ) ) continue;

			Log.Error( $"Failed to build {name}." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}
}
