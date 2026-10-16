using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Sandbox.Tasks;

public readonly struct SyncTask : INotifyCompletion
{
	private static readonly SendOrPostCallback _postCallback = state => ((Action)state!)();

	private readonly ExpirableSynchronizationContext _context;
	private readonly int _frame;
	private readonly bool _allowSynchronous;
	private readonly CancellationToken? _cancellation;

	internal SyncTask( ExpirableSynchronizationContext context, int frameOffset = 0, bool allowSynchronous = false, CancellationToken? cancellation = null )
	{
		_context = context;
		_frame = context.Frame = frameOffset;
		_allowSynchronous = allowSynchronous;
		_cancellation = cancellation;
	}

	/// <summary>
	/// Main-thread switches can complete inside a private synchronization context without
	/// posting to the engine queue. Frame waits still require their target context to advance.
	/// </summary>
	public bool IsCompleted => (_allowSynchronous && _context == SyncContext.MainThread && ThreadSafe.IsMainThread)
		|| (_context == SynchronizationContext.Current && _frame < _context.Frame);

	public void OnCompleted( Action continuation )
	{
		if ( _allowSynchronous && SynchronizationContext.Current == _context )
		{
			_context.Send( _postCallback, continuation );
			return;
		}

		_context.Post( _postCallback, continuation );
	}

	public void GetResult()
	{
		_cancellation?.ThrowIfCancellationRequested();
	}

	public SyncTask GetAwaiter() => this;
}
