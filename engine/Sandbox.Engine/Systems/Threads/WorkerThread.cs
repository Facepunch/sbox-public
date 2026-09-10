using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Tasks;

internal static class WorkerThread
{
	private static Task[] _sWorkerTasks;
	private static CancellationTokenSource _sCts;

	public static bool HasStarted => _sWorkerTasks != null;

	/// <summary>
	/// Starts a bunch of long-running tasks in the worker thread pool that
	/// keep calling <see cref="ExpirableSynchronizationContext.ProcessQueue"/> on
	/// <see cref="SyncContext.WorkerThread"/>.
	/// </summary>
	/// <exception cref="InvalidOperationException">Thrown if tasks are already running.</exception>
	public static void Start()
	{
		if ( HasStarted )
		{
			throw new InvalidOperationException( "Attempted to start new worker threads while some are still running." );
		}

		ThreadPool.GetMinThreads( out var workerThreads, out _ );

		// Don't starve main thread

		workerThreads = Math.Max( Environment.ProcessorCount - 2, 2 );

		_sWorkerTasks = new Task[workerThreads];
		_sCts = new CancellationTokenSource();

		for ( var i = 0; i < workerThreads; i++ )
		{
			_sWorkerTasks[i] = Task.Run( WorkerTask );
		}
	}

	private static async Task WorkerTask()
	{
		var ct = _sCts.Token;

		while ( !ct.IsCancellationRequested )
		{
			SyncContext.WorkerThread.ProcessQueue();

			try
			{
				await Task.Delay( 1, ct );
			}
			catch ( OperationCanceledException )
			{
				return;
			}
		}
	}

	/// <summary>
	/// Forces the tasks created by <see cref="Start"/> to cancel, to be restarted later.
	/// This doesn't cancel tasks created with Sandbox.TaskSource.RunInThreadAsync, they
	/// just get suspended until <see cref="Start"/> is called again.
	/// </summary>
	/// <param name="millisecondsTimeout">
	/// Log an error if any tasks take longer than this to return.
	/// </param>
	public static void Stop( int millisecondsTimeout )
	{
		if ( _sWorkerTasks == null ) return;

		_sCts.Cancel();

		if ( !Task.WhenAll( _sWorkerTasks ).Wait( millisecondsTimeout ) )
		{
			Log.Error( "Background task(s) failed to pause within the allowed time.\n" +
				"Make sure any long-running backround tasks occasionally await TaskSource.Yield() or TaskSource.Delay().\n" +
				$"Tasks: {string.Join( "", _sWorkerTasks.Where( x => !x.IsCompleted ).Select( x => $"\n  {x}" ) )}" );
		}

		_sWorkerTasks = null;
		_sCts = null;
	}
}
