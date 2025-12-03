using System;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.SceneTests;

namespace GameObjects;

using static GlobalGameNamespace;

[TestClass]
public class NetworkTests
{
	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = new Sandbox.Internal.TypeLibrary();
		Game.TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( NetworkTestComponent ).Assembly, false );

		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void NetworkedInput()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var clientAndHost = new ClientAndHost( TypeLibrary );

		// Become the client
		clientAndHost.BecomeClient();

		var inputSettings = new InputSettings();
		inputSettings.InitDefault();

		Input.InputSettings = inputSettings;
		Input.SetAction( "Jump", true );

		// Send a client tick - this will build a user command as well
		Game.ActiveScene.SendClientTick( SceneNetworkSystem.Instance );

		// Become the host
		clientAndHost.BecomeHost();

		clientAndHost.Host.ProcessMessages( InternalMessageType.ClientTick, bs =>
		{
			Networking.System.OnReceiveClientTick( bs, clientAndHost.Client );
		} );

		clientAndHost.Client.Messages.Clear();

		Assert.AreEqual( true, clientAndHost.Client.Pressed( "Jump" ) );
		Assert.AreEqual( true, clientAndHost.Client.Down( "Jump" ) );

		// Become the client
		clientAndHost.BecomeClient();

		Input.ClearActions();

		// Send a client tick - this will build a user command as well
		Game.ActiveScene.SendClientTick( SceneNetworkSystem.Instance );

		// Become the host
		clientAndHost.BecomeHost();

		clientAndHost.Host.ProcessMessages( InternalMessageType.ClientTick, bs =>
		{
			Networking.System.OnReceiveClientTick( bs, clientAndHost.Client );
		} );

		clientAndHost.Host.Messages.Clear();

		Assert.AreEqual( true, clientAndHost.Client.Released( "Jump" ) );
		Assert.AreEqual( false, clientAndHost.Client.Down( "Jump" ) );

		// Let's test wrap-aware command number processing
		var userCommand = new UserCommand( uint.MaxValue );

		clientAndHost.Client.Input.ApplyUserCommand( userCommand );

		// Become the client
		clientAndHost.BecomeClient();

		Input.SetAction( "Jump", true );

		Assert.AreEqual( true, Connection.Local.Pressed( "Jump" ) );
		Assert.AreEqual( true, Connection.Local.Down( "Jump" ) );

		// Send a client tick - this will build a user command as well
		Game.ActiveScene.SendClientTick( SceneNetworkSystem.Instance );

		// Become the host
		clientAndHost.BecomeHost();

		clientAndHost.Host.ProcessMessages( InternalMessageType.ClientTick, bs =>
		{
			Networking.System.OnReceiveClientTick( bs, clientAndHost.Client );
		} );

		Assert.AreEqual( false, clientAndHost.Client.Pressed( "Forward" ) );
		Assert.AreEqual( true, clientAndHost.Client.Pressed( "Jump" ) );
		Assert.AreEqual( true, clientAndHost.Client.Down( "Jump" ) );

		Input.ClearActions();
		Input.SetAction( "Forward", true );

		Assert.AreEqual( true, Connection.Local.Pressed( "Forward" ) );
		Assert.AreEqual( true, Connection.Local.Down( "Forward" ) );
	}

	[TestMethod]
	public void RegisterSyncProps()
	{
		Assert.IsNotNull( Game.TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var testComponentType = Game.TypeLibrary.GetType<NetworkTestComponent>();
		Assert.IsNotNull( testComponentType );

		var testSyncPropertyType = testComponentType.GetProperty( "SyncInt" );
		Assert.IsNotNull( testSyncPropertyType );

		var testPropertyId = testSyncPropertyType.Identity;

		var go = new GameObject();
		var comp1 = go.Components.Create<NetworkTestComponent>();
		comp1.SyncInt = 1;

		var prop1Id = NetworkObject.GetPropertySlot( testPropertyId, comp1.Id );

		var go2 = new GameObject();
		go2.Parent = go;
		var comp2 = go2.Components.Create<NetworkTestComponent>();
		comp2.SyncInt = 2;

		var prop2Id = NetworkObject.GetPropertySlot( testPropertyId, comp2.Id );

		var go3 = new GameObject();
		go3.Parent = go2;
		var comp3 = go3.Components.Create<NetworkTestComponent>();
		comp3.SyncInt = 3;

		var prop3Id = NetworkObject.GetPropertySlot( testPropertyId, comp3.Id );

		go.NetworkSpawn();

		Assert.IsTrue( go._net.dataTable.IsRegistered( prop1Id ) );
		Assert.IsTrue( go._net.dataTable.IsRegistered( prop2Id ) );
		Assert.IsTrue( go._net.dataTable.IsRegistered( prop3Id ) );

		Assert.AreEqual( 1, comp1.SyncInt );
		Assert.AreEqual( 2, comp2.SyncInt );
		Assert.AreEqual( 3, comp3.SyncInt );
	}

	[TestMethod]
	public void NetworkRefreshWithParentChangeHasCorrectPosition()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var client = new NetworkSystem( "client", TypeLibrary );
		Networking.System = client;

		var sceneSystem = new SceneNetworkSystem( TypeLibrary, client );
		client.GameSystem = sceneSystem;

		var client1 = new MockConnection( Guid.NewGuid() );
		var client2 = new MockConnection( Guid.NewGuid() );

		Connection.Local = client1;

		var go1 = new GameObject();
		var go2 = new GameObject( go1 )
		{
			WorldPosition = new Vector3( 100f, 100f, 100f )
		};

		go1.NetworkSpawn( Connection.Local );

		var go3 = new GameObject();
		go3.NetworkSpawn( Connection.Local );

		go3.Parent = go2;

		var refreshMsg = go3._net.GetRefreshMessage();

		Connection.Local = client2;

		// Reset the transform to default as it would be when client first constructs it
		go3.SetParentFromNetwork( null );

		// Now simulate the refresh message from the owner
		go3._net.OnRefreshMessage( client1, refreshMsg );

		Assert.AreEqual( go2, go3.Parent );
		Assert.AreEqual( go2.WorldPosition, go3.WorldPosition );
		Assert.AreEqual( Vector3.Zero, go3.LocalPosition );
	}

	[TestMethod]
	public void RemoteObjectParentToSceneKeepsTransform()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var client = new NetworkSystem( "client", TypeLibrary );
		Networking.System = client;

		var sceneSystem = new SceneNetworkSystem( TypeLibrary, client );
		client.GameSystem = sceneSystem;

		var client1 = new MockConnection( Guid.NewGuid() );
		var client2 = new MockConnection( Guid.NewGuid() );

		Connection.Local = client1;

		var go1 = new GameObject();
		var go2 = new GameObject( go1 )
		{
			WorldPosition = new Vector3( 100f, 100f, 100f )
		};

		go1.NetworkSpawn( Connection.Local );

		var go3 = new GameObject( go2 );
		go3.NetworkSpawn( Connection.Local );

		Connection.Local = client2;

		// Receive a parent message from the network
		go3.SetParentFromNetwork( null, true );

		Assert.AreEqual( go2.WorldPosition, go3.WorldPosition );
	}

	[TestMethod]
	public void RemoteObjectChildSpawnShouldHaveCorrectTransform()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var client = new NetworkSystem( "client", TypeLibrary );
		Networking.System = client;

		var sceneSystem = new SceneNetworkSystem( TypeLibrary, client );
		client.GameSystem = sceneSystem;

		var client1 = new MockConnection( Guid.NewGuid() );
		var client2 = new MockConnection( Guid.NewGuid() );

		Connection.Local = client1;

		var go1 = new GameObject();
		var go2 = new GameObject( go1 )
		{
			WorldPosition = new Vector3( 100f, 100f, 100f )
		};

		go1.NetworkSpawn( Connection.Local );

		var go3 = new GameObject( go2 );
		go3.NetworkSpawn( Connection.Local );

		var createMsg = go3._net.GetCreateMessage();

		Connection.Local = client2;

		// Reset the transform to default as it would be when client first constructs it
		go3.SetParentFromNetwork( null );

		// Now simulate the creation message from the owner
		go3._net.OnCreateMessage( createMsg );

		Assert.AreEqual( go2.WorldPosition, go3.WorldPosition );
		Assert.AreEqual( Vector3.Zero, go3.LocalPosition );
	}

	[TestMethod]
	public void HostCanParentToAnything()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var server = new NetworkSystem( "server", TypeLibrary );
		server.InitializeHost();

		Networking.System = server;
		server.GameSystem = new SceneNetworkSystem( TypeLibrary, server );

		var go = new GameObject();
		go.NetworkSpawn( Connection.Local );

		var go2 = new GameObject();
		go2.NetworkSpawn( new MockConnection( Guid.NewGuid() ) );

		var go3 = new GameObject();
		go3.NetworkSpawn( Connection.Local );

		// We should be able to parent to go3 because we're the host.
		go.Parent = go3;
		Assert.AreEqual( go3, go.Parent );

		// We should be able to parent to go2, even though we don't own it, because we're the host.
		go.Parent = go2;
		Assert.AreEqual( go2, go.Parent );
	}

	[TestMethod]
	public void NetworkChildShouldReplicateWhenParentIsDisabled()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var client = new NetworkSystem( "client", TypeLibrary );
		Networking.System = client;

		var sceneSystem = new SceneNetworkSystem( TypeLibrary, client );
		client.GameSystem = sceneSystem;

		var parentObject = new GameObject
		{
			NetworkMode = NetworkMode.Object
		};

		var childObject = new GameObject( parentObject )
		{
			NetworkMode = NetworkMode.Object
		};

		parentObject.NetworkSpawn( new NetworkSpawnOptions
		{
			StartEnabled = false
		} );

		Assert.IsFalse( parentObject.Enabled );
		Assert.IsTrue( childObject.Enabled );

		Assert.IsNotNull( childObject._net );
	}

	[TestMethod]
	public void SnapshotVersionBlocksOldSnapshots()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var client = new NetworkSystem( "client", TypeLibrary );
		Networking.System = client;

		var sceneSystem = new SceneNetworkSystem( TypeLibrary, client );
		client.GameSystem = sceneSystem;

		var client1 = new MockConnection( Guid.NewGuid() );
		var client2 = new MockConnection( Guid.NewGuid() );

		// Become client1
		Connection.Local = client1;

		var go = new GameObject();
		go.Network.SetOwnerTransfer( OwnerTransfer.Takeover );

		// Disable interpolation for this test to help prove the bug, because
		// otherwise when we test the position later, it'll be in an interpolation
		// buffer
		go.Network.DisableInterpolation();

		go.NetworkSpawn( client2 );

		var go2 = new GameObject();
		IDeltaSnapshot networkObject = go._net;

		// Become client2
		Connection.Local = client2;

		// client2 now owns it, let's have it record a snapshot in this state
		var state = networkObject.WriteSnapshotState();
		var snapshot = new DeltaSnapshot();
		snapshot.CopyFrom( networkObject, state, 2 );

		// Become client1 again
		Connection.Local = client1;

		// Assume control of the network object
		go.Network.TakeOwnership();

		// Change its parent and set position
		go.Parent = go2;
		go.WorldPosition = new Vector3( 0f, 0f, 100f );

		// Drop control of the object, give it back to client2
		go.Network.AssignOwnership( client2 );

		// Now we'll process that old snapshot from client2
		using ( var reader = ByteStream.CreateReader( SerializeSnapshot( snapshot ) ) )
		{
			sceneSystem.DeltaSnapshots.OnDeltaSnapshot( client2, reader );
		}

		// These should be equal, because the old snapshot did NOT apply
		Assert.AreEqual( new Vector3( 0f, 0f, 100f ), go.WorldPosition );
	}

	byte[] SerializeSnapshot( DeltaSnapshot snapshot )
	{
		using var writer = new ByteStream( DeltaSnapshotCluster.MaxSize * 4 );

		writer.Write( snapshot.ObjectId );
		writer.Write( snapshot.Version );
		writer.Write( snapshot.SnapshotId );
		writer.Write( (ushort)snapshot.Entries.Count );

		foreach ( var entry in snapshot.Entries )
		{
			writer.Write( entry.Slot );
			writer.WriteArray( entry.Value );
		}

		return writer.ToArray();
	}

	[TestMethod]
	public void ClientCanOnlyParentToObjectsTheyOwn()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var server = new NetworkSystem( "client", TypeLibrary );
		Networking.System = server;
		server.GameSystem = new SceneNetworkSystem( TypeLibrary, server );

		var go = new GameObject();
		go.NetworkSpawn( Connection.Local );

		var go2 = new GameObject();
		go2.NetworkSpawn( new MockConnection( Guid.NewGuid() ) );

		var go3 = new GameObject();
		go3.NetworkSpawn( Connection.Local );

		// We should be able to parent to go3 because we own it also.
		go.Parent = go3;
		Assert.AreEqual( go3, go.Parent );

		// We should still be equal to go3, because we don't own go2.
		go.Parent = go2;
		Assert.AreEqual( go3, go.Parent );
	}

	[TestMethod]
	public void ObjectRefreshRegister()
	{
		Assert.IsNotNull( TypeLibrary.GetType<ModelRenderer>(), "TypeLibrary hasn't been given the game assembly" );

		using var scope = new Scene().Push();

		var testComponentType = TypeLibrary.GetType<NetworkTestComponent>();
		Assert.IsNotNull( testComponentType );

		var testSyncPropertyType = testComponentType.GetProperty( "SyncInt" );
		Assert.IsNotNull( testSyncPropertyType );

		var testPropertyId = testSyncPropertyType.Identity;

		var go = new GameObject();
		var comp1 = go.Components.Create<NetworkTestComponent>();
		comp1.SyncInt = 1;

		var prop1Id = NetworkObject.GetPropertySlot( testPropertyId, comp1.Id );

		go.NetworkSpawn();

		var go2 = new GameObject();
		go2.Parent = go;
		var comp2 = go2.Components.Create<NetworkTestComponent>();
		comp2.SyncInt = 2;

		var prop2Id = NetworkObject.GetPropertySlot( testPropertyId, comp2.Id );

		Assert.IsTrue( go._net.dataTable.IsRegistered( prop1Id ) );
		Assert.IsFalse( go._net.dataTable.IsRegistered( prop2Id ) );

		go.Network.Refresh();

		Assert.IsTrue( go._net.dataTable.IsRegistered( prop2Id ) );

		Assert.AreEqual( 1, comp1.SyncInt );
		Assert.AreEqual( 2, comp2.SyncInt );
	}

	/// <summary>
	/// When loading a scene with networked objects, those objects must not emit <see cref="ObjectCreateMsg"/>
	/// inside a <see cref="SceneNetworkSystem.SuppressSpawnMessages"/> scope.
	/// </summary>
	[TestMethod]
	public void TestSuppressSpawnMessages()
	{
		using var testSystem = Helpers.InitializeHostWithTestConnection();
		using var _ = SceneNetworkSystem.SuppressSpawnMessages();

		// Scene contains a networked game object

		Helpers.LoadSceneFromJson( "example.scene",
			"""
			{
				"__guid": "86b89011-9646-4ee7-ad30-c0e11d258674",
				"Name": "Networked Object",
				"Enabled": true,
				"NetworkMode": 1
			}
			""" );

		Assert.AreEqual( 0, testSystem.GetMessageCount<ObjectCreateMsg>() );
	}

	private class NetworkTestComponent : Component
	{
		[Sync] public int SyncInt { get; set; }
	}
}
