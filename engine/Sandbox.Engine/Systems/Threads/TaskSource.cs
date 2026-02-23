using Sandbox.Engine;
using System.Threading;

namespace Sandbox;

/// <summary>
/// A generic <see cref="TaskSource"/>.
/// </summary>
public static class GameTask
{

	private static TaskSource source => GlobalContext.Current.TaskSource;

	/// <inheritdoc cref="TaskSource.Yield"/>
	public static Task Yield() => source.Yield();

	/// <inheritdoc cref="TaskSource.Delay(int)"/>
	public static Task Delay( int ms ) => source.Delay( ms );

	/// <inheritdoc cref="TaskSource.Delay(int, CancellationToken)"/>
	public static Task Delay( int ms, CancellationToken ct ) => source.Delay( ms, ct );

	/// <inheritdoc cref="TaskSource.DelaySeconds(float)"/>
	public static Task DelaySeconds( float seconds ) => Delay( (int)(seconds * 1000.0f) );

	/// <inheritdoc cref="TaskSource.DelaySeconds(float, CancellationToken)"/>
	public static Task DelaySeconds( float seconds, CancellationToken ct ) => Delay( (int)(seconds * 1000.0f), ct );

	/// <inheritdoc cref="TaskSource.DelayRealtime(int)"/>
	public static Task DelayRealtime( int ms ) => source.DelayRealtime( ms );

	/// <inheritdoc cref="TaskSource.DelayRealtime(int, CancellationToken)"/>
	public static Task DelayRealtime( int ms, CancellationToken ct ) => source.DelayRealtime( ms, ct );

	/// <inheritdoc cref="TaskSource.DelayRealtimeSeconds(float)"/>
	public static Task DelayRealtimeSeconds( float seconds ) => DelayRealtime( (int)(seconds * 1000.0f) );

	/// <inheritdoc cref="TaskSource.DelayRealtimeSeconds(float, CancellationToken)"/>
	public static Task DelayRealtimeSeconds( float seconds, CancellationToken ct ) => DelayRealtime( (int)(seconds * 1000.0f), ct );

	/// <inheritdoc cref="TaskSource.RunInThreadAsync(Action)"/>
	public static Task RunInThreadAsync( Action action ) => source.RunInThreadAsync( action );

	/// <inheritdoc cref="TaskSource.RunInThreadAsync{T}(Func{T})"/>
	public static Task<T> RunInThreadAsync<T>( Func<T> func ) => source.RunInThreadAsync( func );

	/// <inheritdoc cref="TaskSource.RunInThreadAsync(Func{Task})"/>
	public static Task RunInThreadAsync( Func<Task> task ) => source.RunInThreadAsync( task );

	/// <inheritdoc cref="TaskSource.RunInThreadAsync{T}(Func{Task{T}})"/>
	public static Task<T> RunInThreadAsync<T>( Func<Task<T>> task ) => source.RunInThreadAsync( task );

	/// <inheritdoc cref="TaskSource.CompletedTask"/>
	public static Task CompletedTask => source.CompletedTask;

	/// <inheritdoc cref="TaskSource.FromResult{T}"/>
	public static Task<T> FromResult<T>( T t ) => source.FromResult( t );

	/// <inheritdoc cref="TaskSource.WhenAll(Task[])"/>
	public static Task WhenAll( params Task[] tasks ) => source.WhenAll( tasks );

	/// <inheritdoc cref="TaskSource.WhenAll(IEnumerable{Task})"/>
	public static Task WhenAll( IEnumerable<Task> tasks ) => source.WhenAll( tasks );

	/// <inheritdoc cref="TaskSource.WhenAll{T}(Task{T}[])"/>
	public static Task<T[]> WhenAll<T>( params Task<T>[] tasks ) => source.WhenAll<T>( tasks );

	/// <inheritdoc cref="TaskSource.WhenAll{T}(IEnumerable{Task{T}})"/>
	public static Task<T[]> WhenAll<T>( IEnumerable<Task<T>> tasks ) => source.WhenAll<T>( tasks );

	/// <inheritdoc cref="TaskSource.WhenAny(Task[])"/>
	public static Task WhenAny( params Task[] tasks ) => source.WhenAny( tasks );

	/// <inheritdoc cref="TaskSource.WhenAny(IEnumerable{Task})"/>
	public static Task WhenAny( IEnumerable<Task> tasks ) => source.WhenAny( tasks );

	/// <inheritdoc cref="TaskSource.WhenAny{T}(Task{T}[])"/>
	public static Task<Task<T>> WhenAny<T>( params Task<T>[] tasks ) => source.WhenAny<T>( tasks );

