using System;
using System.Reflection;
using Sandbox.Internal;
using Sandbox.Network;

namespace SceneTests;

internal sealed class ClientAndHost : IDisposable
{
	public TestConnection Client { get; }
	public TestConnection Host { get; }

	private readonly NetworkSystem _hostSystem;
	private readonly NetworkSystem _clientSystem;

	private readonly NetworkSystem _previousNetworkSystem;
	private readonly SceneNetworkSystem _previousSceneNetworkSystem;
	private readonly Connection _previousLocalConnection;

	public ClientAndHost( TypeLibrary typeLibrary )
	{
		// This helper reassigns process-wide networking globals - capture them so
		// Dispose can put everything back and tests stay order-independent.
		_previousNetworkSystem = Networking.System;
		_previousSceneNetworkSystem = SceneNetworkSystem.Instance;
		_previousLocalConnection = Connection.Local;

		_clientSystem = new NetworkSystem( "client", typeLibrary );
		Networking.System = _clientSystem;

		Host = new TestConnection( Guid.NewGuid(), true );

		Client = new TestConnection( Guid.NewGuid() );

		var clientSceneSystem = new SceneNetworkSystem( typeLibrary, _clientSystem );
		_clientSystem.GameSystem = clientSceneSystem;
		_clientSystem.Connect( Host );

		Connection.Local = Client;
		var remoteUserData = UserInfo.Local;

		_hostSystem = new NetworkSystem( "server", typeLibrary );
		Networking.System = _hostSystem;

		var serverSceneSystem = new SceneNetworkSystem( typeLibrary, _hostSystem );
		_hostSystem.GameSystem = serverSceneSystem;
		_hostSystem.InitializeHost();
		_hostSystem.OnConnected( Client );
		_hostSystem.AddConnection( Client, remoteUserData );

		Host.State = Connection.ChannelState.Connected;
		Client.State = Connection.ChannelState.Connected;
	}

	public void BecomeClient()
	{
		Connection.Local = Client;
		Networking.System = _clientSystem;
		SceneNetworkSystem.Instance = _clientSystem.GameSystem as SceneNetworkSystem;
	}

	public void BecomeHost()
	{
		Connection.Local = Host;
		Networking.System = _hostSystem;
		SceneNetworkSystem.Instance = _hostSystem.GameSystem as SceneNetworkSystem;
	}

	public void Become( Connection connection )
	{
		if ( connection == Host ) BecomeHost();
		else if ( connection == Client ) BecomeClient();
		else throw new ArgumentOutOfRangeException( nameof( connection ) );
	}

	public void ProcessMessages()
	{
		if ( Connection.Local == Host )
		{
			ProcessMessages( Client, Host, _hostSystem );
		}
		else
		{
			ProcessMessages( Host, Client, _clientSystem );
		}
	}

	private static void ProcessMessages( TestConnection sender, TestConnection receiver, NetworkSystem receiverSystem )
	{
		// Using reflection to keep HandleIncomingMessage private

		var handleMessageMethod = typeof( NetworkSystem ).GetMethod( "HandleIncomingMessage", BindingFlags.Instance | BindingFlags.NonPublic )
			?? throw new Exception( "Unable to find private method NetworkSystem.HandleIncomingMessage needed for test." );

		// Have to create a delegate instead of MethodInfo.Invoke() because NetworkMessage is a ref struct

		var handleMessageDelegate = handleMessageMethod.CreateDelegate<Action<NetworkSystem.NetworkMessage>>( receiverSystem );

		foreach ( var message in receiver.Messages )
		{
			using var reader = ByteStream.CreateReader( message.Raw );

			handleMessageDelegate( new NetworkSystem.NetworkMessage { Source = sender, Data = reader } );
		}
	}

	/// <summary>
	/// Restores the global networking state that the constructor and the
	/// Become* helpers replaced, so nothing leaks into later tests.
	/// </summary>
	public void Dispose()
	{
		Networking.System = _previousNetworkSystem;
		SceneNetworkSystem.Instance = _previousSceneNetworkSystem;
		Connection.Local = _previousLocalConnection;
	}
}
