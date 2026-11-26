using System;
using System.IO;
using System.Text.Json;
using SceneTests;
using Sandbox.DataModel;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Tasks;
using Sandbox.Utility;

namespace ProjectTests;

/// <summary>
/// Registered local games take precedence over published packages and compile before installation.
/// </summary>
[TestClass]
public class LocalGameResolutionTest
{
	/// <summary>
	/// Registered projects win over cached published packages and revisions without compiling during lookup.
	/// </summary>
	[TestMethod]
	public async Task PackageLookupsPreferLocalProjects()
	{
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_lookup_{Guid.NewGuid():N}" );
		Project.Clear();

		try
		{
			WriteProject( root, "game", "game", ".sbproj", org: "installed" );
			WriteProject( root, "content", "content", ".sbproj", org: "installed" );
			Project.AddAddonPath( root );
			var project = Project.All.Single( x => x.Config.Type == "game" );
			Assert.IsFalse( project.IsPublished );

			var remote = new RemotePackage { Org = new Package.Organization { Ident = "installed" }, Ident = "game" };
			Package.Cache( remote, false );
			Assert.IsTrue( project.IsPublished );
			Package.Cache( remote, false, 123 );
			var content = new RemotePackage { Org = remote.Org, Ident = "content" };
			Package.Cache( content, false );

			foreach ( var ident in new[] { "installed.game", "INSTALLED.GAME#123", "installed/game" } )
			{
				Assert.IsTrue( Package.TryGetCached( ident, out var cached ) );
				Assert.AreSame( project.Package, cached );
				Assert.AreSame( project.Package, await Package.FetchAsync( ident, false ) );
				Assert.AreSame( project.Package, await Package.FetchAsync( ident, true, useCache: false ) );
			}

			Assert.IsFalse( project.Active );
			Assert.IsFalse( project.HasCompiler );
			var localContent = Project.FindByIdent( "installed.content" );
			Assert.AreSame( localContent.Package, await Package.FetchAsync( "installed.content", false ) );
			Assert.AreSame( localContent.Package, await Package.FetchAsync( "installed.content#123", false ) );
			Assert.AreSame( localContent.Package, await Package.FetchAsync( "INSTALLED.CONTENT", false ) );

			WriteProject( root, "duplicate/content", "content", ".sbproj", org: "INSTALLED" );
			Assert.IsNull( Project.AddFromFile( Path.Combine( root, "duplicate", "content", ".sbproj" ) ) );
			Assert.AreSame( localContent, Project.FindByIdent( "installed.content" ) );

			localContent.Config.Ident = "game";
			Assert.AreEqual( "installed.content", (await Package.FetchAsync( "installed.content", false )).FullIdent );
			localContent.Save();
			Assert.AreEqual( "content", localContent.Config.Ident );

			project.Config.Ident = "content";
			project.Save();
			Assert.AreEqual( "game", project.Config.Ident );

			var changedConfig = JsonSerializer.Deserialize<ProjectConfig>( File.ReadAllText( localContent.ConfigFilePath ) );
			changedConfig.Ident = "game";
			File.WriteAllText( localContent.ConfigFilePath, JsonSerializer.Serialize( changedConfig ) );
			Assert.IsNull( Project.AddFromFile( localContent.ConfigFilePath ) );
			Assert.AreSame( project, Project.FindByIdent( "installed.game" ) );
			Assert.AreSame( localContent, Project.FindByIdent( "installed.content" ) );
			Assert.AreEqual( "installed.content", localContent.Package.FullIdent );

			Project.Remove( localContent );
			Assert.AreSame( content, await Package.FetchAsync( "installed.content", false ) );

			Project.Remove( project );
			Assert.AreSame( remote, await Package.FetchAsync( "installed.game", false ) );
		}
		finally
		{
			Package.ClearCache( "installed.game" );
			Package.ClearCache( "installed.game#123" );
			Package.ClearCache( "installed.content" );
			Project.Clear();
			Directory.Delete( root, true );
		}
	}

