using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// A stretch of this thread's work without its synchronization context. A context that asks to be told of waits (the game's
/// main thread's does, to run queued work while it waits) makes the runtime allocate a handle array for each blocking wait,
/// a contended lock's included; device work the render thread and recording workers contend for runs inside one of these so
/// its waits allocate nothing. Nothing that runs game code (callbacks, continuations) belongs inside.
/// </summary>
internal readonly struct NoSyncContext : IDisposable
{
	readonly SynchronizationContext previous;

	NoSyncContext( SynchronizationContext previous ) => this.previous = previous;

	/// <summary>Clear this thread's synchronization context until the scope is disposed.</summary>
	public static NoSyncContext Enter()
	{
		var current = SynchronizationContext.Current;
		if ( current is not null ) SynchronizationContext.SetSynchronizationContext( null );
		return new NoSyncContext( current );
	}

	/// <summary>Put the context back.</summary>
	public void Dispose()
	{
		if ( previous is not null ) SynchronizationContext.SetSynchronizationContext( previous );
	}
}
