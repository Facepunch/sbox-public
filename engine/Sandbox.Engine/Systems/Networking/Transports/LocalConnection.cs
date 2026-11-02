namespace Sandbox.Network;

/// <summary>
/// A mock channel. Allows passing this to RPCs when they're being called locally.
/// </summary>
internal class LocalConnection : Connection
{
	public override string Address => "local";
	public override bool IsHost => Networking.System?.IsHost ?? true;

	internal override void InternalClose( int closeCode, string closeReason ) { }
	internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
	internal override void InternalSend( NetPacket packet, NetFlags flags ) { }

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
	public override bool IsHost => false;

	internal override void InternalClose( int closeCode, string closeReason ) { }
	internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
	internal override void InternalSend( NetPacket packet, NetFlags flags ) { }

	// No transport of our own, everything goes through the host
	internal override bool RoutesThroughHost => true;


	public MockConnection( Guid id )
	{
		Id = id;
	}
}
