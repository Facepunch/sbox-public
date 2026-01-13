using System.Threading;
using System.Threading.Channels;

namespace Sandbox;

/// <summary>
/// Utility functions that revolve around the main thread
/// </summary>
public static class MainThread
{
	static Channel<IDisposable> Disposables = Channel.CreateUnbounded<IDisposable>();
	static Channel<Action> Actions = Channel.CreateUnbounded<Action>();

	/// <summary>
	/// Wait to execute on the main thread
	/// </summary>
	public static SyncTask Wait()
	{
		return new SyncTask( SyncContext.MainThread, allowSynchronous: true );
	}


	internal static void QueueDispose( IDisposable disposable )
	{
		if ( disposable is null )
			return;

		if ( ThreadSafe.IsMainThread )
		{
			disposable.Dispose();
			return;
		}

		Disposables.Writer.TryWrite( disposable );
	}

	/// <summary>
	/// Run a function on the main thread and wait for the result.
	/// </summary>
	internal static T Run<T>( int millisecondsTimeout, Func<T> func )
	{
		if ( ThreadSafe.IsMainThread )
			return func();

		T r = default;
		using var reset = new ManualResetEvent( false );

		Queue( () =>
		{
			try
			{
				r = func();
			}
			finally
			{
				reset.Set();
			}
		} );

		if ( !reset.WaitOne( millisecondsTimeout ) )
		{
			return default;
		}
		return r;
	}

	internal static void RunQueues()
	{
		ThreadSafe.AssertIsMainThread();

		while ( Disposables.Reader.TryRead( out var disposable ) )
		{
			disposable.Dispose();
		}

		RunMainThreadQueues();
	}

	/// <summary>
	/// When running in another thread you can queue a method to run in the main thread.
	/// If you are on the main thread we will execute the method immediately and return.
	/// </summary>
	public static void Queue( Action method )
	{
		if ( ThreadSafe.IsMainThread )
		{
			method();
			return;
		}

		Actions.Writer.TryWrite( method );
	}

	/// <summary>
	/// Run queued actions on the main thread
	/// </summary>
	internal static void RunMainThreadQueues()
	{
		ThreadSafe.AssertIsMainThread();

		while ( Actions.Reader.TryRead( out var action ) )
		{
			try
			{
				action();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}
	}
}
