namespace Sandbox;

/// <summary>
/// Holds the state of a standalone game
/// </summary>
internal partial class StandaloneGameInstance : GameInstance
{
	public StandaloneGameInstance( string ident, GameLoadingFlags flags ) : base( ident, flags )
	{
		this.flags = flags;
	}

	/// <summary>
	/// Attempt to download this package and mount it as a game menu
	/// </summary>
	public override async Task<bool> LoadAsync( PackageLoader.Enroller enroller, CancellationToken token )
	{
		var project = Project.AddFromFile( Standalone.GamePath );
		await Project.SyncWithPackageManager();
		await Project.CompileAsync();
		await project.Package.MountAsync();

		_package = project.Package;
		Log.Info( $"Added from file {project.Config.Title} ({project.Package.FullIdent})" );

		PackageManager.CmdList();

		Application.GameIdent = Package is null ? Ident : $"{_package.Org.Ident}.{_package.Ident}";
		Application.GamePackage = _package;

		{
			var downloadOptions = new PackageLoadOptions
			{
				PackageIdent = Package.FullIdent,
				ContextTag = "gamemenu",
				AllowLocalPackages = true,
				CancellationToken = token
			};

			Log.Trace( $"Install Async from Package {Package.Title}" );
			LoadingScreen.Title = $"Installing {Package.Title}";
			activePackage = await PackageManager.InstallAsync( downloadOptions );
			if ( activePackage is null )
			{
				Log.Warning( $"Package {Package.FullIdent} was null" );
				return false;
			}
		}

		if ( token.IsCancellationRequested )
			return false;

		Log.Trace( $"Loading package {Package.Title}" );
		LoadingScreen.Title = $"Loading {Package.Title}";
		try
		{
			// Load the package. Mount it and add it to the file system.
			// Only load the assemblies inside the package if we're not a developer host
			// (If we're a develop host we load assemblies from the network table)
			if ( !enroller.LoadPackage( Package.FullIdent, !IsDeveloperHost ) )
			{
				if ( IsDeveloperHost )
					return true;

				Log.Warning( "There were errors when trying to load the package for the menu" );
				return false;
			}
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when loading {Package.FullIdent}: {e.Message}" );
			return false;
		}

		LoadingScreen.Title = $"Loading Resources";

		FileSystem.Mounted.Mount( new LocalFileSystem( Standalone.GamePath ) );
		NativeEngine.FullFileSystem.AddProjectPath( Ident, Standalone.GamePath );
		NativeEngine.g_pResourceSystem.ReloadSymlinkedResidentResources();

		EngineFileSystem.ProjectSettings = activePackage.ProjectSettings;

		LoadProjectSettings();

		Log.Trace( $"Loading GameResources" );
		ResourceLoader.LoadAllGameResource( FileSystem.Mounted );

		Log.Trace( $"Loading Fonts" );
		FontManager.Instance.LoadAll( FileSystem.Mounted );

		// Initialize localization
		Game.Language = new LanguageContainer();
		var localizationPath = System.IO.Path.Combine( Standalone.GamePath, "Localization" );
		if ( System.IO.Directory.Exists( localizationPath ) )
		{
			Game.Language.FileSystem.CreateAndMount( localizationPath );
			Game.Language.Refresh();
		}

		SetupFileWatch();

		try
		{
			GameInstanceDll.Current.UpdateProjectConfig( Package );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception when loading {Package.FullIdent}: {e.Message}" );
			return false;
		}

		_packageAssembly = null;


		if ( Package.TryParseIdent( Package.FullIdent, out var ident ) )
		{
			var loaded = enroller.FindAssembly( Package, $"package.{ident.org}.{ident.package}" );
			if ( loaded is not null )
			{
				_packageAssembly = loaded.Assembly;
			}
		}

		return true;
	}
}
