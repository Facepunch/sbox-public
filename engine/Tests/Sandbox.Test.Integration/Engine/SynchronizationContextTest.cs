using Sandbox.Tasks;
using System;
using System.Diagnostics;
using System.Threading;

namespace EngineTests;

[TestClass]
public class SynchronizationContextTest
{
	const int WaitTimeout = 0x102;

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