	/// <inheritdoc cref="TaskSource.WhenAny{T}(IEnumerable{Task{T}})"/>
	public static Task<Task<T>> WhenAny<T>( IEnumerable<Task<T>> tasks ) => source.WhenAny<T>( tasks );

	/// <inheritdoc cref="TaskSource.WaitAll(Task[])"/>
	public static void WaitAll( params Task[] tasks ) => source.WaitAll( tasks );

	/// <inheritdoc cref="TaskSource.WaitAny(Task[])"/>
	public static void WaitAny( params Task[] tasks ) => source.WaitAny( tasks );

	/// <inheritdoc cref="TaskSource.MainThread"/>
	public static SyncTask MainThread()
	{
		return new SyncTask( SyncContext.MainThread, allowSynchronous: true );
	}

	/// <inheritdoc cref="TaskSource.MainThread"/>
	public static SyncTask MainThread( CancellationToken cancellation )
	{
		return new SyncTask( SyncContext.MainThread, allowSynchronous: true, cancellation: cancellation );
	}

	/// <inheritdoc cref="TaskSource.WorkerThread"/>
	public static SyncTask WorkerThread()
	{
		TaskSource.EnsureWorkerThreadsStarted();
		return new SyncTask( SyncContext.WorkerThread, allowSynchronous: true );
	}

	/// <inheritdoc cref="TaskSource.WorkerThread"/>
	public static SyncTask WorkerThread( CancellationToken cancellation )
	{
		TaskSource.EnsureWorkerThreadsStarted();
		return new SyncTask( SyncContext.WorkerThread, allowSynchronous: true, cancellation: cancellation );
	}
}

/// <summary>
/// Provides a way for us to cancel tasks after common async shit is executed.
/// </summary>
public struct TaskSource
{
	private static CancellationTokenSource currentGenerationCts => GlobalContext.Current.CancellationTokenSource;

	internal static CancellationToken Cancellation => currentGenerationCts.Token;

	internal static readonly TaskSource Cancelled = new TaskSource( new CancellationToken( true ) );

	internal TaskSource( int i = 0 )
	{
		_isValid = true;
		_cancellation = Cancellation;

		if ( Cancellation.IsCancellationRequested )
		{
			Log.Warning( $"new TaskSource is already cancelled" );
		}
	}

	internal TaskSource( CancellationToken token )
	{
		if ( token == default )
			token = Cancellation;

		_isValid = true;
		_cancellation = token;
	}

	private static string FormatAction( Delegate action )
	{
		return action.ToSimpleString();
	}

	public static TaskSource Create( CancellationToken token = default )
	{
		return new TaskSource( token );
	}

	/// <summary>
	/// Create a token source, which will also be cancelled when sessions end
	/// </summary>
	public static CancellationTokenSource CreateLinkedTokenSource()
	{
		return CancellationTokenSource.CreateLinkedTokenSource( Cancellation );
	}

	private readonly CancellationToken _cancellation;
	private bool _isValid;

	/// <inheritdoc cref="IValid.IsValid"/>
	public bool IsValid => _isValid && !_cancellation.IsCancellationRequested;

	/// <summary>
	/// Marks this task source as invalid. All associated running tasks will be canceled ASAP.
	/// </summary>
	internal void Expire()
	{
		_isValid = false;
	}

	internal void CancelIfInvalid()
	{
		if ( !IsValid )
		{
			throw new TaskCanceledException();
		}
	}

	/// <summary>
	/// A task that does nothing for given amount of time in milliseconds.
	/// </summary>
	/// <param name="ms">Time to wait in milliseconds.</param>
	public Task Delay( int ms )
	{
		return DelayInternal( ms, _cancellation );
	}

	/// <summary>
	/// A task that does nothing for given amount of time in milliseconds.
	/// </summary>
	/// <param name="ms">Time to wait in milliseconds.</param>
	/// <param name="ct">Token to cancel the delay early.</param>
	public async Task Delay( int ms, CancellationToken ct )
	{
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource( ct, _cancellation );
		await DelayInternal( ms, linkedCts.Token );
	}

	private async Task DelayInternal( int ms, CancellationToken ct )
	{
		var time = Time.NowDouble + ms / 1000.0;

		while ( Time.NowDouble < time )
		{
			await Task.Delay( 1, ct );
			CancelIfInvalid();
		}

		CancelIfInvalid();
	}

	/// <summary>
	/// A task that does nothing for given amount of time in seconds.
	/// </summary>
	/// <param name="seconds">>Time to wait in seconds.</param>
	public Task DelaySeconds( float seconds ) => Delay( (int)(seconds * 1000.0) );

	/// <summary>
	/// A task that does nothing for given amount of time in seconds.
	/// </summary>
	/// <param name="seconds">>Time to wait in seconds.</param>
	/// <param name="ct">Token to cancel the delay early.</param>
	public Task DelaySeconds( float seconds, CancellationToken ct ) => Delay( (int)(seconds * 1000.0), ct );

