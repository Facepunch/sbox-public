namespace Sandbox.Network;

internal sealed class InProcessConnection : Connection
{
	readonly Queue<byte[]> _inbox = new();
	readonly bool _representsHost;
	bool _closed;

	public InProcessConnection Peer { get; private set; }
	public string FakeName { get; init; }
	public SteamId FakeSteamId { get; init; }

	public override string Address => "in-process";
	public override bool IsHost => _representsHost;

	InProcessConnection( bool representsHost )
	{
		_representsHost = representsHost;
	}

	public static (InProcessConnection HostSide, InProcessConnection ClientSide) CreatePair( string fakeName, SteamId fakeSteamId )
	{
		var hostSide = new InProcessConnection( false );
		var clientSide = new InProcessConnection( true )
		{
			FakeName = fakeName,
			FakeSteamId = fakeSteamId
		};

		hostSide.Peer = clientSide;
		clientSide.Peer = hostSide;

		return (hostSide, clientSide);
	}

	internal override bool OnReceiveServerInfo( ref UserInfo userInfo, ServerInfo serverInfo )
	{
		if ( string.IsNullOrEmpty( FakeName ) )
			return true;

		userInfo.Name = FakeName;
		userInfo.SteamId = FakeSteamId;
		userInfo.InventoryBlob = null;
		userInfo.IsVr = false;

		return true;
	}

	internal override void InternalSend( byte[] data, NetFlags flags )
	{
		if ( _closed || Peer is null || Peer._closed )
			return;

		Peer._inbox.Enqueue( data );
		MessagesSent++;
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{
		var count = _inbox.Count;

		while ( count-- > 0 && !_closed && _inbox.TryDequeue( out var data ) )
		{
			OnRawPacketReceived( data, handler );
		}
	}

	internal override void InternalClose( int closeCode, string closeReason )
	{
		_closed = true;
		_inbox.Clear();
	}
}
