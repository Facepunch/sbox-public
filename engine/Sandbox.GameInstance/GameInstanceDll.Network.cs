using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox;

internal partial class GameInstanceDll
{
	readonly StringTable CodeArchiveTable = new( "CodeArchive", true );

	internal readonly ServerPackages ServerPackages = new();

	/// <summary>
	/// The config table is used to send config (like physics config, input config) to the client.
	/// This isn't always needed, because the config is loaded from the package. But if we're operating
	/// without a package, it is needed.
	/// </summary>
	readonly StringTable ConfigTable = new( "Config", true );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly SmallNetworkFiles NetworkedSmallFiles = new( "SmallFiles" );

	/// <summary>
	/// Hold and network any files from Project Settings (.config files.)
	/// </summary>
	readonly SmallNetworkFiles NetworkedConfigFiles = new( "ConfigFiles" );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly LargeNetworkFiles NetworkedLargeFiles = new( "LargeFiles" );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly ReplicatedConvars ReplicatedConvars = new( "ReplicatedConvars" );

	private List<FileWatch> FileWatchers { get; set; } = new();
	private bool DidMountNetworkedFiles { get; set; }

	public GameNetworkSystem CreateGameNetworking( NetworkSystem system )
	{
		var instance = new SceneNetworkSystem( TypeLibrary, system );

		NetworkedLargeFiles.NetworkInitialize( instance );

		if ( Networking.IsHost )
		{
			if ( Application.GamePackage is { } gamePackage )
			{
				ServerPackages.AddRequirement( gamePackage );
			}

			AddFilesToNetwork( NetworkedConfigFiles, EngineFileSystem.ProjectSettings, [".config"] );
			BuildNetworkedFiles();
		}
		else if ( !DidMountNetworkedFiles )
		{
			EngineFileSystem.ProjectSettings.Mount( NetworkedConfigFiles.Files );
			FileSystem.Mounted.Mount( NetworkedSmallFiles.Files );

			NetworkedSmallFiles.Refresh();
			NetworkedConfigFiles.Refresh();

			ResourceLoader.LoadAllGameResource( FileSystem.Mounted );
			FontManager.Instance.LoadAll( FileSystem.Mounted );

			DidMountNetworkedFiles = true;
		}

		return instance;
	}

	void AddFilesToNetwork( SmallNetworkFiles target, BaseFileSystem fs, HashSet<string> validExtensions )
	{
		var files = fs.FindFile( "/", "*", true );

		foreach ( var fileName in files )
		{
			var extension = Path.GetExtension( fileName );
			if ( !validExtensions.Contains( extension ) )
				continue;

			var text = fs.ReadAllBytes( fileName );
			target.AddFile( fs, fileName, text.ToArray() );
		}

		var watcher = fs.Watch();
		watcher.OnChanges += w =>
		{
			foreach ( var fileName in w.Changes )
			{
				var extension = Path.GetExtension( fileName );
				if ( !validExtensions.Contains( extension ) )
					continue;

				if ( fs.FileExists( fileName ) )
				{
					var text = fs.ReadAllBytes( fileName );
					target.AddFile( fs, fileName, text.ToArray() );
				}
				else
				{
					target.RemoveFile( fileName );
				}
			}
		};

		FileWatchers.Add( watcher );
	}

	/// <summary>
	/// This is used to compile code archives that come in from the network.
	/// </summary>
	CompileGroup compileGroup;

	public void InstallNetworkTables( NetworkSystem system )
	{
		system.InstallTable( CodeArchiveTable );
		system.InstallTable( ServerPackages.StringTable );
		system.InstallTable( NetworkedSmallFiles.StringTable );
		system.InstallTable( NetworkedConfigFiles.StringTable );
		system.InstallTable( NetworkedLargeFiles.StringTable );
		system.InstallTable( ReplicatedConvars.StringTable );

		CodeArchiveTable.OnChangeOrAdd = ( entry ) =>
		{
			var codeArchive = new CodeArchive( entry.Data );
			var compiler = compileGroup.GetOrCreateCompiler( codeArchive.CompilerName );
			compiler.UpdateFromArchive( codeArchive );
		};

		CodeArchiveTable.PostNetworkUpdate = FinishLoadingCodeArchives;

		//
		// Config
		//
		system.InstallTable( ConfigTable );
		ConfigTable.PostNetworkUpdate = UpdateConfigFromNetworkTable;
	}

	void FinishLoadingCodeArchives()
	{
		// We need to build it syncronously because we don't want other
		// network shit coming in, that was created using the new assemblies
		// and us not being able to understand because we don't have the
		// new code compiled and loaded yet!
		SyncContext.RunBlocking( compileGroup.BuildAsync() );

		//
		// Get the new assemblies and update them
		//
		if ( compileGroup.BuildResult.Success )
		{
			foreach ( var assm in compileGroup.BuildResult.Output )
			{
				using var stream = new MemoryStream( assm.AssemblyData );
				AssemblyEnroller.LoadAssemblyFromStream( assm.Compiler.AssemblyName, stream );
			}
		}

		//
		// Do the hotload and stuff
		//
		FinishLoadingAssemblies();
	}

