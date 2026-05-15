namespace Networking;

[TestClass]
public class RpcAttributeTests
{
	[TestMethod]
	public void Broadcast_DefaultsReliable()
	{
		var attribute = new Rpc.BroadcastAttribute();

		Assert.IsTrue( attribute.Flags.Contains( NetFlags.Reliable ) );
	}

	[TestMethod]
	public void Broadcast_HostOnlyAddsReliable()
	{
		var attribute = new Rpc.BroadcastAttribute( NetFlags.HostOnly );

		Assert.IsTrue( attribute.Flags.Contains( NetFlags.HostOnly ) );
		Assert.IsTrue( attribute.Flags.Contains( NetFlags.Reliable ) );
	}

	[TestMethod]
	public void Broadcast_UnreliableDoesNotAddReliable()
	{
		var attribute = new Rpc.BroadcastAttribute( NetFlags.OwnerOnly | NetFlags.Unreliable );

		Assert.IsTrue( attribute.Flags.Contains( NetFlags.OwnerOnly ) );
		Assert.IsTrue( attribute.Flags.Contains( NetFlags.Unreliable ) );
		Assert.IsFalse( attribute.Flags.Contains( NetFlags.Reliable ) );
	}
}
