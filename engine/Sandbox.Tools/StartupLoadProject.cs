using Sandbox.Engine;
using Sandbox.Engine.Shaders;
using Sandbox.Physics;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Editor;

/// <summary>
/// Called once on editor startup to load the -project
/// </summary>
static class StartupLoadProject
{
	public static bool IsLoading { get; private set; } = false;

	public static Logger Log = new( "Startup" );

	/// <summary>
	/// Load the startup project for the first time
	/// </summary>
	public static async Task Run()
	{
		IsLoading = true;

		// Create the editor window - hidden
		new EditorMainWindow();

		var path = Sandbox.Utility.CommandLine.GetSwitch( "-project", "" ).TrimQuoted();

		Log.Info( $"Opening {path}" );

		bool success = await OpenProject( path, default );

		if ( !success )
		{
			EditorUtility.Quit( true );
			return;
		}

		//
		// Add to project list if not already there
		//
		{
			var projectList = new ProjectList();
			projectList.TryAddFromFile( path );
			projectList.SaveList();
		}

		//
		// We don't need you no more
		//
		Editor.EditorSplashScreen.StartupFinish();

		//
		// We're ready - open the editor window
		//
		EditorWindow.Startup();

		// Show build notifications by default for any compilers from now on
		CompileGroup.SuppressBuildNotifications = false;

		IsLoading = false;
	}

