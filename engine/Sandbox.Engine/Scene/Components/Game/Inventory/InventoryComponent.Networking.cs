namespace Sandbox;

public partial class InventoryComponent
{
	// These are owner-only: a client can ask things of its own inventory, never anyone else's. The
	// engine enforces that (NetFlags.OwnerOnly rejects any caller that isn't this object's owner), then
	// the host applies the change - the client never acts authoritatively. (Switching predicts locally
	// through Switch/SwitchHost instead; drop/remove can't be predicted because they spawn/destroy
	// networked objects.)

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostRemove( BaseInventoryItem item ) => Remove( item );

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostDrop( BaseInventoryItem item ) => Drop( item );

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostMoveSlot( int fromSlot, int toSlot ) => MoveSlot( fromSlot, toSlot );
}
