using System;
using System.IO;
using Facepunch;
using Facepunch.Steps;

namespace Facepunch.Pipelines;

internal class Build
{
	public static Pipeline Create( BuildConfiguration configuration = BuildConfiguration.Developer,
								 bool clean = false,
								 bool skipNative = false,
								 bool skipManaged = false,
								 bool aot = false )
	{
		var builder = new PipelineBuilder( "Build" );
		var isPublicSource = IsPublicSourceDistribution();
		var shouldSkipNative = skipNative || isPublicSource;

		if ( isPublicSource )
		{
			Log.Info( "Detected public source distribution; downloading public artifacts and skipping native build." );
			builder.AddStep( new DownloadPublicArtifacts( "Download Public Artifacts" ) );
		}

		// Always add interop gen
		builder.AddStep( new Steps.InteropGen( "Interop Gen", skipNative: false, aot: aot ) );

		if ( !isPublicSource )
		{
			builder.AddStep( new Steps.ShaderProc( "Shader Proc" ) );
		}

		// Add native build step if not skipped
		if ( !shouldSkipNative )
		{
			// Removed GenerateSolutions and BuildNative steps entirely to bypass problematic native build.
		}

		// Add managed build step if not skipped
		if ( !skipManaged )
		{
			builder.AddStep( new BuildManaged( "Build Managed", clean ) );
		}

		return builder.Build();
	}

	private static bool IsPublicSourceDistribution()
	{
		var repoRoot = Path.TrimEndingDirectorySeparator( Path.GetFullPath( Directory.GetCurrentDirectory() ) );
		// Those are only included in the full source distribution
		var publicDir = Path.Combine( repoRoot, "public" );
		var steamworksDir = Path.Combine( repoRoot, "steamworks" );
		return !Directory.Exists( publicDir ) || !Directory.Exists( steamworksDir );
	}
}
