using System;
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
