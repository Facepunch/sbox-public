using System;
using System.Collections.Generic;
using System.Threading;

namespace BorealisTests;

/// <summary>
/// The game window's frame thread (<see cref="Sandbox.BorealisGpuFrameThread"/>): the work handed over runs on its own
/// thread, the join waits for it and throws what it threw, stopping never waits longer than the timeout, and a hand-over
/// and join allocate nothing on either thread once warm.
/// </summary>
[TestClass]
public class FrameThreadTests
{
	[TestMethod]
	public void HandedOverWorkRunsOnItsThreadAndTheJoinWaitsForIt()
	{
		var ran = -1;
		var thread = new Sandbox.BorealisGpuFrameThread( () =>
		{
			Thread.Sleep( 30 );
			ran = Environment.CurrentManagedThreadId;
		}, timed: false );

		try
		{
			thread.HandOver();
			Assert.IsTrue( thread.Pending );
			thread.Join();
			Assert.IsFalse( thread.Pending );
			Assert.AreNotEqual( -1, ran, "the join waited for the work" );
			Assert.AreNotEqual( Environment.CurrentManagedThreadId, ran, "on a thread of its own" );
			// The join blocks only if the work hasn't finished by then, which a loaded machine can delay past it
			Assert.IsTrue( thread.JoinsWaited <= 1, "a join waits at most once" );

			// Joining again, or with nothing handed over, does nothing
			thread.Join();
			Assert.AreEqual( 1, thread.HandOvers );
		}
		finally
		{
			thread.Dispose();
		}
	}

	[TestMethod]
	public void WhatTheWorkThrowsIsThrownAtTheJoin()
	{
		var calls = 0;
		var thread = new Sandbox.BorealisGpuFrameThread( () =>
		{
			if ( ++calls == 1 ) throw new InvalidOperationException( "the frame start failed" );
		}, timed: false );

		try
		{
			thread.HandOver();
			var e = Assert.ThrowsException<InvalidOperationException>( thread.Join );
			Assert.AreEqual( "the frame start failed", e.Message );

			// The thread goes on
			thread.HandOver();
			thread.Join();
			Assert.AreEqual( 2, calls );
		}
		finally
		{
			thread.Dispose();
		}
	}

	/// <summary>
	/// What the thread does while the main thread is away (the window presenting its last frame again): called once the work
	/// is done, again after the time it returns, until it returns a negative time; the next hand-over cuts its wait short and
	/// the join never waits for it.
	/// </summary>
	[TestMethod]
	public void IdleRunsAfterTheWorkUntilItStopsOrTheNextHandOver()
	{
		var idles = 0;
		var stopAfter = 3;
		var worked = 0;
		var thread = new Sandbox.BorealisGpuFrameThread( () => worked++, timed: false, idle: () =>
		{
			Interlocked.Increment( ref idles );
			return Volatile.Read( ref idles ) >= Volatile.Read( ref stopAfter ) ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds( Volatile.Read( ref stopAfter ) == int.MaxValue ? 10_000 : 5 );
		} );

		try
		{
			// Stops by itself after three calls
			thread.HandOver();
			thread.Join();
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while ( Volatile.Read( ref idles ) < 3 && clock.Elapsed < TimeSpan.FromSeconds( 5 ) ) Thread.Sleep( 1 );
			Thread.Sleep( 50 );
			Assert.AreEqual( 3, Volatile.Read( ref idles ), "called until it returned a negative time" );

			// A long wait is cut short by the next hand-over, whose work runs at once
			Volatile.Write( ref stopAfter, int.MaxValue );
			thread.HandOver();
			thread.Join();
			Assert.AreEqual( 2, worked );
			clock.Restart();
			thread.HandOver();
			thread.Join();
			Assert.AreEqual( 3, worked );
			Assert.IsTrue( clock.Elapsed < TimeSpan.FromSeconds( 1 ), $"the hand-over waited {clock.Elapsed} for the idle" );
		}
		finally
		{
			thread.Dispose();
		}
	}

	[TestMethod]
	public void StoppingWaitsNoLongerThanTheTimeout()
	{
		using var never = new ManualResetEventSlim();
		var thread = new Sandbox.BorealisGpuFrameThread( () => never.Wait(), timed: false );
		thread.HandOver();

		var clock = System.Diagnostics.Stopwatch.StartNew();
		thread.Dispose();
		Assert.IsTrue( clock.Elapsed < Sandbox.BorealisGpuFrameThread.StopTimeout * 2 + TimeSpan.FromSeconds( 1 ), $"stopped in {clock.Elapsed}" );
		never.Set();
	}

	/// <summary>
	/// A join that waits does so without the caller's synchronization context (the main thread's hears of its waits and runs
	/// queued game continuations in them), nor does stopping it: the context hears of no wait, and the caller's context is back
	/// after each.
	/// </summary>
	[TestMethod]
	public void AJoinThatWaitsDoesntTellTheCallersContext()
	{
		var thread = new Sandbox.BorealisGpuFrameThread( () => Thread.Sleep( 30 ), timed: false );
		var previous = SynchronizationContext.Current;
		var context = new WaitNotifiedContext();
		SynchronizationContext.SetSynchronizationContext( context );
		try
		{
			thread.HandOver();
			thread.Join();
			Assert.AreEqual( 1, thread.JoinsWaited, "the join waited" );
			Assert.AreEqual( 0, context.Waits, "the caller's context heard of the join's wait" );
			Assert.AreSame( context, SynchronizationContext.Current );

			// Stopping it with work handed over waits the same way
			thread.HandOver();
			thread.Dispose();
			Assert.AreEqual( 0, context.Waits, "the caller's context heard of the stop's wait" );
			Assert.AreSame( context, SynchronizationContext.Current );
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( previous );
			thread.Dispose();
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

	[TestMethod, TestCategory( Allocations.Category )]
	public void HandOverAndJoinAllocateNothingOnEitherThread()
	{
		long last = -1, frameThreadBytes = 0;
		var thread = new Sandbox.BorealisGpuFrameThread( () =>
		{
			// What the frame thread allocated since the last hand-over: its loop's waits and timings
			var now = GC.GetAllocatedBytesForCurrentThread();
			frameThreadBytes = last < 0 ? 0 : now - last;
			last = now;
		}, timed: false );

		try
		{
			Allocations.AssertSettles( "a hand-over and join on the main thread", () =>
			{
				thread.HandOver();
				thread.Join();
			} );

			var bytes = new List<long>();
			var settled = 0;
			for ( int i = 0; i < 100 && settled < 5; i++ )
			{
				thread.HandOver();
				thread.Join();
				bytes.Add( frameThreadBytes );
				settled = frameThreadBytes == 0 ? settled + 1 : 0;
			}

			Assert.AreEqual( 5, settled, $"the frame thread kept allocating: {string.Join( ", ", bytes )}" );
		}
		finally
		{
			thread.Dispose();
		}
	}
}
