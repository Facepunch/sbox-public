using static Facepunch.Constants;

namespace Facepunch.Steps;

internal static class Build
{
	internal static ExitCode Run(
		BuildConfiguration config = BuildConfiguration.Developer,
		bool clean = false,
		bool skipNative = false,
		bool skipManaged = false )
	{
		var isPublicSource = IsPublicSourceDistribution();
		var shouldSkipNative = skipNative || isPublicSource;

		if ( isPublicSource )
		{
			Log.Info( "Detected public source distribution; downloading public artifacts and skipping native build." );
			if ( new DownloadPublicArtifacts().Run() != ExitCode.Success )
				return ExitCode.Failure;
		}

		if ( new InteropGen( skipNative: isPublicSource ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		if ( !isPublicSource && new ShaderProc().Run() != ExitCode.Success )
			return ExitCode.Failure;

		if ( !shouldSkipNative )
		{
			if ( new BuildVpc().Run() != ExitCode.Success )
				return ExitCode.Failure;

			if ( new GenerateSolutions( config ).Run() != ExitCode.Success )
				return ExitCode.Failure;

			if ( new BuildNative( config, clean ).Run() != ExitCode.Success )
				return ExitCode.Failure;
		}

		if ( !skipManaged && new BuildManaged( clean ).Run() != ExitCode.Success )
			return ExitCode.Failure;

		return ExitCode.Success;
	}

	private static bool IsPublicSourceDistribution()
	{
		var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
		var publicDir = Path.Combine( repoRoot, "public" );
		var steamworksDir = Path.Combine( repoRoot, "steamworks" );
		return !Directory.Exists( publicDir ) || !Directory.Exists( steamworksDir );
	}
}
