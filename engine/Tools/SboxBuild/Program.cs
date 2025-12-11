using System;
using System.CommandLine;
using Facepunch.Pipelines;
using Facepunch.Steps;
using static Facepunch.Constants;

namespace Facepunch;

/// <summary>
/// Main entry point for the SboxBuild tool
/// </summary>
internal class Program
{
	static int Main( string[] args )
	{
		// Create root command
		var rootCommand = new RootCommand( "sboxbuild - Build and deployment tool for s&box\n\nRun this from your sbox project root." );

		AddBuildPipeline( rootCommand );
		AddFormatPipeline( rootCommand );
		AddBuildContentStep( rootCommand );
		AddTestStep( rootCommand );
		AddBuildShadersStep( rootCommand );
		AddGenerateSolutionsStep( rootCommand );

		AddSyncPublicRepo( rootCommand );

		AddPullRequestPipeline( rootCommand );
		AddDeployPipeline( rootCommand );

		rootCommand.Invoke( args );
		return Environment.ExitCode;
	}

	private static void AddBuildPipeline( RootCommand rootCommand )
	{
		var buildCommand = new Command( "build", "Build managed & native code" );

		var configOption = new Option<BuildConfiguration>(
			"--config",
			description: "Build configuration (Developer, Retail, etc.)",
			getDefaultValue: () => BuildConfiguration.Developer );

		var cleanOption = new Option<bool>(
			"--clean",
			description: "Whether to do a clean build",
			getDefaultValue: () => false );

		var skipNativeOption = new Option<bool>(
			"--skip-native",
			description: "Skip building native code",
			getDefaultValue: () => false );

		var skipManagedOption = new Option<bool>(
			"--skip-managed",
			description: "Skip building managed code",
			getDefaultValue: () => false );

		buildCommand.AddOption( configOption );
		buildCommand.AddOption( cleanOption );
		buildCommand.AddOption( skipNativeOption );
		buildCommand.AddOption( skipManagedOption );

		buildCommand.SetHandler( ( BuildConfiguration config, bool clean, bool skipNative, bool skipManaged ) =>
		{
			var pipeline = Build.Create( config, clean, skipNative, skipManaged );
			ExitCode result = pipeline.Run();
			Environment.ExitCode = (int)result;
		}, configOption, cleanOption, skipNativeOption, skipManagedOption );

		rootCommand.Add( buildCommand );
	}

	private static void AddFormatPipeline( RootCommand rootCommand )
	{
		var formatCommand = new Command( "format", "Format all code" );

		var verifyOption = new Option<bool>(
			"--verify",
			description: "Verify compliance without changes",
			getDefaultValue: () => false );

		formatCommand.AddOption( verifyOption );

		formatCommand.SetHandler( ( bool verifyOnly ) =>
		{
			var pipeline = FormatAll.Create( verifyOnly );
			ExitCode result = pipeline.Run();
			Environment.ExitCode = (int)result;
		}, verifyOption );
		rootCommand.Add( formatCommand );
	}

	private static void AddPullRequestPipeline( RootCommand rootCommand )
	{
		var pullRequestCommand = new Command( "pullrequest", "Run the pull request pipeline" );
		pullRequestCommand.SetHandler( () =>
		{
			var pipeline = PullRequest.Create();
			ExitCode result = pipeline.Run();
			Environment.ExitCode = (int)result;
		} );
		rootCommand.Add( pullRequestCommand );
	}

	private static void AddDeployPipeline( RootCommand rootCommand )
	{
		var deployCommand = new Command( "deploy", "Run the deployment pipeline" );

		var targetOption = new Option<BuildTarget>(
			"--target",
			description: "Target environment (Staging or Release)",
			getDefaultValue: () => BuildTarget.Staging );

		var cleanOption = new Option<bool>(
			"--clean",
			description: "Whether to do a clean build",
			getDefaultValue: () => false );

		deployCommand.AddOption( targetOption );
		deployCommand.AddOption( cleanOption );

		deployCommand.SetHandler( ( BuildTarget target, bool clean ) =>
		{
			var pipeline = Deploy.Create( target, clean );
			ExitCode result = pipeline.Run();
			Environment.ExitCode = (int)result;
		}, targetOption, cleanOption );

		rootCommand.Add( deployCommand );
	}

	private static void AddBuildContentStep( RootCommand rootCommand )
	{
		var buildContentCommand = new Command( "build-content", "Build game content" );
		buildContentCommand.SetHandler( () =>
		{
			var step = new BuildContent( "Build Content" );
			ExitCode result = step.Run();
			Environment.ExitCode = (int)result;
		} );
		rootCommand.Add( buildContentCommand );
	}

	private static void AddTestStep( RootCommand rootCommand )
	{
		var testsCommand = new Command( "test", "Run tests" );
		testsCommand.SetHandler( () =>
		{
			var step = new Test( "Run Tests" );
			ExitCode result = step.Run();
			Environment.ExitCode = (int)result;
		} );
		rootCommand.Add( testsCommand );
	}

	private static void AddBuildShadersStep( RootCommand rootCommand )
	{
		var buildShadersCommand = new Command( "build-shaders", "Build shaders" );
		var forcedOption = new Option<bool>(
			"--forced",
			description: "Whether to force rebuild all shaders",
			getDefaultValue: () => false );

		buildShadersCommand.AddOption( forcedOption );

		buildShadersCommand.SetHandler( ( bool forced ) =>
		{
			var step = new BuildShaders( "Build Shaders", forced );
			ExitCode result = step.Run();
			Environment.ExitCode = (int)result;
		}, forcedOption );

		rootCommand.Add( buildShadersCommand );
	}
	private static void AddGenerateSolutionsStep( RootCommand rootCommand )
	{
		var generateSolutionsCommand = new Command( "generate-solutions", "Generate Visual Studio solutions without building them" );

		var configOption = new Option<BuildConfiguration>(
			"--config",
			description: "Build configuration (Developer, Retail)",
			getDefaultValue: () => BuildConfiguration.Developer );

		generateSolutionsCommand.AddOption( configOption );

		generateSolutionsCommand.SetHandler( ( BuildConfiguration config ) =>
		{
			var step = new GenerateSolutions( "Generate Solutions", config );
			ExitCode result = step.Run();
			Environment.ExitCode = (int)result;
		}, configOption );

		rootCommand.Add( generateSolutionsCommand );
	}

	private static void AddSyncPublicRepo( RootCommand rootCommand )
	{
		var syncCommand = new Command( "sync-public-repo", "Perform a dry run of the sync step" );

		var option = new Option<bool>(
			"--dry-run",
			description: "Whether to perform a dry run",
			getDefaultValue: () => false );

		syncCommand.AddOption( option );

		syncCommand.SetHandler( ( bool dryRun ) =>
		{
			var step = new SyncPublicRepo( "Sync Public Repo", dryRun );
			ExitCode result = step.Run();
			Environment.ExitCode = (int)result;
		}, option );

		rootCommand.Add( syncCommand );
	}
}
