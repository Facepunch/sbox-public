using Sandbox.Tasks;
using System;
using System.Threading;

namespace SystemTests;

/// <summary>
/// Synchronous native boundaries must keep async continuations isolated from the engine queue.
/// </summary>
[TestClass]
public class AsyncPumpTests
{
	/// <summary>
	/// Awaited worker work returns to the pump without dispatching unrelated outer-context work.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void WorkerResultReturnsToCallingContext()
	{
		var previous = SynchronizationContext.Current;
		var outer = new CountingContext();
		SynchronizationContext.SetSynchronizationContext( outer );

		try
		{
			var caller = Environment.CurrentManagedThreadId;
			var result = AsyncPump.RunSynchronously( async () =>
			{
				var context = SynchronizationContext.Current;
				await Task.Yield();
				var value = await Task.Run( () => 42 );
				Assert.AreEqual( caller, Environment.CurrentManagedThreadId );
				Assert.AreSame( context, SynchronizationContext.Current );
				return value;
			} );

			Assert.AreEqual( 42, result );
			Assert.AreEqual( 0, outer.PostCount );
			Assert.AreSame( outer, SynchronizationContext.Current );
		}
		finally
		{
			SynchronizationContext.SetSynchronizationContext( previous );
		}
	}

	/// <summary>
	/// Synchronous and asynchronous failures preserve their original exception and restore context.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FailureRestoresContext( bool asynchronous )
	{
		var previous = SynchronizationContext.Current;
		var expected = new InvalidOperationException( "Expected pump failure" );
		Func<Task> operation = asynchronous
			? async () =>
			{
				await Task.Yield();
				throw expected;
			}
		: () => throw expected;

		var actual = Assert.ThrowsException<InvalidOperationException>( () => AsyncPump.RunSynchronously( operation ) );

		Assert.AreSame( expected, actual );
		Assert.AreSame( previous, SynchronizationContext.Current );
	}

	/// <summary>
	/// Cancellation propagates only after the operation finishes, restoring the caller's context.
	/// </summary>
	[TestMethod]
	public void CancellationRestoresContext()
	{
		var previous = SynchronizationContext.Current;
		var token = new CancellationToken( true );
		var exception = Assert.ThrowsException<OperationCanceledException>( () => AsyncPump.RunSynchronously( async () =>
		{
			await Task.Yield();
			token.ThrowIfCancellationRequested();
		} ) );

		Assert.AreEqual( token, exception.CancellationToken );
		Assert.AreSame( previous, SynchronizationContext.Current );
	}

	/// <summary>
	/// Send must never silently execute an owning-thread callback on a worker.
	/// </summary>
	[TestMethod]
	[Timeout( 5000 )]
	public void CrossThreadSendIsRejected()
	{
		AsyncPump.RunSynchronously( async () =>
		{
			var context = SynchronizationContext.Current;
			await Task.Run( () => Assert.ThrowsException<NotSupportedException>( () => context.Send( _ => Assert.Fail(), null ) ) );
		} );
	}

	sealed class CountingContext : SynchronizationContext
	{
		internal int PostCount;

		/// <summary>
		/// Record any work that accidentally escapes the private pump.
		/// </summary>
		public override void Post( SendOrPostCallback callback, object state ) => PostCount++;
	}
}
