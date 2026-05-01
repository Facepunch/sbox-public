namespace Sandbox.Network;

/// <summary>
/// A mock channel. Allows passing this to RPCs when they're being called locally.
/// </summary>
internal class LocalConnection : Connection
{
	public override string Address => "local";
	public override string Name => "local";
	public override bool IsHost => Networking.System?.IsHost ?? true;

	internal override void InternalClose( int closeCode, string closeReason ) { }
	internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
	internal override void InternalSend( byte[] data, NetFlags flags ) { }

	public LocalConnection( Guid id )
	{
		Id = id;
	}
}

/// <summary>
/// A mock channel. Allows passing this to RPCs when they're being called locally. Mock connections
/// will also exist for other clients when connected to a dedicated server. If we try to send a message
/// to one, we'll route that message through the server instead.
/// </summary>
internal class MockConnection : Connection
{
	public override string Address => "";
	public override string Name => $"{Id}";
	public override bool IsHost => false;

	internal override void InternalClose( int closeCode, string closeReason ) { }
	internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
	internal override void InternalSend( byte[] data, NetFlags flags ) { }

	internal override void SendStream( ByteStream stream, NetFlags flags = NetFlags.Reliable )
	{
		// Route unencoded messages through the host — we don't have a direct transport.
		if ( !TryGetRoutableHost( out var host ) )
			return;

		var wrapper = new TargetedMessage
		{
			SenderId = Local.Id,
			TargetId = Id,
			Message = stream.ToArray(),
			Flags = (byte)flags
		};

		host.SendMessage( wrapper, flags );
	}

	/// <summary>
	/// Override for pre-encoded payloads (e.g. from Broadcast's single-encode path).
	/// Decodes the wire bytes and routes through the host via <see cref="TargetedInternalMessage"/>.
	/// </summary>
	internal override void Send( byte[] encoded, NetFlags flags )
	{
		if ( !TryGetRoutableHost( out var host ) )
			return;

		var decoded = Decode( encoded );

		var wrapper = new TargetedInternalMessage
		{
			SenderId = Local.Id,
			TargetId = Id,
			Data = decoded.ToArray(),
			Flags = (byte)flags
		};

		host.SendMessage( wrapper, flags );
	}

	private bool TryGetRoutableHost( out Connection host )
	{
		host = Host;

		if ( host is null or MockConnection )
		{
			if ( Networking.Debug )
				Log.Warning( $"MockConnection: no available host to route through for {this}" );

			host = null;
			return false;
		}

		return true;
	}

	public MockConnection( Guid id )
	{
		Id = id;
	}
}
