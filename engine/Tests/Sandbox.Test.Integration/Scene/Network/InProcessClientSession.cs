using System;
using System.Linq;
using Sandbox;
using Sandbox.Internal;
using Sandbox.Network;

namespace SceneTests;

/// <summary>
/// Exercises <see cref="InProcessClientSession"/> - the engine side of the editor's docked
/// in-process clients - through the REAL connection handshake against an in-process host:
/// ServerInfo → UserInfo → Welcome → MountVPKs → Snapshot → ClientReady → Activate.
/// </summary>
[TestClass]
public class InProcessClientSessionTests
{
	/// <summary>
	/// Pump the client session and the host until the client connects (or we give up).
	/// The session restores the host context after every tick, so host.Tick() always runs
	/// under host globals.
	/// </summary>
	static void PumpUntilConnected( InProcessClientSession session, NetworkSystem host, int maxIterations = 128 )
	{
		for ( var i = 0; i < maxIterations && !session.IsConnected; i++ )
		{
			session.Tick();
			host.Tick();
		}
	}

	[TestMethod]
	public void FullHandshake_ConnectsAndReceivesScene()
	{
		var prevSystem = Networking.System;
		var prevInstance = SceneNetworkSystem.Instance;
		var prevLocal = Connection.Local;
		var prevScene = Game.ActiveScene;

		NetworkSystem host = null;
		InProcessClientSession session = null;
		Scene hostScene = null;

		try
		{
			//
			// Host: network system + scene system + a scene containing a recognizable object
			//
			host = new NetworkSystem( "server", GlobalGameNamespace.TypeLibrary );
			Networking.System = host;
			host.InitializeHost();
			host.GameSystem = new SceneNetworkSystem( GlobalGameNamespace.TypeLibrary, host );

			hostScene = Helpers.LoadSceneFromJson( "scenes/inprocess_test.scene",
				$"{{ \"Id\": \"{Guid.NewGuid()}\", \"Name\": \"HostTestObject\", \"Enabled\": true }}" );

			Game.ActiveScene = hostScene;
			Game.IsPlaying = true;

			//
			// Client: create the in-process session; this registers the InProcessSocket on
			// the host and starts the handshake.
			//
			session = InProcessClientSession.Create( "Tenant One" );

			Assert.IsTrue( host.Sockets.OfType<InProcessSocket>().Any(), "Host should have an InProcessSocket" );

			PumpUntilConnected( session, host );

			//
			// Client side: fully connected, with its own scene built from the snapshot.
			//
			Assert.IsTrue( session.IsConnected, "Client should reach Connected state" );
			Assert.IsNotNull( session.Scene, "Client should have a scene from the snapshot" );
			Assert.AreNotEqual( hostScene, session.Scene, "Client scene must be a distinct instance" );
			Assert.IsTrue( session.Scene.Children.Any( x => x.Name == "HostTestObject" ),
				"Client scene should contain the host's object" );

			//
			// Host side: exactly one connection, fully connected, with the fake identity.
			//
			var hostSide = host.Connections.SingleOrDefault();
			Assert.IsNotNull( hostSide, "Host should have one client connection" );
			Assert.AreEqual( Connection.ChannelState.Connected, hostSide.State );
			Assert.AreEqual( "Tenant One", hostSide.Name );

			//
			// Host globals must be exactly as we left them - no tenant leakage.
			//
			Assert.AreEqual( host, Networking.System, "Networking.System leaked" );
			Assert.AreEqual( hostScene, Game.ActiveScene, "Game.ActiveScene leaked" );
			Assert.AreEqual( prevLocal, Connection.Local, "Connection.Local leaked" );

			//
			// The client keeps ticking fine while connected.
			//
			for ( var i = 0; i < 8; i++ )
			{
				session.Tick();
				host.Tick();
			}

			Assert.IsTrue( session.IsConnected );
			Assert.AreEqual( hostScene, Game.ActiveScene, "Game.ActiveScene leaked after steady-state ticks" );

			//
			// Disconnect: host cleans up the connection through the normal path.
			//
			session.Dispose();
			session = null;

			host.Tick();

			Assert.IsFalse( host.Connections.Any(), "Host should have no connections after disposal" );
			Assert.AreEqual( hostScene, Game.ActiveScene, "Game.ActiveScene leaked after disposal" );
		}
		finally
		{
			session?.Dispose();

			try { host?.Disconnect(); } catch { }

			hostScene?.Destroy();

			Networking.System = prevSystem;
			SceneNetworkSystem.Instance = prevInstance;
			Connection.Local = prevLocal;
			Game.ActiveScene = prevScene;
		}
	}

	[TestMethod]
	public void MultipleClients_AllConnectDistinctly()
	{
		var prevSystem = Networking.System;
		var prevInstance = SceneNetworkSystem.Instance;
		var prevLocal = Connection.Local;
		var prevScene = Game.ActiveScene;

		NetworkSystem host = null;
		var sessions = new System.Collections.Generic.List<InProcessClientSession>();
		Scene hostScene = null;

		try
		{
			host = new NetworkSystem( "server", GlobalGameNamespace.TypeLibrary );
			Networking.System = host;
			host.InitializeHost();
			host.GameSystem = new SceneNetworkSystem( GlobalGameNamespace.TypeLibrary, host );

			hostScene = Helpers.LoadSceneFromJson( "scenes/inprocess_multi_test.scene",
				$"{{ \"Id\": \"{Guid.NewGuid()}\", \"Name\": \"HostTestObject\", \"Enabled\": true }}" );

			Game.ActiveScene = hostScene;
			Game.IsPlaying = true;

			const int clientCount = 4;

			for ( var i = 0; i < clientCount; i++ )
			{
				sessions.Add( InProcessClientSession.Create() );
			}

			for ( var i = 0; i < 256 && !sessions.All( s => s.IsConnected ); i++ )
			{
				foreach ( var s in sessions )
					s.Tick();

				host.Tick();
			}

			Assert.IsTrue( sessions.All( s => s.IsConnected ), "All clients should connect" );
			Assert.AreEqual( clientCount, host.Connections.Count() );

			// Each client has its own distinct scene, none of them the host's.
			var scenes = sessions.Select( s => s.Scene ).ToList();
			Assert.IsTrue( scenes.All( s => s is not null && s != hostScene ) );
			Assert.AreEqual( clientCount, scenes.Distinct().Count(), "Client scenes must be distinct instances" );

			// Distinct identities on the host.
			var names = host.Connections.Select( c => c.Name ).ToList();
			Assert.AreEqual( clientCount, names.Distinct().Count(), $"Client names should be distinct: {string.Join( ", ", names )}" );

			Assert.AreEqual( hostScene, Game.ActiveScene, "Game.ActiveScene leaked" );
			Assert.AreEqual( host, Networking.System, "Networking.System leaked" );
		}
		finally
		{
			foreach ( var s in sessions )
			{
				try { s.Dispose(); } catch { }
			}

			try { host?.Disconnect(); } catch { }

			hostScene?.Destroy();

			Networking.System = prevSystem;
			SceneNetworkSystem.Instance = prevInstance;
			Connection.Local = prevLocal;
			Game.ActiveScene = prevScene;
		}
	}
}