	private record struct WorkerThreadAction( Action Action, TaskCompletionSource Tcs )
	{
		public static SendOrPostCallback PostCallback { get; } = state =>
		{
			var item = (WorkerThreadAction)state!;

			try
			{
				item.Action();
				item.Tcs.SetResult();
			}
			catch ( Exception e )
			{
				item.Tcs.SetException( e );
			}
		};

		public override string ToString()
		{
			return FormatAction( Action );
		}
	}

	internal static void EnsureWorkerThreadsStarted()
	{
		if ( !Tasks.WorkerThread.HasStarted ) Tasks.WorkerThread.Start();
	}

	public async Task RunInThreadAsync( Action action )
	{
		var tcs = new TaskCompletionSource();
		SyncContext.WorkerThread.Post( WorkerThreadAction.PostCallback, new WorkerThreadAction( action, tcs ) );

		EnsureWorkerThreadsStarted();

		await tcs.Task;
		CancelIfInvalid();
	}

	private record struct WorkerThreadFunc<T>( Func<T> Func, TaskCompletionSource<T> Tcs )
	{
		public static SendOrPostCallback PostCallback { get; } = state =>
		{
			var item = (WorkerThreadFunc<T>)state!;

			try
			{
				var result = item.Func();
				item.Tcs.SetResult( result );
			}
			catch ( Exception e )
			{
				item.Tcs.SetException( e );
			}
		};

		public override string ToString()
		{
			return FormatAction( Func );
		}
	}

	public async Task<T> RunInThreadAsync<T>( Func<T> func )
	{
		var tcs = new TaskCompletionSource<T>();
		SyncContext.WorkerThread.Post( WorkerThreadFunc<T>.PostCallback, new WorkerThreadFunc<T>( func, tcs ) );

		EnsureWorkerThreadsStarted();

		var result = await tcs.Task;
		CancelIfInvalid();
		return result;
	}

	private record struct WorkerThreadTask( Func<Task> TaskFunc, TaskCompletionSource<Task> Tcs )
	{
		private static Action<Task, object> ContinuationAction { get; } = ( task, state ) =>
		{
			var tcs = (TaskCompletionSource<Task>)state;

			if ( task.IsCanceled )
			{
				tcs.SetCanceled();
			}
			else if ( task.IsFaulted )
			{
				tcs.SetException( task.Exception! );
			}
			else
			{
				Assert.True( task.IsCompletedSuccessfully );
				tcs.SetResult( task );
			}
		};

		public static SendOrPostCallback PostCallback { get; } = state =>
		{
			var item = (WorkerThreadTask)state!;

			try
			{
				var task = item.TaskFunc();
				task.ContinueWith( ContinuationAction, item.Tcs );
			}
			catch ( Exception e )
			{
				item.Tcs.SetException( e );
			}
		};

		public override string ToString()
		{
			return FormatAction( TaskFunc );
		}
	}

	public async Task RunInThreadAsync( Func<Task> task )
	{
		var tcs = new TaskCompletionSource<Task>();
		SyncContext.WorkerThread.Post( WorkerThreadTask.PostCallback, new WorkerThreadTask( task, tcs ) );

		EnsureWorkerThreadsStarted();

		await await tcs.Task;
		CancelIfInvalid();
	}

	private record struct WorkerThreadTask<T>( Func<Task<T>> TaskFunc, TaskCompletionSource<Task<T>> Tcs )
	{
		private static Action<Task<T>, object> ContinuationAction { get; } = ( task, state ) =>
		{
			var tcs = (TaskCompletionSource<Task<T>>)state;

			if ( task.IsCanceled )
			{
				tcs.SetCanceled();
			}
			else if ( task.IsFaulted )
			{
				tcs.SetException( task.Exception! );
			}
			else
			{
				Assert.True( task.IsCompletedSuccessfully );
				tcs.SetResult( task );
			}
		};

		public static SendOrPostCallback PostCallback { get; } = state =>
		{
			var item = (WorkerThreadTask<T>)state!;

			try
			{
				var task = item.TaskFunc();
				task.ContinueWith( ContinuationAction, item.Tcs );
			}
			catch ( Exception e )
			{
				item.Tcs.SetException( e );
			}
		};

		public override string ToString()
		{
			return FormatAction( TaskFunc );
		}
	}

	public async Task<T> RunInThreadAsync<T>( Func<Task<T>> task )
	{
		var tcs = new TaskCompletionSource<Task<T>>();
		SyncContext.WorkerThread.Post( WorkerThreadTask<T>.PostCallback, new WorkerThreadTask<T>( task, tcs ) );

		EnsureWorkerThreadsStarted();

		var result = await await tcs.Task;
		CancelIfInvalid();
		return result;
	}