	/// <summary>
	/// Opens the project for the editor, this should only be called once from the -project command line.
	/// </summary>
	static async Task<bool> OpenProject( string path, CancellationToken ct )
	{
		if ( string.IsNullOrEmpty( path ) ) throw new ArgumentException( nameof( path ) );
		Assert.IsNull( Project.Current );

		// should never be one existing once we remove all this shit
		Project project = Project.AddFromFile( path, false );
		var parentPackage = project.Config.GetMetaOrDefault<string>( "ParentPackage", null );

		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Init FileSystem" ) )
		{
			FileSystem.InitializeFromProject( project );
		}

		ProjectCookie?.Dispose();
		ProjectCookie = new CookieContainer( "project", fileSystem: FileSystem.ProjectTemporary );

		// Set the project as active and sync with the package manager to install any dependant packages the compiler depends on
		Project.Current = project;
		Project.Current.LastOpened = DateTime.Now;
		project.Active = true;
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Init AssetSystem" ) )
		{
			AssetSystem.InitializeFromProject( project );
		}

		// Scan the project for libraries
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Init Libraries" ) )
		{
			LibrarySystem.InitializeFromProject( project );
		}

		//
		// We want to load all the built in projects before anything else. This gives us a baseline.
		// Then if our "parentpackage" has a "base" package, it'll hotload over our baseline.
		//
		Log.Info( $"Load Builtin Projects" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Builtin Projects" ) )
		{
			await PackageManager.InstallProjects( Project.All.Where( x => x.IsBuiltIn ).ToArray() );
		}

		//
		// Load the dlls etc from our parent package
		//
		if ( project.Config.Type == "addon" && !string.IsNullOrWhiteSpace( parentPackage ) )
		{
			using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: ParentPackage" ) )
			{
				Log.Info( $"Load {parentPackage}" );
				await PackageManager.InstallAsync( new PackageLoadOptions( parentPackage, "tools" ) );
			}
		}

		//
		// This should really only load our current project, that we're editing right now
		//
		Log.Info( $"Syncing package manager" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Sync PackageManager" ) )
		{
			await Project.SyncWithPackageManager();
		}


		// This double Load shit is stupid, creates the compilers properly now that we've installed any dependant packages
		project.Load();

		ExportSettings( project );

		Log.Info( $"Generating solution" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Generate Solution" ) )
		{
			await Project.GenerateSolution();
		}

		Log.Info( $"Creating Filesystem" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Update ProjectFilesystem" ) )
		{
			UpdateProjectFilesystem( project );
		}

		// Compiles and waits for the project in a bullshit way - this already starts happening way sooner
		Log.Info( $"Compiling projects" );

		if ( await EditorUtility.Projects.Updated( project ) == false )
		{
			using var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Compile" );
			// load failed, present the user with the information
			// and let them decide if they want to continue broken or bail (or fix things, recompile and continue)

			if ( await StartupFailedPopup.Show( project ) == false )
				return false;

			await EditorUtility.Projects.WaitForCompiles();
		}

		if ( project.Config.Type == "addon" && !string.IsNullOrWhiteSpace( parentPackage ) )
		{
			using var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Load Addon" );

			Log.Info( $"LoadGamePackageAsync" );
			// Install it as the active game
			await GameInstanceDll.Current.LoadGamePackageAsync( parentPackage, GameLoadingFlags.Host | GameLoadingFlags.Reload, ct );

			Log.Info( $"AssetSystem.InstallAsync" );
			// Install into asset system so we can use prefabs, gameresources, etc.
			await AssetSystem.InstallAsync( parentPackage, false );

			Log.Info( $"MountAsync" );
			// Mount our current project, and load the source!
			await project.Package.MountAsync( true );

			// Mount our current project into the filesystem and make sure to load all assets
			FileSystem.Mounted.CreateAndMount( project.GetAssetsPath() );
			ResourceLoader.LoadAllGameResource( FileSystem.Mounted );
		}
		else
		{
			using var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Load Game" );

			// It'd be nice to do a full end to end test, but this is as far as it'll go
			if ( Sandbox.Application.IsUnitTest )
				return true;

			Log.Info( $"Loading game package" );
			await GameInstanceDll.Current.LoadGamePackageAsync( project.Package.FullIdent, GameLoadingFlags.Host | GameLoadingFlags.Reload, ct );
		}


		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Init Mounts" ) )
		{
			// Mount any mounts that are required
			await EditorUtility.Mounting.InitMountsFromConfig( project );
		}

		//
		// Load the resources
		//
		Log.Info( "Importing custom assets" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: Register CustomAssetTypes" ) )
		{
			AssetType.UpdateCustomTypes();
			AssetType.ImportCustomTypeFiles();
		}

		// Download cloud assets afterwards, otherwise we don't have references
		Log.Info( "Refreshing cloud assets" );
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: DownloadCloudAssets" ) )
		{
			await RefreshCloudAssets( ct );
		}

		// Go through and compile all assets
		using ( var _ = Bootstrap.StartupTiming?.ScopeTimer( $"Load Project: CompileAllAssets" ) )
		{
			await CompileAllShaders();
			CompileAllAssets();

			FileWatch.Tick();

			// do we even need this anymore?
			IAssetSystem.LoadWorkingSetsAndTags();
		}

		return true;
	}

	static void UpdateProjectFilesystem( Project project )
	{
		var assetsPath = project.GetAssetsPath();
		if ( !System.IO.Directory.Exists( assetsPath ) )
			return;

		NativeEngine.FullFileSystem.AddProjectPath( project.Config.FullIdent, project.GetAssetsPath() );

		var cloudFolder = System.IO.Path.Combine( project.GetRootPath(), ".sbox", "cloud" );
		NativeEngine.FullFileSystem.AddCloudPath( "mod_cloud", cloudFolder );

		var transientFolder = System.IO.Path.Combine( project.GetRootPath(), ".sbox", "transient" );
		NativeEngine.FullFileSystem.AddCloudPath( "mod_transient", transientFolder );

		//
		// The engine ships a bunch of transient files, like image generations from the addon base, and
		// cloud assets that the menu scene uses. Mount them last, but no need in the menu project.
		//
		if ( project.Config.Ident != "menu" )
		{
			var engineTransient = EngineFileSystem.Root.GetFullPath( "addons/menu/transients" );
			NativeEngine.FullFileSystem.AddCloudPath( "mod_engtrans", engineTransient );
		}

		Editor.FileSystem.RebuildContentPath();

		if ( !Sandbox.Application.IsUnitTest )
		{
			IAssetSystem.UpdateMods();
		}
	}

	/// <summary>
	/// At some point we'll figure out how to do resources so they'll compile properly.
	/// Until then.. this is what we got.
	/// </summary>
	static async Task CompileAllShaders()
	{
		var projectDir = Path.GetFullPath( Project.Current.RootDirectory.FullName );

		var sw = Stopwatch.StartNew();
		var gr = AssetSystem.All.Where( x => x.AssetType == AssetType.Shader && x.HasSourceFile && Path.GetFullPath( x.AbsoluteSourcePath ).StartsWith( projectDir, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		if ( gr.Length == 0 ) return;

		var options = new ShaderCompileOptions();
		options.ForceRecompile = false;
		options.SingleThreaded = false;
		options.ConsoleOutput = false;

		FastTimer timer = FastTimer.StartNew();
		foreach ( var r in gr )
		{
			await ShaderCompile.Compile( r.AbsolutePath, r.RelativePath, options, default );
		}
		if ( sw.Elapsed.TotalSeconds > 2 )
		{
			Log.Info( $"Compiling shaders took {sw.Elapsed.TotalSeconds:0.000}s" );
		}
	}

	static void CompileAllAssets()
	{
		var sw = Stopwatch.StartNew();
		var gr = AssetSystem.All.Where( x => !x.IsTrivialChild && x.CanRecompile && !x.IsCompiledAndUpToDate ).ToArray();
		if ( gr.Length == 0 ) return;

		FastTimer timer = FastTimer.StartNew();

		int i = 0;
		foreach ( var r in gr )
		{
			i++;
			Log.Info( $"Compiling {i}/{gr.Length} {r.Path}" );
			IToolsDll.Current?.Spin();
			r.Compile( false );
		}

		if ( sw.Elapsed.TotalSeconds > 2 )
		{
			Log.Info( $"Compiling assets took {sw.Elapsed.TotalSeconds:0.000}s" );
		}
	}

	/// <summary>
	/// Download all required cloud resources for each asset in the asset system, and remove any we don't need anymore 
	/// </summary>
	static async Task RefreshCloudAssets( CancellationToken token )
	{
		var sw = Stopwatch.StartNew();

		packagesToDownload.AddRange( CloudAsset.GetAssetReferences( false ) );

		// 1. remove any installed packages we no longer need
		var required = new HashSet<string>();
		if ( IGameInstance.Current?.Package is RemotePackage remotePackage )
		{
			// may require a game package, don't uninstall that
			required.Add( remotePackage.FullIdent );
		}

		foreach ( var ident in packagesToDownload )
		{
			// normalise into org.ident, without version, so we can easily check below
			if ( Package.TryParseIdent( ident, out var p ) )
				required.Add( Package.FormatIdent( p.org, p.package ) );
		}

		foreach ( var package in AssetSystem.GetInstalledPackages() )
		{
			if ( required.Contains( package.FullIdent ) ) continue;

			Log.Info( $"'{package.FullIdent}' is unused, cleaning up.." );
			AssetSystem.UninstallPackage( package );
		}

		// 2. make sure everything we need is installed
		await CloudAsset.Install( packagesToDownload.Distinct(), token );

		Log.Info( $"..refresh complete (took {sw.Elapsed.TotalSeconds:0.000}s)" );
	}

	private static void ExportSettings( Project project )
	{
		if ( !FileSystem.ProjectSettings.FileExists( "/Input.config" ) )
		{
			if ( !project.Config.TryGetMeta<InputSettings>( "InputSettings", out var meta ) )
			{
				meta = new InputSettings();
			}

			FileSystem.ProjectSettings.WriteJson( "/Input.config", meta.Serialize() );
		}

		if ( project.Config.SetMeta( "InputSettings", null ) )
		{
			project.Save();
		}

		if ( !FileSystem.ProjectSettings.FileExists( "/Collision.config" ) )
		{
			if ( !project.Config.TryGetMeta<CollisionRules>( "Collision", out var meta ) )
			{
				meta = new CollisionRules();
			}

			EditorUtility.SaveProjectSettings( meta, "/Collision.config" );
		}

		if ( project.Config.SetMeta( "Collision", null ) )
		{
			project.Save();
		}
	}

	static List<string> packagesToDownload = new();

	/// <summary>
	/// Adds a package to packagesToDownload - which will be downloaded
	/// </summary>
	internal static void QueuePackageDownload( string packageName )
	{
		if ( !IsLoading )
			throw new System.Exception( "QueuePackageDownload shouldn't get called after startup" );

		packagesToDownload.Add( packageName );
	}
}
