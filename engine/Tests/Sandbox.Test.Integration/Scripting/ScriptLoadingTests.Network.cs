using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Individual in-memory edits preserve resource scripts, publish accepted source, and never need a disk file.
	/// </summary>
	[TestMethod]
	public void ScriptTablePublishesInMemoryFiles()
	{
		files.WriteAllText( "disk.csx", "int diskCount = 0; int NextDiskCount() { return ++diskCount; } class DiskScript {}" );
		ResourceLoader.LoadAllGameResource( files );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return NextDiskCount();" ) );
		var table = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( table, isClient: false );
		const string path = "dynamic/shared.csx";
		const string source = "[Global] int InjectedStarts = 0; InjectedStarts++; class InjectedScript { public int Read() { return 3; } }";

		Assert.IsTrue( Game.Scripting.LoadFile( path, source ).Success );
		Assert.IsFalse( files.FileExists( path ) );
		Assert.AreEqual( source, table.Entries[path].ReadAsString() );
		Assert.AreEqual( 3, Game.Scripting.Run<int>( "return new InjectedScript().Read();" ) );
		Assert.AreEqual( 2, Game.Scripting.Run<int>( "return NextDiskCount();" ), "Adding source must preserve other scripts' state." );
		Assert.IsTrue( table.Entries.ContainsKey( "disk.csx" ) );

		table.ClearChanges();
		Assert.IsTrue( Game.Scripting.LoadFile( path, source ).Success );
		Assert.IsFalse( table.HasChanged );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "InjectedStarts" ) );

		ResourceLoader.LoadAllGameResource( files );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "InjectedScript" ), "Resource scans must retain in-memory scripts." );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "InjectedStarts" ) );
		table.ClearChanges();

		Assert.IsFalse( Game.Scripting.LoadFile( path, "class Broken { public int = ; }" ).Success );
		Assert.IsFalse( table.HasChanged );
		Assert.AreEqual( source, table.Entries[path].ReadAsString() );
		Assert.AreEqual( 3, Game.Scripting.Run<int>( "return new InjectedScript().Read();" ) );

		Assert.IsTrue( Game.Scripting.LoadFile( path, source.Replace( "return 3;", "return 7;" ) ).Success );
		Assert.AreEqual( 7, Game.Scripting.Run<int>( "return new InjectedScript().Read();" ) );
		StringAssert.Contains( table.Entries[path].ReadAsString(), "return 7;" );
		Assert.IsTrue( Game.Scripting.LoadFile( "dynamic/private.server.csx", "class InjectedPrivate {}" ).Success );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "InjectedPrivate" ) );
		Assert.IsFalse( table.Entries.ContainsKey( "dynamic/private.server.csx" ) );

		Assert.IsTrue( Game.Scripting.RemoveFile( path ).Success );
		Assert.IsNull( Game.TypeLibrary.GetType( "InjectedScript" ) );
		Assert.IsFalse( table.Entries.ContainsKey( path ) );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "DiskScript" ) );
		table.ClearChanges();
		Assert.IsTrue( Game.Scripting.RemoveFile( path ).Success );
		Assert.IsFalse( table.HasChanged );
	}

	/// <summary>
	/// Source injected before hosting reaches joining clients, which cannot replace or remove the host's scripts.
	/// </summary>
	[TestMethod]
	public void ScriptTableInMemoryFilesRunOnJoiningClient()
	{
		const string path = "dynamic/join.csx";
		const string source = "[Global] int JoinedStarts = 0; JoinedStarts++; class InjectedJoin {}";
		Assert.IsTrue( Game.Scripting.LoadFile( path, source ).Success );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "JoinedStarts" ) );
		var host = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( host, isClient: false );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "JoinedStarts" ), "Hosting must not execute the injected script twice." );

		context.Reset();
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "InjectedJoin" ) );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "JoinedStarts" ) );

		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.LoadFile( path, "class ClientReplacement {}" ) );
		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.RemoveFile( path ) );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "InjectedJoin" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "ClientReplacement" ) );
		Assert.AreEqual( source, client.Entries[path].ReadAsString() );
	}

	/// <summary>
	/// Client batch edits cannot replace or remove host declarations, or execute local server-only source.
	/// </summary>
	[TestMethod]
	public void ScriptTableRejectsClientBatchEdits()
	{
		var host = new StringTable( "Scripts", true );
		host.SetString( "shared.csx", "class HostOwnedScript { public int Read() { return 17; } }" );
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		var original = Game.TypeLibrary.GetType( "HostOwnedScript" );
		Game.Scripting.Globals.Set( "RejectedStarts", 0 );

		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.LoadFiles( new Dictionary<string, string>
		{
			["private.server.csx"] = "[Global] int RejectedStarts = 0; RejectedStarts++; class ClientOwnedScript {}"
		} ) );
		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.LoadFiles( new Dictionary<string, string>() ) );
		Assert.AreSame( original, Game.TypeLibrary.GetType( "HostOwnedScript" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "ClientOwnedScript" ) );
		Assert.AreEqual( 0, Game.Scripting.Globals.Get<int>( "RejectedStarts" ) );
		Assert.AreEqual( 17, Game.Scripting.Run<int>( "return new HostOwnedScript().Read();" ) );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );

		Game.Scripting.InitializeNetworking( client, isClient: false );
		Assert.IsTrue( Game.Scripting.LoadFile( "shared.csx", "class HostOwnedScript { public int Read() { return 42; } }" ).Success );
		Assert.AreEqual( 42, Game.Scripting.Run<int>( "return new HostOwnedScript().Read();" ) );
		StringAssert.Contains( client.Entries["shared.csx"].ReadAsString(), "return 42;" );
	}

	/// <summary>
	/// An empty client cannot publish anonymous declarations while waiting for the host's first snapshot.
	/// </summary>
	[TestMethod]
	public void ScriptTableRejectsClientAnonymousDeclarations()
	{
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		Game.Scripting.Globals.Set( "RejectedStarts", 0 );

		Assert.ThrowsException<InvalidOperationException>( () => Game.Scripting.Load(
			"[Global] int RejectedStarts = 0; RejectedStarts++; class ClientOwnedScript {}" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "ClientOwnedScript" ) );
		Assert.AreEqual( 0, Game.Scripting.Globals.Get<int>( "RejectedStarts" ) );

		var host = new StringTable( "Scripts", true );
		host.SetString( "shared.csx", "class HostOwnedScript {}" );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "HostOwnedScript" ) );
		Assert.AreEqual( 42, Game.Scripting.CreateScript( "return 42;" ).RunAndReturn<int>() );
	}

	/// <summary>
	/// Hosting after scripts have loaded seeds only shared source; rejected edits retain the last accepted version.
	/// </summary>
	[TestMethod]
	public void ScriptTablePublishesAcceptedSharedFiles()
	{
		var sources = new Dictionary<string, string>
		{
			["shared.csx"] = "class NetworkShared { public int Value() { return 3; } }",
			["private.server.csx"] = "class NetworkPrivate {}",
			["private.SERVER.CSX"] = "class NetworkPrivateUpper {}",
			["other.txt"] = "class NetworkOther {}"
		};
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		var table = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( table, isClient: false );
		CollectionAssert.AreEquivalent( new[] { "shared.csx" }, table.Entries.Keys.ToArray() );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "NetworkPrivate" ), "Server scripts must still run on the host." );
		table.ClearChanges();

		sources["shared.csx"] = "class Broken { public int = ; }";
		Assert.IsFalse( Game.Scripting.LoadFiles( sources ).Success );
		Assert.IsFalse( table.HasChanged );
		StringAssert.Contains( table.Entries["shared.csx"].ReadAsString(), "return 3;" );

		sources["shared.csx"] = "class NetworkShared { public int Value() { return 7; } }";
		sources["new.CSX"] = "class NetworkNew {}";
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		Assert.IsTrue( table.HasChanged );
		StringAssert.Contains( table.Entries["shared.csx"].ReadAsString(), "return 7;" );
		Assert.IsTrue( table.Entries.ContainsKey( "new.CSX" ) );
		table.ClearChanges();
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		Assert.IsFalse( table.HasChanged );

		sources["shared.server.csx"] = sources["shared.csx"];
		sources.Remove( "shared.csx" );
		Assert.IsTrue( Game.Scripting.LoadFiles( sources ).Success );
		Assert.IsFalse( table.Entries.ContainsKey( "shared.csx" ) );
		Assert.IsFalse( table.Entries.ContainsKey( "shared.server.csx" ) );
	}

	/// <summary>
	/// A joining client waits for dependencies, then publishes the snapshot and live additions, edits and removals in batches.
	/// </summary>
	[TestMethod]
	public void ScriptTableSnapshotAndLiveUpdatesExecuteCoherentBatches()
	{
		var host = new StringTable( "Scripts", true );
		host.SetString( "a-caller.csx", "[Global] int NetworkValue = 0; NetworkValue = NetworkScriptHelper.Old(); class NetworkCaller { public int Read() { return NetworkScriptHelper.Old(); } }" );
		host.SetString( "z-helper.csx", "class NetworkScriptHelper { public static int Old() { return 17; } }" );
		host.SetString( "state.csx", "int count = 0; int NextNetworkCount() { return ++count; }" );
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsNull( Game.TypeLibrary.GetType( "NetworkCaller" ), "Receiving the snapshot must not execute before assemblies load." );

		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success, Game.Scripting.LastError?.ToString() );
		Assert.AreEqual( 17, Game.Scripting.Globals.Get<int>( "NetworkValue" ) );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return NextNetworkCount();" ) );
		client.PostNetworkUpdate = () => Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success, Game.Scripting.LastError?.ToString() );

		host.SetString( "a-caller.csx", "[Global] int NetworkValue = 0; NetworkValue = NetworkScriptHelper.New(); class NetworkCaller { public int Read() { return NetworkScriptHelper.New(); } }" );
		host.SetString( "z-helper.csx", "class NetworkScriptHelper { public static int New() { return 42; } }" );
		host.SetString( "added.csx", "class NetworkAdded {}" );
		ExchangeScripts( host, client );
		Assert.AreEqual( 42, Game.Scripting.Globals.Get<int>( "NetworkValue" ) );
		Assert.AreEqual( 42, Game.Scripting.Run<int>( "return new NetworkCaller().Read();" ) );
		Assert.AreEqual( 2, Game.Scripting.Run<int>( "return NextNetworkCount();" ) );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "NetworkAdded" ) );

		host.Remove( "a-caller.csx" );
		host.Remove( "z-helper.csx" );
		ExchangeScripts( host, client );
		Assert.IsNull( Game.TypeLibrary.GetType( "NetworkCaller" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "NetworkScriptHelper" ) );
		Assert.AreEqual( 3, Game.Scripting.Run<int>( "return NextNetworkCount();" ) );
	}

	/// <summary>
	/// Local packages and file watchers cannot replace host source or execute server-only scripts on a client.
	/// </summary>
	[TestMethod]
	public void ScriptTableOwnsClientExecution()
	{
		var host = new StringTable( "Scripts", true );
		host.SetString( "shared.csx", "[Global] int NetworkStarts = 0; NetworkStarts++; class HostScript {}" );
		host.SetString( "private.SERVER.csx", "class PrivateClientScript {}" );
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		Assert.IsNull( Game.TypeLibrary.GetType( "PrivateClientScript" ) );

		files.WriteAllText( "shared.csx", "class LocalScript {}" );
		files.WriteAllText( "private.server.csx", "class PrivateLocalScript {}" );
		ResourceLoader.LoadAllGameResource( files );
		DispatchResourceChanges( "shared.csx", "private.server.csx" );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "HostScript" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "LocalScript" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "PrivateLocalScript" ) );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "NetworkStarts" ) );

		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "NetworkStarts" ) );
	}

	/// <summary>
	/// Promotion retains shared definitions without starting server-only scripts found in local packages or watchers.
	/// </summary>
	[TestMethod]
	public void ScriptTablePromotionKeepsServerScriptsOnTheirOriginalHost()
	{
		var host = new StringTable( "Scripts", true );
		host.SetString( "shared.csx", "class SharedBeforePromotion {}" );
		var client = new StringTable( "Scripts", true );
		Game.Scripting.InitializeNetworking( client, isClient: true );
		ExchangeScripts( host, client, snapshot: true );
		Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
		Game.Scripting.InitializeNetworking( client, isClient: false );

		files.WriteAllText( "private.server.csx", "class OriginalHostOnlyScript {}" );
		files.WriteAllText( "added.csx", "class SharedAfterPromotion {}" );
		ResourceLoader.LoadAllGameResource( files );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "SharedBeforePromotion" ) );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "SharedAfterPromotion" ) );
		Assert.IsNull( Game.TypeLibrary.GetType( "OriginalHostOnlyScript" ) );

		files.WriteAllText( "private.server.csx", "class OriginalHostOnlyEditedScript {}" );
		DispatchResourceChanges( "private.server.csx" );
		Assert.IsNull( Game.TypeLibrary.GetType( "OriginalHostOnlyEditedScript" ) );
		CollectionAssert.AreEquivalent( new[] { "shared.csx", "added.csx" }, client.Entries.Keys.ToArray() );
	}

	/// <summary>
	/// A restarted handshake keeps its registered table while replacing the disposed scripting context.
	/// </summary>
	[TestMethod]
	public void ScriptTableSurvivesScriptingReset()
	{
		var table = new StringTable( "Scripts", true );
		var system = new NetworkSystem( "script-reconnect", Game.TypeLibrary );
		var host = new StringTable( "Scripts", true );
		var previousSystem = Networking.System;
		try
		{
			Networking.System = null;
			var source = new LocalConnection( Guid.NewGuid() );
			Game.Scripting.InitializeNetworking( table, isClient: true );
			system.InstallTable( table );
			var previousScripts = Game.Scripting;
			context.Reset();
			table.Reset();
			Game.Scripting.InitializeNetworking( table, isClient: true );
			system.InstallTable( table );
			Assert.AreNotSame( previousScripts, Game.Scripting );

			host.SetString( "after-reset.csx", "class AfterNetworkReset {}" );
			var writer = ByteStream.Create( 1024 );
			try
			{
				writer.Write( InternalMessageType.TableSnapshot );
				writer.Write( host.Name );
				host.BuildSnapshotMessage( ref writer );
				using var reader = ByteStream.CreateReader( writer.ToArray() );
				system.HandleIncomingMessage( new NetworkSystem.NetworkMessage { Source = source, Data = reader } );
			}
			finally
			{
				writer.Dispose();
			}

			Assert.IsTrue( Game.Scripting.LoadNetworkFiles().Success );
			Assert.IsNotNull( Game.TypeLibrary.GetType( "AfterNetworkReset" ) );
		}
		finally
		{
			Networking.System = previousSystem;
		}
	}

	/// <summary>
	/// An assigned package filesystem does not make an in-flight install ready for dependent scripts.
	/// </summary>
	[TestMethod]
	public void ScriptTablePackageWaitsForInFlightInstall()
	{
		var activePackage = CreateNetworkTestPackage();
		var download = new ServerPackages.PackageDownload { activePackage = activePackage, IsDownloading = true };
		AsyncPump.RunSynchronously( async () =>
		{
			var pending = download.DownloadAndMount( default, reloadResources: false );
			Assert.IsFalse( pending.IsCompleted, "Dependent scripts must wait for the owning install to finish." );
			download.IsDownloading = false;
			Assert.AreSame( files, await pending );
		} );
	}

	/// <summary>
	/// Deferred package resources keep remote ownership when loaded after the script snapshot.
	/// </summary>
	[TestMethod]
	public void ScriptTableDeferredResourcesPreservePackage()
	{
		files.WriteAllBytes( "tests/network-package.surface_c", EngineFileSystem.CoreContent.ReadAllBytes( "surfaces/default.surface_c" ).ToArray() );
		var activePackage = CreateNetworkTestPackage();
		var download = new ServerPackages.PackageDownload { activePackage = activePackage };
		Assert.IsNull( ResourceLibrary.Get<Surface>( "tests/network-package.surface" ) );
		AsyncPump.RunSynchronously( () => download.LoadResources() );
		var resource = ResourceLibrary.Get<Surface>( "tests/network-package.surface" );
		Assert.IsNotNull( resource );
		Assert.AreSame( activePackage.Package, resource.Package );
		Assert.IsTrue( resource.IsRemote );

		ResourceLoader.LoadAllGameResource( files, reloadExisting: true );
		Assert.AreSame( activePackage.Package, resource.Package, "The final aggregate scan must retain package attribution." );
	}

	/// <summary>
	/// Represents an already-downloaded package without contacting the backend.
	/// </summary>
	private PackageManager.ActivePackage CreateNetworkTestPackage()
	{
		var package = new PackageManager.ActivePackage();
		typeof( PackageManager.ActivePackage ).GetProperty( nameof( package.FileSystem ) ).SetValue( package, files );
		typeof( PackageManager.ActivePackage ).GetProperty( nameof( package.Package ) ).SetValue( package, new RemotePackage { Title = "Network test" } );
		return package;
	}

	/// <summary>
	/// Exercises the same serialized snapshot and update messages used by the network system.
	/// </summary>
	private static void ExchangeScripts( StringTable host, StringTable client, bool snapshot = false )
	{
		var writer = ByteStream.Create( 1024 );
		try
		{
			if ( snapshot )
			{
				host.BuildSnapshotMessage( ref writer );
			}
			else
			{
				host.BuildUpdateMessage( ref writer );
			}

			using var reader = ByteStream.CreateReader( writer.ToArray() );
			if ( snapshot )
			{
				client.ReadSnapshot( reader );
			}
			else
			{
				client.ReadUpdate( reader );
			}

			host.ClearChanges();
		}
		finally
		{
			writer.Dispose();
		}
	}
}
