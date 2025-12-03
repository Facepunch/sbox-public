using System;
using Sandbox.Internal;
using Sandbox.Network;

namespace Sandbox.SceneTests;

internal sealed class ClientAndHost
{
	public TestConnection Client { get; }
	public TestConnection Host { get; }

	private readonly NetworkSystem _hostSystem;
	private readonly NetworkSystem _clientSystem;

	public ClientAndHost( TypeLibrary typeLibrary )
	{
		_clientSystem = new NetworkSystem( "client", typeLibrary );
		Networking.System = _clientSystem;

		Host = new TestConnection( Guid.NewGuid(), true )
		{
			State = Connection.ChannelState.Connected
		};

		Client = new TestConnection( Guid.NewGuid() )
		{
			State = Connection.ChannelState.Connected
		};

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
		_hostSystem.AddConnection( Client, remoteUserData );
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
}
