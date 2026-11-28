using Sandbox.Tasks;
using System;
using System.Threading;

namespace SystemTests;

/// <summary>
/// At shutdown the main thread's context closes before the native engine goes: a continuation still queued to it would
/// otherwise run in the next wait on that thread (the runtime's own wait at exit, after the native dlls are unloaded).
/// </summary>
[TestClass]
public class SyncContextShutdownTests
{
	/// <summary>
	/// Waits on a thread whose context is <paramref name="context"/>, with <paramref name="posted"/> queued to it first and
	/// <paramref name="beforeWait"/> run before the wait, as the runtime waits at exit. True if the queued work ran.
	/// </summary>
	static bool QueuedWorkRunsInLaterWait( ExpirableSynchronizationContext context, Action beforeWait )
	{
		var ran = false;
		var thread = new Thread( () =>
		{
			SynchronizationContext.SetSynchronizationContext( context );
			context.Post( _ => ran = true, null );
			beforeWait?.Invoke();

			using var never = new ManualResetEvent( false );
			never.WaitOne( 100 );
		} );

		thread.Start();
		Assert.IsTrue( thread.Join( 5000 ), "The waiting thread didn't finish." );
		return ran;
	}

	/// <summary>
	/// Without closing, a later wait runs the queued continuation: how the quit crashed.
	/// </summary>
	[TestMethod]
	public void OpenContextRunsQueuedWorkInLaterWait()
	{
		Assert.IsTrue( QueuedWorkRunsInLaterWait( new ExpirableSynchronizationContext( false ), null ) );
	}

	/// <summary>
	/// Closed (as <see cref="SyncContext.Shutdown"/> does before the native engine goes), the wait runs nothing.
	/// </summary>
	[TestMethod]
	public void ClosedContextRunsNothingInLaterWait()
	{
		var context = new ExpirableSynchronizationContext( false );
		Assert.IsFalse( QueuedWorkRunsInLaterWait( context, context.Close ) );
		Assert.IsTrue( context.IsClosed );
		Assert.AreEqual( 0, context.QueueCount );
	}

	/// <summary>
	/// Work posted after closing is dropped, also through an expired instance whose successor closed.
	/// </summary>
	[TestMethod]
	public void PostAfterCloseIsDropped()
	{
		var old = new ExpirableSynchronizationContext( false );
		var current = new ExpirableSynchronizationContext( false );
		old.Expire( current );
		current.Close();

		var ran = false;
		current.Post( _ => ran = true, null );
		old.Post( _ => ran = true, null );
		current.Send( _ => ran = true, null );
		current.ProcessQueue();

		Assert.IsFalse( ran );
		Assert.AreEqual( 0, current.QueueCount );
	}
}
