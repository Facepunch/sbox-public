namespace Sandbox.Network;

/// <summary>
/// An empty connection that can be used for testing and optimizing processing.
/// Messages can not actually be sent to it, and it won't receive any either. Other clients
/// will be unaware of it.
/// </summary>
internal class EmptyConnection : Connection
{
	public override string Address => "empty";
	public override string Name => "empty";
	public override bool IsHost => false;

	internal override void InternalClose( int closeCode, string closeReason ) { }
	internal override void InternalRecv( NetworkSystem.MessageHandler handler ) { }
	internal override void InternalSend( byte[] data, NetFlags flags ) { }

	public EmptyConnection( Guid id )
	{
		Id = id;
	}
}
