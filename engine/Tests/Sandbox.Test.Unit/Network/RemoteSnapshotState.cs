using System;
using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class RemoteSnapshotStateTest
{
	private static DeltaSnapshot.SnapshotDataEntry CreateEntry( int slot, byte value, ulong hash )
	{
		return new DeltaSnapshot.SnapshotDataEntry
		{
			Slot = slot,
			Value = [value],
			Hash = hash
		};
	}

	[TestMethod]
	public void OlderAckDoesNotClearNewerPredictedValue()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };
		const int slot = 42;

		var ackAt1 = CreateEntry( slot, 1, 1001 );
		var ackAt2 = CreateEntry( slot, 2, 1002 );
		var predictedAt3 = CreateEntry( slot, 3, 1003 );

		state.Update( ackAt1, snapshotId: 1 );
		state.AddPredicted( predictedAt3, snapshotId: 3, timeNow: 0f );
		state.Update( ackAt2, snapshotId: 2 );

		Assert.IsTrue( state.TryGetHash( slot, out var hash, timeNow: 0.1f ) );
		Assert.AreEqual( 1003ul, hash );
	}

	[TestMethod]
	public void MatchingAckClearsPredictedValue()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };
		const int slot = 7;

		var ackAt1 = CreateEntry( slot, 1, 2001 );
		var ackAt2 = CreateEntry( slot, 2, 2002 );

		state.Update( ackAt1, snapshotId: 1 );
		state.AddPredicted( ackAt2, snapshotId: 2, timeNow: 0f );
		state.Update( ackAt2, snapshotId: 2 );

		Assert.IsTrue( state.TryGetHash( slot, out var hash, timeNow: 0.1f ) );
		Assert.AreEqual( 2002ul, hash );
	}

	[TestMethod]
	public void ExpiredPredictionForcesResend()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };
		const int slot = 99;

		var initialValues = CreateEntry( slot, 0, 1000 );
		state.Update( initialValues, snapshotId: 0 );

		var predicted = CreateEntry( slot, 1, 1001 );
		state.AddPredicted( predicted, snapshotId: 1, timeNow: 0f );

		Assert.IsFalse( state.TryGetHash( slot, out _, timeNow: 0.3f ) );
	}

	[TestMethod]
	public void NewerAckClearsOlderPredictedValue()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };
		const int slot = 10;

		var predictedAt2 = CreateEntry( slot, 2, 3002 );
		var ackAt5 = CreateEntry( slot, 5, 3005 );

		state.AddPredicted( predictedAt2, snapshotId: 2, timeNow: 0f );
		state.Update( ackAt5, snapshotId: 5 );

		Assert.IsTrue( state.TryGetHash( slot, out var hash, timeNow: 0.1f ) );
		Assert.AreEqual( 3005ul, hash );
	}

	[TestMethod]
	public void OutOfOrderAckDoesNotClearNewerPrediction()
	{
		var state = new RemoteSnapshotState { ObjectId = Guid.NewGuid() };
		const int slot = 55;

		var predictedS2 = CreateEntry( slot, 2, 2002 );
		var ackS1 = CreateEntry( slot, 1, 2001 );

		state.AddPredicted( predictedS2, snapshotId: 2, timeNow: 0f );
		state.Update( ackS1, snapshotId: 1 );

		Assert.IsTrue( state.TryGetHash( slot, out var hash, timeNow: 0.1f ) );
		Assert.AreEqual( 2002ul, hash );
	}
}