	/// <summary>
	/// Installing a published game prepares its discovered local project, even when supplied cloud metadata. Failed compilation leaves it available for a retry.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task InstallingGamePreparesLocalProject( bool failFirst )
	{
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_install_{Guid.NewGuid():N}" );
		Project.Clear();

		try
		{
			WriteProject( root, "game", "game", ".sbproj", org: "installed" );
			WriteProject( root, "unused", "library", ".sbproj", org: "installed" );
			WriteProject( root, "game/Libraries/dependency", "library", ".sbproj", org: "installed" );
			var codePath = Path.Combine( root, "game", "Code", "Game.cs" );
			var code = """
				/// <summary>
				/// An assembly compiled from the installed game.
				/// </summary>
				public class InstalledGame { }
				""";
			File.WriteAllText( codePath, failFirst ? "invalid C# source" : code );
			Project.AddAddonPath( root );
			Project.AddAddonPath( Path.Combine( root, "game", "Libraries" ) );
			var project = Project.All.Single( x => x.Config.Type == "game" );
			var options = new PackageLoadOptions
			{
				PackageIdent = "installed.game#123",
				Package = new RemotePackage { Org = new Package.Organization { Ident = "installed" }, Ident = "game" },
				ContextTag = "test_installed_game"
			};

			if ( failFirst )
			{
				var first = PackageManager.InstallAsync( options );
				var second = PackageManager.InstallAsync( options with { PackageIdent = "installed.game#124" } );
				var dependency = PackageManager.InstallAsync( options with { PackageIdent = "installed.dependency#125", Package = null } );
				await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => first );
				await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => second );
				await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => dependency );
				Assert.IsFalse( project.Active );
				Assert.IsNull( PackageManager.Find( options.PackageIdent ) );
				Assert.IsNull( PackageManager.Find( "installed.dependency" ) );
				File.WriteAllText( codePath, code );
			}

			var installs = await Task.WhenAll( PackageManager.InstallAsync( options ),
				PackageManager.InstallAsync( options with { PackageIdent = "installed.game#124" } ) );
			var installed = installs[0];
			Assert.AreSame( installed, installs[1] );
			Assert.AreSame( project.Package, installed.Package );
			Assert.IsTrue( project.Active );
			Assert.IsFalse( Project.All.Single( x => x.Config.Ident == "unused" ).HasCompiler );
			Assert.IsTrue( Project.All.Single( x => x.Config.Ident == "dependency" ).HasCompiler );
			Assert.IsTrue( installed.AssemblyFileSystem.FileExists( ".bin/package.installed.game.dll" ) );
			Assert.AreSame( installed, PackageManager.Find( "installed.game#123" ) );
			Assert.AreSame( installed, await PackageManager.InstallAsync( options ) );
		}
		finally
		{
			PackageManager.UnmountTagged( "test_installed_game" );
			Project.Clear();
			Directory.Delete( root, true );
		}
	}

	/// <summary>
	/// A listen host advertises its archive. Matching peers keep their assembly and older peers request the host's code.
	/// </summary>
	[TestMethod]
	[DataRow( 20, false, false )]
	[DataRow( 10, false, false )]
	[DataRow( 10, true, false )]
	[DataRow( 10, false, true )]
	public void JoiningClientRunsLocalHostsGameCode( int clientDamage, bool updateDuringJoin, bool corruptDownload )
	{
		using var scope = GlobalContext.GameScope();
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_archive_{Guid.NewGuid():N}" );
		var previousSystem = Networking.System;
		var dll = (GameInstanceDll)IGameInstanceDll.Current;

		if ( GameInstanceDll.Current is null )
			dll.Bootstrap();

		Project.Clear();

		try
		{
			var host = LoadCopy( "host", 20 );
			Assert.AreEqual( 20, ReadDamage() );
			Assert.IsTrue( dll.CodeArchiveTable.Entries.ContainsKey( host.Compiler.AssemblyName ) );

			var hostArchives = new CodeArchiveTable();
			var name = host.Compiler.AssemblyName;
			var archive = dll.CodeArchiveTable.Entries[name].Data;
			hostArchives.Set( name, archive );
			var peer = new TestConnection();
			dll.CodeArchiveTable.SendSnapshot( peer );

			var weaponFile = Path.Combine( root, "host", "archiveprobe", "Code", "Weapon.cs" );
			File.WriteAllText( weaponFile, File.ReadAllText( weaponFile ).Replace( "=> 20;", "=> 35;" ) );
			host.Compiler.MarkForRecompile();
			Assert.IsTrue( SyncContext.RunBlocking( host.Compiler.Group.BuildAsync() ) );
			var hotload = host.Compiler.Output.Archive.Serialize();

			dll.CloseGame();
			Project.Remove( host );
			Networking.System = new NetworkSystem( "joining client", dll.TypeLibrary );
			var connection = new TestConnection( Guid.NewGuid(), isHost: true );
			Networking.System.Connect( connection );

			LoadCopy( "client", clientDamage );
			Assert.AreEqual( clientDamage, ReadDamage() );
			var installedType = dll.TypeLibrary.GetType( "ArchiveProbeWeapon" ).TargetType;
			Assert.AreEqual( 0, dll.CodeArchiveTable.Entries.Count );

			dll.InstallNetworkTables( Networking.System );
			peer.ProcessMessages( InternalMessageType.TableSnapshot, incoming =>
			{
				Assert.AreEqual( dll.CodeArchiveTable.Name, incoming.Read<string>() );
				var manifest = incoming;
				Assert.AreEqual( name, manifest.Read<string>() );
				Assert.AreEqual( Sandbox.Utility.Crc64.FromBytes( archive ), manifest.Read<ulong>() );
				Assert.IsNull( manifest.Read<string>() );
				Assert.AreEqual( 0, manifest.GetRemainingBytes().Length );
				dll.CodeArchiveTable.ReadSnapshot( incoming );
			} );

			var loading = dll.LoadNetworkTables( Networking.System );
			var requests = connection.Messages.Where( m => m.Type == InternalMessageType.Request ).ToArray();
			Assert.AreEqual( clientDamage == 20 ? 0 : 1, requests.Length );
			if ( requests.Length > 0 )
			{
				var server = new NetworkSystem( "listen host", dll.TypeLibrary );
				typeof( NetworkSystem ).GetProperty( nameof( NetworkSystem.IsHost ) ).SetValue( server, true );
				server.AddHandler<RequestCodeArchive>( hostArchives.OnRequest );
				peer.System = server;
				peer.State = Connection.ChannelState.Welcome;
				Deliver( requests.Single(), peer );

				if ( updateDuringJoin ) SendHotload();

				var response = peer.Messages.Single( m => m.Type == InternalMessageType.Response );
				if ( corruptDownload ) response.Raw[^1] ^= 1;
				Deliver( response, connection );
			}

			Assert.AreEqual( !corruptDownload, SyncContext.RunBlocking( loading.WaitAsync( TimeSpan.FromSeconds( 10 ) ) ) );
			if ( corruptDownload ) return;

			Assert.AreEqual( updateDuringJoin ? 35 : 20, ReadDamage() );
			Assert.AreEqual( clientDamage == 20 ? 0 : 1, dll.NetworkCompileGroup.BuildResult.Output?.Count ?? 0 );
			Assert.AreEqual( clientDamage == 20, installedType == dll.TypeLibrary.GetType( "ArchiveProbeWeapon" ).TargetType );
			Assert.AreEqual( 1, dll.CodeArchiveTable.Entries.Count );

			if ( clientDamage == 20 )
			{
				SendHotload();
				Assert.AreEqual( 35, ReadDamage() );
				Assert.AreEqual( 1, dll.NetworkCompileGroup.BuildResult.Output.Count );
			}

			void SendHotload()
			{
				hostArchives.Set( name, hotload );
				var update = ByteStream.Create( 1024 );
				hostArchives.BuildUpdateMessage( ref update );
				using var incoming = ByteStream.CreateReader( update.ToArray() );
				update.Dispose();
				dll.CodeArchiveTable.ReadUpdate( incoming );
			}

			static void Deliver( TestConnection.Message message, TestConnection receiver )
			{
				using var data = ByteStream.CreateReader( message.Raw );
				receiver.System.HandleIncomingMessage( new NetworkSystem.NetworkMessage { Source = receiver, Data = data } );
			}
		}
		finally
		{
			dll.CloseGame();
			Networking.System = previousSystem;
			Project.Clear();
			Directory.Delete( root, true );
		}

		Project LoadCopy( string peer, int damage )
		{
			var path = Path.Combine( root, peer );
			WriteProject( path, "archiveprobe", "game", ".sbproj" );
			File.WriteAllText( Path.Combine( path, "archiveprobe", "Code", "Weapon.cs" ), $$"""
				/// <summary>
				/// A weapon whose damage differs between the two installed copies.
				/// </summary>
				public static class ArchiveProbeWeapon
				{
					/// <summary>
					/// The damage dealt by this build.
					/// </summary>
					public static int Damage => {{damage}};
				}
				""" );
			Project.AddAddonPath( path );
			Assert.IsTrue( SyncContext.RunBlocking( dll.LoadGamePackageAsync( "local.archiveprobe", GameLoadingFlags.Remote | GameLoadingFlags.Reload, default ) ) );
			return Project.FindByIdent( "local.archiveprobe" );
		}

		int ReadDamage()
		{
			var weapon = dll.TypeLibrary.GetType( "ArchiveProbeWeapon" );
			return (int)weapon.Properties.Single( p => p.Name == "Damage" ).GetValue( null );
		}
	}

	/// <summary>
	/// Initial and live requirements skip an absent local addon, mount a registered local project, and reject an unavailable Sandbox package.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void JoiningClientResolvesServerPackages( bool liveUpdate, bool missingRequired )
	{
		using var game = new TestGame();
		const string localGame = "local.joinprobe";
		const string hostAddon = "LOCAL.hostonlyaddon";
		const ulong mapId = 4321;
		const string sandbox = "facepunch.sandbox#2147483647";

		try
		{
			WriteProject( game.Root, "joinprobe", "game", ".sbproj" );
			Project.AddAddonPath( game.Root );

			var mapFolder = Directory.CreateDirectory( Path.Combine( game.Root, "map" ) ).FullName;
			File.WriteAllText( Path.Combine( mapFolder, "join-map-content.txt" ), "bundled asset" );
			Directory.CreateDirectory( Path.Combine( mapFolder, "maps" ) );
			File.Copy( Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "core/maps/error.vpk" ), Path.Combine( mapFolder, "maps/error.vpk" ) );
			using var installed = OpenWorkshopMap( mapFolder, mapId, "maps/error.vpk" );

			var system = Networking.System = new NetworkSystem( "joining client", game.Dll.TypeLibrary );
			system.Connect( new TestConnection( Guid.NewGuid(), isHost: true ) );
			Assert.IsTrue( SyncContext.RunBlocking( game.Dll.LoadGamePackageAsync( localGame, GameLoadingFlags.Remote | GameLoadingFlags.Reload, default ) ) );
			game.Dll.InstallNetworkTables( system );

			// Cached metadata without downloadable files makes the required mount fail offline.
			if ( missingRequired )
				Package.Cache( new RemotePackage { Org = new Package.Organization { Ident = "facepunch" }, Ident = "sandbox" }, false, int.MaxValue );

			var table = game.Dll.ServerPackages.StringTable;
			var ready = new ServerPackages.PackageInfo { ResourcesReady = true };
			table.Set( localGame, liveUpdate ? default : ready );
			game.Dll.WorkshopItems.StringTable.Set( mapId.ToString(), new WorkshopItems.ItemInfo { Id = mapId, Type = Workshop.ItemType.Map, ResourcesReady = true } );

			// With a live update the registered game keeps the join waiting until the host marks it ready.
			var join = liveUpdate ? game.Dll.LoadNetworkTables( system ) : null;
			var addonEntry = table.Set( hostAddon, new ServerPackages.PackageInfo() );
			var requiredEntry = missingRequired ? table.Set( sandbox, ready ) : null;
			if ( liveUpdate )
			{
				Assert.IsFalse( join.IsCompleted );
				table.OnChangeOrAdd( addonEntry );
				if ( missingRequired ) table.OnChangeOrAdd( requiredEntry );
				table.OnChangeOrAdd( table.Set( localGame, ready ) );
			}
			else
			{
				join = game.Dll.LoadNetworkTables( system );
			}

			Assert.AreEqual( !missingRequired, SyncContext.RunBlocking( join.WaitAsync( TimeSpan.FromSeconds( 10 ) ) ) );
			Assert.IsNull( ServerPackages.Get( hostAddon ), "A host-only project should never start a download." );
			if ( missingRequired )
			{
				StringAssert.Contains( system.FailureReason, liveUpdate ? "Failed to install server packages" : "required server package" );
			}
			else
			{
				Assert.IsTrue( ServerPackages.Get( localGame ).IsMounted );
				Assert.IsTrue( FileSystem.Mounted.FileExists( "join-map-content.txt" ) );
				var map = SyncContext.RunBlocking( game.Dll.WorkshopItems.OpenMapAsync( mapId, default ) );
				Assert.AreEqual( "maps/error.vpk", map.Asset );
				Assert.IsNull( map.Release );
			}
		}
		finally
		{
			if ( missingRequired ) Package.ClearCache( sandbox );
		}
	}

	/// <summary>
	/// A host loads selected maps by Workshop ID, map package ident, game asset path, or mounted scene URI.
	/// A bundled map project stands in for its Workshop item, and its ident becomes that item's ID.
	/// A map project's primary asset path doesn't load it, nor does a package ident that isn't a map.
	/// </summary>
	[TestMethod]
	[DataRow( "local" )]
	[DataRow( "local-ident" )]
	[DataRow( "bundled" )]
	[DataRow( "bundled-ident" )]
	[DataRow( "workshop" )]
	[DataRow( "game" )]
	[DataRow( "workshop-vpk" )]
	[DataRow( "workshop-missing-asset" )]
	[DataRow( "workshop-no-asset" )]
	[DataRow( "package-ident" )]
	[DataRow( "mounted-uri" )]
	public void LaunchSelectedSceneMap( string source )
	{
		using var game = new TestGame();
		const string primaryAsset = "maps/launch_probe.scene";
		const ulong itemId = 54321;
		var workshop = source.StartsWith( "workshop" );
		var mounted = source == "mounted-uri";
		var bundled = source.StartsWith( "bundled" );
		var byIdent = source.EndsWith( "-ident" ) && source != "package-ident";
		var selectedAsset = source switch
		{
			"workshop-vpk" => "maps/error.vmap",
			"workshop-missing-asset" => "maps/missing.scene",
			"workshop-no-asset" => null,
			_ => primaryAsset
		};

		using var testMount = mounted ? RegisterTestMount() : null;
		WriteProject( game.Root, "launchgame", "game", ".sbproj", configure: config =>
		{
			config.Metadata["StartupScene"] = mounted ? primaryAsset : "missing.scene";
			config.Metadata["MapStartupScene"] = source == "workshop-vpk" ? primaryAsset : "missing.scene";
		} );
		if ( source.StartsWith( "local" ) || bundled )
		{
			game.WriteMap( primaryAsset, bundled ? itemId : 0 );
		}

		var folder = source == "game" || mounted ? "launchgame" : "map";
		var assets = game.CopyAsset( folder, primaryAsset + "_c" );
		if ( source == "workshop-vpk" ) game.CopyAsset( folder, "maps/error.vpk", "core/maps/error.vpk" );
		Project.AddAddonPath( game.Root );

		using var installed = workshop ? OpenWorkshopMap( assets, itemId, selectedAsset ) : null;
		LaunchArguments.Map = source switch
		{
			"package-ident" => "local.launchgame",
			"mounted-uri" => "mount://testgame/maps/testlevel.scene",
			_ => byIdent ? "local.map" : workshop || bundled ? itemId.ToString() : primaryAsset
		};

		var loaded = SyncContext.RunBlocking( game.Dll.LoadGamePackageAsync( "local.launchgame", GameLoadingFlags.Host | GameLoadingFlags.Reload, default ) );
		if ( source is "local" or "package-ident" or "workshop-missing-asset" )
		{
			Assert.IsFalse( loaded );
			Assert.AreEqual( 0, game.Dll.WorkshopItems.StringTable.Entries.Count );
			return;
		}

		Assert.IsTrue( loaded );
		Assert.AreEqual( workshop ? 1 : 0, game.Dll.WorkshopItems.StringTable.Entries.Count );
		Assert.AreEqual( workshop || bundled ? itemId : 0, Application.Map.WorkshopId );
		if ( bundled ) Assert.AreEqual( "map", Application.Map.Title );

		// The launch map's project isn't required of clients. They mount their own copy when they launch.
		if ( bundled ) Assert.IsFalse( game.Dll.ServerPackages.StringTable.Entries.Keys.Any( x => x.StartsWith( "local.map", StringComparison.OrdinalIgnoreCase ) ) );
		if ( byIdent ) Assert.AreEqual( bundled ? itemId.ToString() : "local.map", LaunchArguments.Map );
		Assert.IsNotNull( Game.ActiveScene );
		Assert.IsTrue( FileSystem.Mounted.FileExists( primaryAsset + "_c" ) );

		if ( source is "workshop-vpk" or "mounted-uri" )
		{
			var scene = new Scene();
			using var cleanup = new DisposeAction( () => scene.Destroy() );
			using var sceneScope = scene.Push();
			var component = OpenMap( scene, fromLaunch: true );
			Assert.AreEqual( LaunchArguments.Map, component.MapName );
			if ( mounted )
			{
				Assert.IsTrue( component.GameObject.Children.Any( x => x.Name == "MountedRoot" ) );
			}
			else
			{
				Assert.AreEqual( "maps/error.vpk", component.LoadedVpkPath );

				// The launch map stays required after the component that loaded it lets go.
				component.UnloadMap();
				Assert.AreEqual( 1, game.Dll.WorkshopItems.StringTable.Entries.Count );
			}
		}
	}

	/// <summary>
	/// A menu launch opens the configured menu before the selected map and records its scene source.
	/// </summary>
	[TestMethod]
	public void MenuSceneTakesPrecedenceOverLaunchMap()
	{
		using var game = new TestGame();
		WriteProject( game.Root, "menugame", "game", ".sbproj", configure: config => config.Metadata["MenuScene"] = "menu.scene" );
		game.CopyAsset( "menugame", "menu.scene_c" );
		game.CopyAsset( "menugame", "level.scene_c" );
		Project.AddAddonPath( game.Root );
		LaunchArguments.Map = "level.scene";

		Assert.IsTrue( SyncContext.RunBlocking( game.Dll.LoadGamePackageAsync( "local.menugame", GameLoadingFlags.Host | GameLoadingFlags.Menu | GameLoadingFlags.Reload, default ) ) );
		Assert.IsTrue( Game.IsOnMenu );
	}

	/// <summary>
	/// A scene's map Workshop ID mounts the bundled map project published as it, without installing the Workshop item.
	/// </summary>
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void SceneMapMountsBundledProject( bool hasPrimaryAsset )
	{
		using var game = new TestGame();
		const string primaryAsset = "maps/scene_probe.scene";
		const ulong itemId = 65432;

		game.WriteMap( hasPrimaryAsset ? primaryAsset : "maps/missing.scene", itemId );
		game.CopyAsset( "map", primaryAsset + "_c", "unittest/addons/glass_test/Assets/scenes/glass_test.scene_c" );
		Project.AddAddonPath( game.Root );
		Assert.IsFalse( FileSystem.Mounted.FileExists( primaryAsset + "_c" ) );

		// A menu names the map after its project, without mounting it.
		var info = SyncContext.RunBlocking( new MapReference( itemId.ToString() ).GetInfoAsync() );
		Assert.AreEqual( "map", info.Title );
		Assert.IsFalse( FileSystem.Mounted.FileExists( primaryAsset + "_c" ) );

		var scene = new Scene();
		using var cleanup = new DisposeAction( () => scene.Destroy() );
		using var sceneScope = scene.Push();
		var first = OpenMap( scene, itemId.ToString(), expected: hasPrimaryAsset );
		if ( !hasPrimaryAsset )
		{
			Assert.IsFalse( IsRequired(), "A failed scene load must release its package immediately." );
			return;
		}

		var second = OpenMap( scene, itemId.ToString() );
		Assert.IsTrue( FileSystem.Mounted.FileExists( primaryAsset + "_c" ) );
		Assert.AreEqual( 0, game.Dll.WorkshopItems.StringTable.Entries.Count );

		// A map that isn't the launch map is required of clients for as long as it's loaded.
		Assert.IsTrue( IsRequired() );
		first.UnloadMap();
		Assert.IsTrue( IsRequired() );
		second.UnloadMap();
		Assert.IsFalse( IsRequired() );

		var reopened = OpenMap( scene, itemId.ToString() );
		Assert.IsTrue( IsRequired() );
		reopened.UnloadMap();
		Assert.IsFalse( IsRequired() );

		bool IsRequired() => game.Dll.ServerPackages.StringTable.Entries.Keys.Any( x => x.StartsWith( "local.map", StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// A scene's map in a mounted game mounts that game itself, without a launch having done it.
	/// </summary>
	[TestMethod]
	public void SceneMapMountsItsGame()
	{
		using var game = new TestGame();
		using var testMount = RegisterTestMount();
		SyncContext.RunBlocking( Sandbox.Mounting.Directory.SetMountState( "testgame", false ) );
		Assert.IsFalse( Sandbox.Mounting.Directory.Get( "testgame" ).IsMounted );

		var scene = new Scene();
		using var cleanup = new DisposeAction( () => scene.Destroy() );
		using var sceneScope = scene.Push();
		var component = OpenMap( scene, "mount://testgame/maps/testlevel.scene" );

		Assert.IsTrue( Sandbox.Mounting.Directory.Get( "testgame" ).IsMounted );

		var info = SyncContext.RunBlocking( new MapReference( component.MapName ).GetInfoAsync() );
		Assert.IsFalse( string.IsNullOrEmpty( info.Title ) );
		Assert.IsTrue( component.GameObject.Children.Any( x => x.Name == "MountedRoot" ) );
	}

	/// <summary>
	/// A joining client needs its bundled map's assets before it can deserialize the host's scene.
	/// A host running the bundled map advertises no Workshop download for that ID.
	/// </summary>
	[TestMethod]
	public void JoiningClientMountsBundledSceneMap()
	{
		using var game = new TestGame();
		const ulong itemId = 87654;
		const string asset = "maps/bundled_join_probe.scene";

		WriteProject( game.Root, "joingame", "game", ".sbproj" );
		game.WriteMap( asset, itemId );
		game.CopyAsset( "map", asset + "_c" );
		Project.AddAddonPath( game.Root );
		Assert.IsFalse( FileSystem.Mounted.FileExists( asset + "_c" ) );

		var system = Networking.System = new NetworkSystem( "joining client", game.Dll.TypeLibrary );
		system.Connect( new TestConnection( Guid.NewGuid(), isHost: true ) );
		LaunchArguments.Map = itemId.ToString();

		Assert.IsTrue( SyncContext.RunBlocking( game.Dll.LoadGamePackageAsync( "local.joingame", GameLoadingFlags.Remote | GameLoadingFlags.Reload, default ) ) );
		game.Dll.InstallNetworkTables( system );
		Assert.IsTrue( SyncContext.RunBlocking( game.Dll.LoadNetworkTables( system ).WaitAsync( TimeSpan.FromSeconds( 10 ) ) ) );

		Assert.AreEqual( 0, game.Dll.WorkshopItems.StringTable.Entries.Count );
		Assert.IsTrue( FileSystem.Mounted.FileExists( asset + "_c" ), "The client must be able to resolve assets from its bundled map." );
	}

	static MapInstance OpenMap( Scene scene, string map = null, bool fromLaunch = false, bool expected = true )
	{
		var component = scene.CreateObject().Components.Create<MapInstance>();
		component.MapName = map;
		component.UseMapFromLaunch = fromLaunch;
		using ( CallbackBatch.Batch() ) component.OnLoadInternal();
		SyncContext.RunBlocking( scene.WaitForLoading() );
		Assert.AreEqual( expected, component.IsLoaded );
		return component;
	}

	static IDisposable RegisterTestMount()
	{
		var assembly = typeof( TestGameMount ).Assembly;
		Sandbox.Mounting.Directory.AddAssembly( assembly );
		return new DisposeAction( () => Sandbox.Mounting.Directory.RemoveAssembly( assembly ) );
	}

	sealed class TestGame : IDisposable
	{
		readonly GlobalContext.GlobalContextScope scope = GlobalContext.GameScope();
		readonly NetworkSystem previousSystem = Networking.System;
		readonly string previousMap = LaunchArguments.Map;
		internal readonly GameInstanceDll Dll = (GameInstanceDll)IGameInstanceDll.Current;
		internal readonly string Root = Directory.CreateDirectory( Path.Combine( Environment.CurrentDirectory, "core", $"test_maps_{Guid.NewGuid():N}" ) ).FullName;

		internal TestGame()
		{
			if ( GameInstanceDll.Current is null ) Dll.Bootstrap();
			Project.Clear();
		}

		internal void WriteMap( string asset, ulong workshopId )
		{
			WriteProject( Root, "map", "map", ".sbproj", configure: config =>
			{
				config.Metadata["PrimaryAsset"] = asset;
				config.Metadata["WorkshopId"] = workshopId;
			} );
		}

		internal string CopyAsset( string folder, string asset, string fixture = "unittest/addons/resources/Assets/test.scene_c" )
		{
			var assets = Path.Combine( Root, folder, "Assets" );
			var destination = Path.Combine( assets, asset );
			Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
			File.Copy( Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), fixture ), destination );
			return assets;
		}

		void IDisposable.Dispose()
		{
			Dll.CloseGame();
			LaunchArguments.Map = previousMap;
			Networking.System = previousSystem;
			Project.Clear();
			Directory.Delete( Root, true );
			scope.Dispose();
		}
	}

	static WorkshopFileSystem OpenWorkshopMap( string folder, ulong id, string asset )
	{
		Directory.CreateDirectory( folder );
		File.WriteAllText( Path.Combine( folder, WorkshopItemMetaData.FileName ), Json.Serialize( new WorkshopItemMetaData
		{
			TypeName = "map",
			Meta = Json.Serialize( new { PrimaryAsset = asset } )
		} ) );
		return SyncContext.RunBlocking( WorkshopFileSystem.OpenInstalledAsync( id, folder, default ) );
	}

	static void WriteProject( string root, string folder, string type, string filename, string org = "local", Action<ProjectConfig> configure = null )
	{
		var directory = Path.Combine( root, folder );
		Directory.CreateDirectory( Path.Combine( directory, "Code" ) );
		var config = new ProjectConfig
		{
			Title = folder,
			Org = org,
			Ident = Path.GetFileName( folder ),
			Type = type,
			Schema = 1
		};
		configure?.Invoke( config );
		File.WriteAllText( Path.Combine( directory, filename ), JsonSerializer.Serialize( config ) );
	}
}