	public async Task LoadNetworkTables( NetworkSystem system )
	{
		compileGroup = new CompileGroup( "server" );

		// Don't hotload while we're downloading stuff!
		using var pauseAsmLoadScope = PauseLoadingAssemblies();

		// Any assemblies come our way? 
		foreach ( var entry in CodeArchiveTable.Entries )
		{
			var codeArchive = new CodeArchive( entry.Value.Data );
			var compiler = compileGroup.GetOrCreateCompiler( codeArchive.CompilerName );
			compiler.UpdateFromArchive( codeArchive );
		}

		// We might have loaded new assemblies, here's a safe time to
		// hotload before we start downloading again.
		FinishLoadingCodeArchives();

		// Load configs from network tables
		UpdateConfigFromNetworkTable();

		await ServerPackages.InstallAll();

		await NetworkedLargeFiles.RunDownloadQueue( system, default );
	}

	static string[] _interestingExtensions = new[] { "_c", ".scss", ".ttf" };
	static string[] _engineAssets = new[] { "vtex_c", "vmat_c", "vsnd_c", "vmdl_c", "vpk", "vanmgrph_c" }; // anything that the engine has to download has to be a LARGE download
	List<string> _netIncludePaths = new(); // wildcard-supported paths we also want to include content of

	bool ShouldNetworkFile( string filename )
	{
		filename = filename.NormalizeFilename();

		if ( !AssetDownloadCache.IsLegalDownload( filename ) )
			return false;

		if ( _netIncludePaths.Any( x => filename.WildcardMatch( x ) ) )
			return true;

		return _interestingExtensions.Any( x => filename.EndsWith( x ) );
	}

	void UpdateNetworkFile( BaseFileSystem fs, string filename )
	{
		// ignore code junk
		if ( filename.Contains( "\\code\\obj\\", System.StringComparison.OrdinalIgnoreCase ) )
			return;

		if ( filename.EndsWith( "vmap" ) ) filename = Path.ChangeExtension( filename, ".vpk" );
		else if ( !ShouldNetworkFile( filename ) )
		{
			return;
		}

		if ( !fs.FileExists( filename ) )
			return;

		bool isEngineAsset = _engineAssets.Any( x => filename.EndsWith( x ) );

		var fullPath = fs.GetFullPath( filename );
		var size = fs.FileSize( filename );

		var smallFileSize = 1024 * 64; // biggest file to include in the memory filesystem is 64kb

		if ( !isEngineAsset && size < smallFileSize )
		{
			var bytes = fs.ReadAllBytes( filename );
			var wasAdded = NetworkedSmallFiles.AddFile( fs, filename, bytes.ToArray() );

			if ( wasAdded )
			{
				if ( AssetDownloadCache.DebugNetworkFiles )
					Log.Info( $"Adding Small File {filename} ({size.FormatBytes()})" );
			}
			else
			{
				Log.Warning( $"File '{filename}' ('{fullPath}') doesn't exist - skipping" );
			}
		}
		else
		{
			var wasAdded = NetworkedLargeFiles.AddFile( filename );

			if ( wasAdded )
			{
				if ( AssetDownloadCache.DebugNetworkFiles )
					Log.Info( $"Adding LARGE File {filename} ({size.FormatBytes()})" );
			}
			else
			{
				Log.Warning( $"File '{filename}' ('{fullPath}') doesn't exist - skipping" );
			}
		}

	}

	/// <summary>
	/// Go through our mounted files and make them available to joining clients for download
	/// </summary>
	void BuildNetworkedFiles()
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();

		var gameInstance = IGameInstance.Current as GameInstance;
		if ( gameInstance is null )
		{
			Log.Warning( "Game Instance was null when building network files" );
			return;
		}

		// No network files needed for package based games
		if ( gameInstance.IsRemote ) return;

		Log.Info( "Building network files.." );

		// include anything on resource paths
		var project = Project.Current;
		if ( project is not null && !string.IsNullOrWhiteSpace( project.Config.Resources ) )
		{
			var resourcePaths = project.Config.Resources.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
			.Select( x => x.Trim() )
			.Where( x => !x.StartsWith( "//" ) )
			.Select( x => x.NormalizeFilename( true, false ) );

			_netIncludePaths.AddRange( resourcePaths );
		}

		var fs = gameInstance.GameFileSystem;
		var files = fs.FindFile( "/", "*", true );

		foreach ( var file in files )
		{
			UpdateNetworkFile( fs, file );
		}

		var watcher = fs.Watch();
		watcher.OnChanges += w =>
		{
			foreach ( var fileName in w.Changes )
			{
				UpdateNetworkFile( fs, fileName );
			}
		};

		FileWatchers.Add( watcher );
		Log.Info( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );
	}
}
