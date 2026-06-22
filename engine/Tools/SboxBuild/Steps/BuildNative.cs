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
	/// Developer build with memory debugging enabled
	/// </summary>
	DeveloperMemoryDebug,

	/// <summary>
	/// Retail build with optimizations for release
	/// </summary>
	Retail
}

/// <summary>
/// Step to build the native code components
/// </summary>
internal class BuildNative( BuildConfiguration configuration = BuildConfiguration.Developer, bool clean = false )
{
	private readonly Platform platform = Platform.Create();

	internal ExitCode Run()
	{
		// Build strategy based on build type
		if ( configuration == BuildConfiguration.Retail )
		{
			return BuildRetail();
		}
		else
		{
			return BuildDeveloper();
		}
	}

	private ExitCode CompileSolution( string solutionName, bool forceRebuild = false )
	{
		if ( !platform.CompileSolution( solutionName, forceRebuild ) )
		{
			Log.Error( $"Failed to build {solutionName}." );
			return ExitCode.Failure;
		}

		return ExitCode.Success;
	}

	private ExitCode BuildRetail()
	{
		Log.Info( "Starting Retail build..." );

		// Ignore clean flag and don't rebuild on CI
		bool forceRebuild = clean && !Utility.IsCi();

		if ( CompileSolution( "schemacompiler_all", forceRebuild ) != ExitCode.Success )
			return ExitCode.Failure;

		if ( CompileSolution( $"buildbot_all_{platform.PlatformID}", forceRebuild ) != ExitCode.Success )
			return ExitCode.Failure;

		// For tools, we always use rebuild in retail mode regardless of the CleanBuild setting
		if ( platform is WindowsPlatform )
		{
			if ( CompileSolution( $"buildbot_tools_{platform.PlatformID}", true ) != ExitCode.Success )
				return ExitCode.Failure;
		}

		return ExitCode.Success;
	}

	private ExitCode BuildDeveloper()
	{
		Log.Info( "Starting Developer build..." );

		if ( CompileSolution( "schemacompiler_all", clean ) != ExitCode.Success )
			return ExitCode.Failure;

		if ( CompileSolution( $"developer_all_{platform.PlatformID}", clean ) != ExitCode.Success )
			return ExitCode.Failure;

		return ExitCode.Success;
	}
}
