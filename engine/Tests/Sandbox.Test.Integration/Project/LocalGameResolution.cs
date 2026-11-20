using System;
using System.IO;
using System.Text.Json;
using SceneTests;
using Sandbox.DataModel;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Tasks;

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
		using var scope = GlobalContext.GameScope();
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_join_packages_{Guid.NewGuid():N}" );
		var previousSystem = Networking.System;
		var dll = (GameInstanceDll)IGameInstanceDll.Current;
		const string localGame = "local.joinprobe";
		const string hostAddon = "LOCAL.hostonlyaddon";
		const string sandbox = "facepunch.sandbox#2147483647";

		if ( GameInstanceDll.Current is null )
			dll.Bootstrap();

		Project.Clear();

		try
		{
			WriteProject( root, "joinprobe", "game", ".sbproj" );
			Project.AddAddonPath( root );
			var system = Networking.System = new NetworkSystem( "joining client", dll.TypeLibrary );
			system.Connect( new TestConnection( Guid.NewGuid(), isHost: true ) );
			Assert.IsTrue( SyncContext.RunBlocking( dll.LoadGamePackageAsync( localGame, GameLoadingFlags.Remote | GameLoadingFlags.Reload, default ) ) );
			dll.InstallNetworkTables( system );

			// Cached metadata without downloadable files makes the required mount fail offline.
			if ( missingRequired )
				Package.Cache( new RemotePackage { Org = new Package.Organization { Ident = "facepunch" }, Ident = "sandbox" }, false, int.MaxValue );

			var table = dll.ServerPackages.StringTable;
			var ready = new ServerPackages.PackageInfo { ResourcesReady = true };
			table.Set( localGame, liveUpdate ? default : ready );

			// With a live update the registered game keeps the join waiting until the host marks it ready.
			var join = liveUpdate ? dll.LoadNetworkTables( system ) : null;
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
				join = dll.LoadNetworkTables( system );
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
			}
		}
		finally
		{
			dll.CloseGame();
			Networking.System = previousSystem;
			if ( missingRequired ) Package.ClearCache( sandbox );
			Project.Clear();
			Directory.Delete( root, true );
		}
	}

	static void WriteProject( string root, string folder, string type, string filename, string org = "local" )
	{
		var directory = Path.Combine( root, folder );
		Directory.CreateDirectory( Path.Combine( directory, "Code" ) );
		File.WriteAllText( Path.Combine( directory, filename ), JsonSerializer.Serialize( new ProjectConfig
		{
			Title = folder,
			Org = org,
			Ident = Path.GetFileName( folder ),
			Type = type,
			Schema = 1
		} ) );
	}
}
