using Sandbox.Tasks;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EngineTests;

[TestClass]
public class SynchronizationContextTest
{
	const int WaitTimeout = 0x102;

	[TestMethod]
	public void YieldDoesNotRequireAnotherQueuePump()
	{
		var context = new ExpirableSynchronizationContext( false );
		var previous = SynchronizationContext.Current;
		try
		{
			SynchronizationContext.SetSynchronizationContext( context );
			var task = YieldTwice();
			Assert.IsFalse( task.IsCompleted );
			context.ProcessQueue();
			Assert.IsTrue( task.IsCompletedSuccessfully, "Task.Yield is not a frame boundary or rendering fence." );
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( previous );
		}

		static async Task YieldTwice()
		{
			await Task.Yield();
			await Task.Yield();
		}
	}

	[TestMethod]
	public void TimedWaitHonorsTimeout()
	{
		var context = new ExpirableSynchronizationContext( false );
		using var signal = new ManualResetEvent( false );
		var timer = Stopwatch.StartNew();

		Assert.AreEqual( WaitTimeout, context.Wait( [signal.SafeWaitHandle.DangerousGetHandle()], false, 50 ) );
		Assert.IsTrue( timer.Elapsed >= TimeSpan.FromMilliseconds( 50 ), "The wait returned before its timeout." );
	}

	[TestMethod]
	[DataRow( 1000 )]
	[DataRow( Timeout.Infinite )]
	public void WaitPumpsQueuedWorkUntilSignaled( int timeout )
	{
		var context = new ExpirableSynchronizationContext( false );
		using var signal = new ManualResetEvent( false );
		context.Post( _ => signal.Set(), null );

		Assert.AreEqual( 0, context.Wait( [signal.SafeWaitHandle.DangerousGetHandle()], false, timeout ) );
	}

	[TestMethod]
	public void ZeroTimeoutOnlyPollsHandle()
	{
		var context = new ExpirableSynchronizationContext( false );
		using var signal = new ManualResetEvent( false );
		context.Post( _ => signal.Set(), null );

		Assert.AreEqual( WaitTimeout, context.Wait( [signal.SafeWaitHandle.DangerousGetHandle()], false, 0 ) );
		Assert.AreEqual( 1, context.QueueCount );

		signal.Set();
		Assert.AreEqual( 0, context.Wait( [signal.SafeWaitHandle.DangerousGetHandle()], false, 0 ) );
	}
}