	public async Task DelayRealtime( int ms )
	{
		await Task.Delay( ms, _cancellation );
		CancelIfInvalid();
	}

	public async Task DelayRealtime( int ms, CancellationToken ct )
	{
		using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource( ct, _cancellation );
		await Task.Delay( ms, linkedCts.Token );
		CancelIfInvalid();
	}

	public Task DelayRealtimeSeconds( float seconds ) => DelayRealtime( (int)(seconds * 1000.0f) );

	public Task DelayRealtimeSeconds( float seconds, CancellationToken ct ) => DelayRealtime( (int)(seconds * 1000.0f), ct );

	/// <summary>
	/// Continues on the main thread.
	/// </summary>
	public SyncTask MainThread()
	{
		return GameTask.MainThread( Cancellation );
	}

	/// <summary>
	/// Continues on a worker thread.
	/// </summary>
	public SyncTask WorkerThread()
	{
		return GameTask.WorkerThread( Cancellation );
	}

	/// <inheritdoc cref="Task.CompletedTask" />
	public Task CompletedTask => Task.CompletedTask;

	/// <inheritdoc cref="Task.FromResult{T}" />
	public Task<T> FromResult<T>( T t ) => Task.FromResult( t );

	/// <inheritdoc cref="Task.FromCanceled" />
	public Task FromCanceled( CancellationToken token ) => Task.FromCanceled( token );

	/// <inheritdoc cref="Task.FromException" />
	public Task FromException( Exception e ) => Task.FromException( e );

	/// <inheritdoc cref="Task.WhenAll(Task[])" />
	public async Task WhenAll( params Task[] tasks )
	{
		await Task.WhenAll( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
	}

	/// <inheritdoc cref="Task.WhenAll(IEnumerable{Task})" />
	public async Task WhenAll( IEnumerable<Task> tasks )
	{
		await Task.WhenAll( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
	}

	/// <inheritdoc cref="Task.WhenAll{T}(Task{T}[])" />
	public async Task<T[]> WhenAll<T>( params Task<T>[] tasks )
	{
		var result = await Task.WhenAll<T>( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
		return result;
	}

	/// <inheritdoc cref="Task.WhenAll{T}(IEnumerable{Task{T}})" />
	public async Task<T[]> WhenAll<T>( IEnumerable<Task<T>> tasks )
	{
		var result = await Task.WhenAll<T>( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
		return result;
	}

	/// <inheritdoc cref="Task.WhenAny(Task[])" />
	public async Task WhenAny( params Task[] tasks )
	{
		await Task.WhenAny( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
	}

	/// <inheritdoc cref="Task.WhenAny(IEnumerable{Task})" />
	public async Task WhenAny( IEnumerable<Task> tasks )
	{
		await Task.WhenAny( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
	}

	/// <inheritdoc cref="Task.WaitAny(Task[])" />
	public void WaitAny( params Task[] tasks )
	{
		Task.WaitAny( tasks, _cancellation );
	}

	/// <inheritdoc cref="Task.WaitAll(Task[])" />
	public void WaitAll( params Task[] tasks )
	{
		Task.WaitAll( tasks, _cancellation );
	}

	/// <inheritdoc cref="Task.WhenAny{T}(Task{T}[])" />
	public async Task<Task<T>> WhenAny<T>( params Task<T>[] tasks )
	{
		var result = await Task.WhenAny<T>( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
		return result;
	}

	/// <inheritdoc cref="Task.WhenAny{T}(IEnumerable{Task{T}})" />
	public async Task<Task<T>> WhenAny<T>( IEnumerable<Task<T>> tasks )
	{
		var result = await Task.WhenAny<T>( tasks ).WaitAsync( _cancellation );
		CancelIfInvalid();
		return result;
	}

	/// <inheritdoc cref="Task.Yield" />
	public async Task Yield()
	{
		await Task.Yield();
		CancelIfInvalid();
	}

	/// <summary>
	/// Wait until the start of the next frame
	/// </summary>
	public async Task Frame()
	{
		await SyncContext.FrameStage.Update.Await();
		CancelIfInvalid();
	}

	/// <summary>
	/// Wait until the end of the frame
	/// </summary>
	public async Task FrameEnd()
	{
		await SyncContext.FrameStage.PreRender.Await();
		CancelIfInvalid();
	}

	/// <summary>
	/// Wait until the next fixed update
	/// </summary>
	public async Task FixedUpdate()
	{
		await SyncContext.FrameStage.FixedUpdate.Await();
		CancelIfInvalid();
	}
}
