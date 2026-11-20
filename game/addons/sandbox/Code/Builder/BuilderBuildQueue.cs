using Sandbox.Diagnostics;

namespace Sandbox;

/// <summary>
/// Spreads builder model generation across frames. A big dupe of unique curved blocks would otherwise
/// build every mesh and cook every hull in one frame. Cached models never queue.
/// </summary>
public sealed class BuilderBuildQueue : GameObjectSystem<BuilderBuildQueue>
{
	const double BudgetMs = 2;

	readonly Queue<BuilderShapeKey> _pending = new();
	readonly HashSet<BuilderShapeKey> _queued = new();
	readonly List<Waiter> _waiters = new();
	RealTimeSince _sinceSweep;

	sealed class Waiter
	{
		public List<BuilderShapeKey> Keys;
		public TaskCompletionSource<bool> Done;
	}

	public BuilderBuildQueue( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Process, "BuilderBuildQueue" );
	}

	/// <summary>
	/// Ask for a model to be built soon. Poll <see cref="BuilderModelCache.TryGet"/> for the result.
	/// </summary>
	public void Enqueue( BuilderShapeKey key )
	{
		if ( BuilderModelCache.Contains( key ) || !_queued.Add( key ) ) return;
		_pending.Enqueue( key );
	}

	/// <summary>
	/// Builds every model in <paramref name="keys"/> over the next frames. Completes once they're all cached,
	/// so spawning afterwards doesn't stall. They stay cached for a while even before anything leases them.
	/// Returns false if the scene shut down first, in which case nothing should be spawned.
	/// </summary>
	public Task<bool> Prewarm( IEnumerable<BuilderShapeKey> keys )
	{
		List<BuilderShapeKey> missing = null;

		foreach ( var key in keys )
		{
			if ( BuilderModelCache.Contains( key ) ) continue;

			missing ??= new();
			missing.Add( key );
			Enqueue( key );
		}

		if ( missing is null ) return Task.FromResult( true );

		var waiter = new Waiter { Keys = missing, Done = new TaskCompletionSource<bool>() };
		_waiters.Add( waiter );
		return waiter.Done.Task;
	}

	void Process()
	{
		var timer = FastTimer.StartNew();
		var built = 0;

		while ( _pending.Count > 0 && (built == 0 || timer.ElapsedMilliSeconds < BudgetMs) )
		{
			var key = _pending.Dequeue();
			_queued.Remove( key );
			BuilderModelCache.Get( key );
			built++;
		}

		for ( int i = _waiters.Count - 1; i >= 0; i-- )
		{
			if ( !AllCached( _waiters[i].Keys ) ) continue;

			var waiter = _waiters[i];
			_waiters.RemoveAt( i );
			waiter.Done.TrySetResult( true );
		}

		if ( _sinceSweep > 5 && _waiters.Count == 0 && _pending.Count == 0 )
		{
			_sinceSweep = 0;
			BuilderModelCache.Sweep();
		}
	}

	static bool AllCached( List<BuilderShapeKey> keys )
	{
		foreach ( var key in keys )
		{
			if ( !BuilderModelCache.Contains( key ) ) return false;
		}

		return true;
	}

	public override void Dispose()
	{
		foreach ( var waiter in _waiters )
			waiter.Done.TrySetResult( false );

		_waiters.Clear();
		base.Dispose();
	}
}
