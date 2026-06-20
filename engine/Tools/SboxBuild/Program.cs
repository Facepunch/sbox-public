using System;
using System.CommandLine;
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
		var rootCommand = new RootCommand( "sboxbuild - Build and deployment tool for s&box\n\nRun this from your sbox project root." );

		// Compound commands (handle multiple related steps with flags)
		AddBuildCommand( rootCommand );
		AddFormatCommand( rootCommand );

		// Individual step commands
		AddBuildContentCommand( rootCommand );
		AddTestCommand( rootCommand );
		AddBuildShadersCommand( rootCommand );
		AddGenerateSolutionsCommand( rootCommand );
		AddSyncPublicRepoCommand( rootCommand );
		AddWriteVersionCommand( rootCommand );
		AddNvPatchCommand( rootCommand );
		AddSignBinariesCommand( rootCommand );
		AddBuildAddonsCommand( rootCommand );
		AddGameCacheCommand( rootCommand );
		AddUploadSymbolsCommand( rootCommand );
		AddUploadDocsCommand( rootCommand );
		AddSentryReleaseCommand( rootCommand );
		AddUploadSteamCommand( rootCommand );
		AddDiscordPostCommand( rootCommand );
		AddDownloadPublicArtifactsCommand( rootCommand );
		AddCheckNativeTouchedCommand( rootCommand );
		AddNotifySlackCommand( rootCommand );
		AddUploadReferenceAssembliesCommand( rootCommand );

		rootCommand.Invoke( args );
		return Environment.ExitCode;
	}

	// ── Compound commands ─────────────────────────────────────────────────────

	private static void AddBuildCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "build", "Build managed & native code" );

		var configOption = new Option<BuildConfiguration>( "--config",
			getDefaultValue: () => BuildConfiguration.Developer );
		var cleanOption = new Option<bool>( "--clean", getDefaultValue: () => false );
		var skipNativeOption = new Option<bool>( "--skip-native", getDefaultValue: () => false );
		var skipManagedOption = new Option<bool>( "--skip-managed", getDefaultValue: () => false );

		cmd.AddOption( configOption );
		cmd.AddOption( cleanOption );
		cmd.AddOption( skipNativeOption );
		cmd.AddOption( skipManagedOption );

		cmd.SetHandler( ( BuildConfiguration config, bool clean, bool skipNative, bool skipManaged ) =>
		{
			Environment.ExitCode = (int)Build.Run( config, clean, skipNative, skipManaged );
		}, configOption, cleanOption, skipNativeOption, skipManagedOption );

		rootCommand.Add( cmd );
	}

	private static void AddFormatCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "format", "Format all code" );
		var verifyOption = new Option<bool>( "--verify",
			description: "Verify compliance without changes",
			getDefaultValue: () => false );
		cmd.AddOption( verifyOption );
		cmd.SetHandler( ( bool verify ) =>
		{
			Environment.ExitCode = (int)FormatAll.Run( verify );
		}, verifyOption );
		rootCommand.Add( cmd );
	}

	// ── Individual step commands ──────────────────────────────────────────────

	private static void AddBuildContentCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "build-content", "Build game content" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new BuildContent().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddTestCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "test", "Run tests" );
		var noBuildOption = new Option<bool>( "--no-build",
			description: "Skip building before running tests (assumes projects are already built)",
			getDefaultValue: () => false );
		var filterOption = new Option<string>( "--filter",
			description: "dotnet test filter expression, e.g. TestCategory!=LiveBackend",
			getDefaultValue: () => null );
		cmd.AddOption( noBuildOption );
		cmd.AddOption( filterOption );
		cmd.SetHandler( ( bool noBuild, string filter ) =>
		{
			Environment.ExitCode = (int)new Test( noBuild, filter ).Run();
		}, noBuildOption, filterOption );
		rootCommand.Add( cmd );
	}

	private static void AddBuildShadersCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "build-shaders", "Build shaders" );
		var forcedOption = new Option<bool>( "--forced",
			description: "Force rebuild all shaders",
			getDefaultValue: () => false );
		cmd.AddOption( forcedOption );
		cmd.SetHandler( ( bool forced ) =>
		{
			Environment.ExitCode = (int)new BuildShaders( forced ).Run();
		}, forcedOption );
		rootCommand.Add( cmd );
	}

	private static void AddGenerateSolutionsCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "generate-solutions", "Generate Visual Studio solutions without building them" );
		var configOption = new Option<BuildConfiguration>( "--config",
			getDefaultValue: () => BuildConfiguration.Developer );
		cmd.AddOption( configOption );
		cmd.SetHandler( ( BuildConfiguration config ) =>
		{
			Environment.ExitCode = (int)new GenerateSolutions( config ).Run();
		}, configOption );
		rootCommand.Add( cmd );
	}

	private static void AddSyncPublicRepoCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "sync-public-repo", "Sync master branch to the public repository" );
		var dryRunOption = new Option<bool>( "--dry-run",
			getDefaultValue: () => false );
		cmd.AddOption( dryRunOption );
		cmd.SetHandler( ( bool dryRun ) =>
		{
			Environment.ExitCode = (int)new SyncPublicRepo( dryRun ).Run();
		}, dryRunOption );
		rootCommand.Add( cmd );
	}

	private static void AddWriteVersionCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "write-version", "Write version information to game/.version" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new WriteVersion().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddNvPatchCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "nvpatch", "Apply NvPatch to game executables" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new NvPatch().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddSignBinariesCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "sign-binaries", "Sign game binaries using Azure Trusted Signing" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new SignBinaries().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddBuildAddonsCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "build-addons", "Build addons and menu" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new BuildAddons().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddGameCacheCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "game-cache", "Create game cache" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new GameCache().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddUploadSymbolsCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "upload-symbols", "Upload debug symbols" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new UploadSymbols().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddUploadDocsCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "upload-docs", "Upload documentation schema" );
		cmd.SetHandler( () => { Environment.ExitCode = (int)new UploadDocumentation().Run(); } );
		rootCommand.Add( cmd );
	}

	private static void AddSentryReleaseCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "sentry-release", "Create and finalize a Sentry release" );
		cmd.SetHandler( () =>
		{
			Environment.ExitCode = (int)new SentryRelease( "fcpnch", "sbox-native" ).Run();
		} );
		rootCommand.Add( cmd );
	}

	private static void AddUploadSteamCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "upload-steam", "Upload build to Steam" );
		var targetOption = new Option<BuildTarget>( "--target",
			getDefaultValue: () => BuildTarget.Staging );
		cmd.AddOption( targetOption );
		cmd.SetHandler( ( BuildTarget target ) =>
		{
			string branch = BuildTargetToSteamBranch( target );
			Environment.ExitCode = (int)new UploadSteam( branch ).Run();
		}, targetOption );
		rootCommand.Add( cmd );
	}

	private static void AddDiscordPostCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "discord-post", "Post a build notification to Discord" );
		var targetOption = new Option<BuildTarget>( "--target",
			getDefaultValue: () => BuildTarget.Staging );
		cmd.AddOption( targetOption );
		cmd.SetHandler( ( BuildTarget target ) =>
		{
			var commitMessage = Environment.GetEnvironmentVariable( "COMMIT_MESSAGE" ) ?? "Build completed";
			if ( commitMessage.TrimStart().StartsWith( '!' ) )
			{
				Log.Info( "Skipping Discord notification: commit message starts with '!'." );
				return;
			}
			var version = Utility.VersionName();
			var message = $"New build ({version}) ready for {target}:\n\n{commitMessage}";
			Environment.ExitCode = (int)new DiscordPostStep( message, "Build" ).Run();
		}, targetOption );
		rootCommand.Add( cmd );
	}

	private static void AddDownloadPublicArtifactsCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "download-public-artifacts", "Download pre-built public artifacts" );
		var nativeOnlyOption = new Option<bool>( "--native-only",
			description: "Download only native binaries",
			getDefaultValue: () => false );
		cmd.AddOption( nativeOnlyOption );
		cmd.SetHandler( ( bool nativeOnly ) =>
		{
			Environment.ExitCode = (int)new DownloadPublicArtifacts( nativeOnly ).Run();
		}, nativeOnlyOption );
		rootCommand.Add( cmd );
	}

	private static void AddCheckNativeTouchedCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "check-native-touched", "Check if the PR touches native code; writes native_touched output" );
		cmd.SetHandler( () =>
		{
			Environment.ExitCode = (int)CheckNativeTouched.Run();
		} );
		rootCommand.Add( cmd );
	}

	private static void AddNotifySlackCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "notify-slack", "Send a workflow failure notification to Slack" );
		var messageOption = new Option<string>( "--message",
			description: "Optional custom message to include",
			getDefaultValue: () => null );
		cmd.AddOption( messageOption );
		cmd.SetHandler( ( string message ) =>
		{
			Environment.ExitCode = (int)NotifySlack.Run( message );
		}, messageOption );
		rootCommand.Add( cmd );
	}

	private static void AddUploadReferenceAssembliesCommand( RootCommand rootCommand )
	{
		var cmd = new Command( "upload-reference-assemblies", "Package the managed reference assemblies and upload them to the backend" );

		var targetOption = new Option<BuildTarget>(
			"--target",
			description: "Target environment / channel (Staging or Release)",
			getDefaultValue: () => BuildTarget.Staging );

		cmd.AddOption( targetOption );

		cmd.SetHandler( ( BuildTarget target ) =>
		{
			Environment.ExitCode = (int)new UploadReferenceAssemblies( target ).Run();
		}, targetOption );

		rootCommand.Add( cmd );
	}
}

