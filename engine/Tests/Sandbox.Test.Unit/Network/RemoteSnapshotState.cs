using System;
using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class RemoteSnapshotStateTest
{
	[TestMethod]
	public void NewerSnapshotIsAccepted()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };

		Assert.IsTrue( state.Accept( 1, 1 ) );
		Assert.IsTrue( state.Accept( 1, 2 ) );
	}

	[TestMethod]
	public void OlderSnapshotArrivingLateIsIgnored()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };

		Assert.IsTrue( state.Accept( 1, 2 ) );
		Assert.IsFalse( state.Accept( 1, 1 ) );
		Assert.IsFalse( state.Accept( 1, 2 ) );

		// Other slots are tracked separately
		Assert.IsTrue( state.Accept( 2, 1 ) );
	}

	[TestMethod]
	public void SnapshotIdsWrapAround()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };

		Assert.IsTrue( state.Accept( 1, uint.MaxValue ) );
		Assert.IsTrue( state.Accept( 1, 0 ) );
		Assert.IsFalse( state.Accept( 1, uint.MaxValue ) );
	}

	/// <summary>
	/// Ids used to be 16 bits, so a slot that didn't change for 65536 snapshots got a new value with its old id.
	/// </summary>
	[TestMethod]
	public void LongUnchangedSlotsStillUpdate()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };

		Assert.IsTrue( state.Accept( 1, 5 ) );
		Assert.IsTrue( state.Accept( 1, 5 + 65536 ) );
		Assert.IsTrue( state.Accept( 1, 5 + 65536 * 2 - 50 ) );
	}

	[TestMethod]
	public void RemovedSlotAcceptsAnything()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };

		Assert.IsTrue( state.Accept( 1, 5 ) );
		state.Remove( 1 );
		Assert.IsTrue( state.Accept( 1, 3 ) );
	}
}
