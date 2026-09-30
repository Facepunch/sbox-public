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
		foreach ( var stage in GetStages( config, clean, skipNative, skipManaged ) )
		{
			if ( stage.Run() != ExitCode.Success )
				return ExitCode.Failure;
		}

		return ExitCode.Success;
	}

	internal static IEnumerable<(string Name, Func<ExitCode> Run)> GetStages(
		BuildConfiguration config = BuildConfiguration.Developer,
		bool clean = false,
		bool skipNative = false,
		bool skipManaged = false )
	{
		var isPublicSource = IsPublicSourceDistribution();
		var shouldSkipNative = skipNative || isPublicSource;

		if ( isPublicSource )
		{
			yield return ("Artifacts", () =>
			{
				Log.Info( "Detected public source distribution; downloading public artifacts and skipping native build." );
				return new DownloadPublicArtifacts().Run();
			}
			);
		}

		yield return ("Bindings", new InteropGen( skipNative: isPublicSource ).Run);

		if ( !isPublicSource )
			yield return ("Shader packing", new ShaderProc().Run);

		if ( !shouldSkipNative )
		{
			// Public distributions skip this with the rest of the native build - they link
			// nothing, so they need no third party binaries.
			yield return ("Dependencies", new DownloadThirdParty().Run);
			yield return ("Solutions", new GenerateSolutions( config ).Run);
			yield return ("Native", new BuildNative( config, clean ).Run);
		}

		if ( !skipManaged )
			yield return ("Managed", new BuildManaged( clean ).Run);
	}

	internal static bool IsPublicSourceDistribution()
	{
		var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
		var publicDir = Path.Combine( repoRoot, "public" );
		var steamworksDir = Path.Combine( repoRoot, "steamworks" );
		return !Directory.Exists( publicDir ) || !Directory.Exists( steamworksDir );
	}
}
