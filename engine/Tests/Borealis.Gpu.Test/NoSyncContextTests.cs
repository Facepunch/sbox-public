using System.Threading;
using Borealis.Backend;

namespace BorealisGpuTests;

/// <summary>
/// <see cref="NoSyncContext"/>: device work the frame services do on the game's main thread waits for locks the render thread
/// holds; inside the scope those waits don't reach the main thread's context (which asks to hear of waits, and has the runtime
/// allocate a handle array for each), and the context comes back after.
/// </summary>
[TestClass]
[DoNotParallelize]
public class NoSyncContextTests
{
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ContendedLockInsideAllocatesNothing()
	{
		var previous = SynchronizationContext.Current;
		var context = new WaitNotifiedContext();
		var gate = new object();
		using var held = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var stop = false;
		var holder = new Thread( () =>
		{
			while ( true )
			{
				release.Wait();
				release.Reset();
				if ( Volatile.Read( ref stop ) ) return;
				lock ( gate )
				{
					held.Set();
					Thread.Sleep( 2 );
				}
			}
		} )
		{ IsBackground = true };
		holder.Start();

		try
		{
			SynchronizationContext.SetSynchronizationContext( context );
			var waitsInside = 0;
			Allocations.AssertSettles( "a contended lock inside the scope", () =>
			{
				var before = context.Waits;
				using ( NoSyncContext.Enter() )
				{
					lock ( gate ) { }
				}

				waitsInside += context.Waits - before;
			}, beforePass: () =>
			{
				// The other thread holds the lock as the pass starts, so the pass waits for it
				held.Reset();
				release.Set();
				held.Wait();
			} );

			Assert.AreEqual( 0, waitsInside, "the context heard of a wait inside the scope" );
			Assert.AreSame( context, SynchronizationContext.Current, "the context didn't come back" );
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( previous );
			Volatile.Write( ref stop, true );
			release.Set();
			holder.Join();
		}
	}

	sealed class WaitNotifiedContext : SynchronizationContext
	{
		public int Waits;

		public WaitNotifiedContext() => SetWaitNotificationRequired();

		public override int Wait( IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout )
		{
			Waits++;
			return base.Wait( waitHandles, waitAll, millisecondsTimeout );
		}
	}
}
