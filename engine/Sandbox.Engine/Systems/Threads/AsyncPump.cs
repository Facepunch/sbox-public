using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox.Tasks;

/// <summary>
/// Bridges synchronous callers to asynchronous operations without processing unrelated engine work.
/// </summary>
internal static class AsyncPump
{
	/// <summary>
	/// Runs an operation and its captured continuations on the calling thread until it completes.
	/// All child work must be awaited. Engine frame/game-time waits and dispatch back to a blocked
	/// engine queue are unsupported. Explicit worker tasks and ConfigureAwait(false) retain their
	/// usual behavior. Nested calls have independent queues.
	/// </summary>
	internal static void RunSynchronously( Func<Task> operation )
	{
		Run( operation ).GetAwaiter().GetResult();
	}

	/// <summary>
	/// Runs an operation with the same restrictions as the void overload and returns its result.
	/// Exceptions and cancellation propagate to the caller.
	/// </summary>
	internal static T RunSynchronously<T>( Func<Task<T>> operation )
	{
		return ((Task<T>)Run( operation )).GetAwaiter().GetResult();
	}

	static Task Run( Func<Task> operation )
	{
		ArgumentNullException.ThrowIfNull( operation );
		using var context = new PumpSynchronizationContext();
		return context.Run( operation );
	}

	sealed class PumpSynchronizationContext : SynchronizationContext, IDisposable
	{
		readonly BlockingCollection<(SendOrPostCallback Callback, object State)> _queue = new();
		readonly int _threadId = Environment.CurrentManagedThreadId;

		/// <summary>
		/// Queue an awaited continuation for the calling thread.
		/// </summary>
		public override void Post( SendOrPostCallback callback, object state )
		{
			_queue.Add( (callback, state) );
		}

		/// <summary>
		/// Copies must route continuations to the same operation's queue.
		/// </summary>
		public override SynchronizationContext CreateCopy() => this;

		/// <summary>
		/// Synchronous callbacks are only supported on the owning thread.
		/// Cross-thread callers must post work instead.
		/// </summary>
		public override void Send( SendOrPostCallback callback, object state )
		{
			if ( Environment.CurrentManagedThreadId != _threadId )
				throw new NotSupportedException( "AsyncPump does not support cross-thread Send. Use Post instead." );

			callback( state );
		}

		/// <summary>
		/// Run an operation and its continuations, restoring the caller's context on every exit.
		/// </summary>
		public Task Run( Func<Task> operation )
		{
			var previous = Current;
			SetSynchronizationContext( this );

			try
			{
				var task = operation();
				if ( task.IsCompleted )
					return task;

				// Completion can happen without posting here, for example when an operation
				// returns a worker task directly. Wake the consuming thread in that case too.
				_ = task.ContinueWith( _ => _queue.CompleteAdding(), CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default );

				foreach ( var work in _queue.GetConsumingEnumerable() )
				{
					work.Callback( work.State );
				}

				return task;
			}
			finally
			{
				SetSynchronizationContext( previous );
			}
		}

		/// <summary>
		/// Release the queue after the operation and its continuations have finished.
		/// </summary>
		public void Dispose() => _queue.Dispose();
	}
}
