using Sandbox.Engine;
using Sandbox.Utility;
using System.Threading;

namespace Sandbox.Tasks;

internal static partial class SyncContext
{
	/// <summary>
	/// Current sync context for the main thread. This will be null until <see cref="Init"/> has been
	/// called for the first time.
	/// </summary>
	public static ExpirableSynchronizationContext MainThread { get; private set; }

	/// <summary>
	/// Current sync context for worker threads. This will be null until <see cref="Init"/> has been
	/// called for the first time.
	/// </summary>
	public static ExpirableSynchronizationContext WorkerThread { get; private set; }

	/// <summary>
	/// Sets both <see cref="MainThread"/> and <see cref="SynchronizationContext.Current"/> to be a new
	/// instance of <see cref="ExpirableSynchronizationContext"/>. Only has an effect the first time it's called.
	/// </summary>
	public static void Init()
	{
		if ( MainThread != null ) return;

		MainThread = new ExpirableSynchronizationContext( false );
		WorkerThread = new ExpirableSynchronizationContext( true );

		SynchronizationContext.SetSynchronizationContext( MainThread );
	}

	/// <summary>
	/// Invalidates <see cref="MainThread"/> and <see cref="WorkerThread"/>, and replaces
	/// them with a new instance.
	/// Any tasks that try to continue on the old instances will log an error, unless they
	/// are whitelisted with <see cref="ExpirableSynchronizationContext.AllowPersistentTaskMethods"/>.
	/// </summary>
	public static void Reset()
	{
		MainThread = Reset( MainThread );
		WorkerThread = Reset( WorkerThread );
	}

	private static ExpirableSynchronizationContext Reset( ExpirableSynchronizationContext oldInstance )
	{
		var newInstance = new ExpirableSynchronizationContext( oldInstance.WarnNonYieldingTasks );

		if ( SynchronizationContext.Current == oldInstance )
		{
			SynchronizationContext.SetSynchronizationContext( newInstance );
		}

		oldInstance?.Expire( newInstance );

		newInstance.ProcessQueue();

		return newInstance;
	}

	/// <summary>
	/// Run an async task in a synchronous blocking manner.
	/// </summary>
	public static void RunBlocking( Task task )
	{
		ThreadSafe.AssertIsMainThread();

		// Single scope for the entire wait period (not per-iteration)
		using ( PerformanceStats.Timings.Async.Scope() )
		{
			while ( !task.IsCompleted )
			{
				EngineLoop.RunAsyncTasksCore();

				// Check + Sleep: only sleep if there's no pending work
				bool hasPendingWork =
					Sandbox.MainThread.HasPendingActions ||
					SyncContext.MainThread?.QueueCount > 0;

				if ( !hasPendingWork )
					Thread.Sleep( 16 );

				IToolsDll.Current?.Spin();
			}
		}

		if ( task.Exception != null )
			throw task.Exception;
	}

	/// <summary>
	/// Run an async task in a synchronous blocking manner and returns the result.
	/// </summary>
	public static TResult RunBlocking<TResult>( Task<TResult> task )
	{
		ThreadSafe.AssertIsMainThread();

		// Single scope for the entire wait period (not per-iteration)
		using ( PerformanceStats.Timings.Async.Scope() )
		{
			while ( !task.IsCompleted )
			{
				EngineLoop.RunAsyncTasksCore();

				// Check + Sleep: only sleep if there's no pending work
				bool hasPendingWork =
					Sandbox.MainThread.HasPendingActions ||
					SyncContext.MainThread?.QueueCount > 0;

				if ( !hasPendingWork )
					Thread.Sleep( 16 );
			}
		}

		if ( task.Exception != null )
			throw task.Exception;

		return task.Result;
	}

	/// <summary>
	/// Create a scope that sets the current synchronization context to the provided context.
	/// </summary>
	internal static IDisposable Scope( SynchronizationContext context )
	{
		var oldContext = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext( context );

		return DisposeAction.Create( () => { SynchronizationContext.SetSynchronizationContext( oldContext ); } );
	}
}
