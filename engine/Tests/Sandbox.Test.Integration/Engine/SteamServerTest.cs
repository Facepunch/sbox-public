using Sandbox.Network;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace EngineTests;

[TestClass]
public class SteamServerTest
{
	const int SharedQueryPort = 0xffff;

	[TestMethod]
	public void CreateServerAndShutdown()
	{
		SteamNative.GameServer.Initialize( 27015, SharedQueryPort, "1.0.0.0" );

		Assert.IsTrue( SteamNative.GameServer.IsAvailable(), "SteamGameServer interface is null after init - Steam redistributables may not be present" );

		SteamNative.GameServer.SetMapName( "gm_construct" );

		SteamNative.GameServer.Shutdown();
	}

	sealed class LoopbackListener : SteamNetwork.Socket
	{
		internal LoopbackListener( int port ) => InitHandle( SteamNative.NetworkingSockets.CreateListenSocketIP( port ) );
	}

	/// <summary>
	/// Per-socket Steam callbacks must reach the listener through the normal runtime pump.
	/// </summary>
	[TestMethod, TestCategory( "LiveBackend" )]
	public void RuntimePumpAcceptsLoopbackConnection()
	{
		int port;
		using ( var reservation = new UdpClient( new IPEndPoint( IPAddress.Loopback, 0 ) ) )
		{
			port = ((IPEndPoint)reservation.Client.LocalEndPoint).Port;
		}

		var previousSystem = Networking.System;
		LoopbackListener listener = null;
		HSteamNetConnection connection = default;
		SteamNative.GameServer.Initialize( 27015, SharedQueryPort, "1.0.0.0" );

		try
		{
			// The game server needs a Steam identity before it can connect to its own listener.
			SteamNative.GameServer.LogOn( null );
			Assert.IsTrue( SpinWait.SpinUntil( () =>
			{
				SteamNative.Runtime.RunCallbacks();
				return SteamNative.GameServer.IsLoggedOn();
			}, 30000 ), "The game server did not log on to Steam." );

			Networking.System = new NetworkSystem( "Steam loopback", null );
			listener = new LoopbackListener( port );
			Assert.IsTrue( listener.IsValid );

			connection = SteamNative.NetworkingSockets.ConnectByIPAddress( $"127.0.0.1:{port}" );
			Assert.AreNotEqual( 0u, connection.Id );

			Assert.IsTrue( SpinWait.SpinUntil( () =>
			{
				SteamNative.Runtime.RunCallbacks();
				return !listener.Connections.IsEmpty;
			}, 5000 ), "The runtime pump did not deliver the connection callback." );
		}
		finally
		{
			if ( connection.Id != 0 )
				SteamNative.NetworkingSockets.CloseConnection( connection, 0, "Test finished" );

			listener?.Dispose();
			Networking.System = previousSystem;
			SteamNative.GameServer.Shutdown();
		}
	}
}
