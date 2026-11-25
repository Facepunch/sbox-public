using System;
using System.Collections.Generic;
using Sandbox.Tasks;

namespace SystemTests;

[TestClass]
public class FrameStageAwaiterTest
{
	[TestMethod]
	public void WaitersResumeOnTheNextTriggerInOrder()
	{
		var stage = new SyncContext.FrameStage.FrameStageAwaiter();
		var resumed = new List<int>();

		for ( var i = 0; i < 100; i++ )
		{
			var index = i;
			var awaiter = stage.Await();
			Assert.IsFalse( awaiter.IsCompleted );
			awaiter.OnCompleted( () => resumed.Add( index ) );
		}

		stage.Trigger();
		CollectionAssert.AreEqual( Enumerable.Range( 0, 100 ).ToList(), resumed );

		stage.Trigger();
		Assert.AreEqual( 100, resumed.Count, "a waiter ran twice" );
	}

	[TestMethod]
	public void WaitingAgainFromAContinuationWaitsForTheNextTrigger()
	{
		var stage = new SyncContext.FrameStage.FrameStageAwaiter();
		var runs = 0;

		void Wait()
		{
			stage.Await().OnCompleted( () =>
			{
				runs++;
				Wait();
			} );
		}

		Wait();
		stage.Trigger();
		Assert.AreEqual( 1, runs );
		stage.Trigger();
		Assert.AreEqual( 2, runs );
	}

	[TestMethod]
	public void AwaitIsCompletedOnceTriggered()
	{
		var stage = new SyncContext.FrameStage.FrameStageAwaiter();
		var awaiter = stage.Await();
		stage.Trigger();
		Assert.IsTrue( awaiter.IsCompleted );
	}
}
