using System.Threading;

namespace Sandbox.SceneRenderer;

/// <summary>
/// Reusable thread-pool batch with shared job claiming and caller assistance.
/// Allocation-free after warmup. <see cref="Wait"/> joins all jobs before reporting the first failure.
/// </summary>
internal sealed class JobBatch
{
	readonly Action<int> run;
	readonly List<Item> items = new();
	readonly CountdownEvent pending = new( 0 );
	int next;
	int total;
	Exception error;

	/// <summary>
	/// Create a batch retaining the per-slot callback.
	/// </summary>
	public JobBatch( Action<int> run )
	{
		this.run = run;
	}

	/// <summary>
	/// Start slots [0, count) on at most <paramref name="threads"/> workers. Wait before reusing the batch.
	/// </summary>
	public void Start( int count, int threads )
	{
		error = null;
		next = 0;
		total = count;

		var workers = Math.Min( count, Math.Max( 1, threads ) );
		pending.Reset( workers );

		while ( items.Count < workers ) items.Add( new Item( this ) );
		for ( int i = 0; i < workers; i++ )
			ThreadPool.UnsafeQueueUserWorkItem( items[i], preferLocal: false );
	}

	/// <summary>
	/// Run unclaimed jobs on the calling thread.
	/// </summary>
	public void Help() => Claim();

	/// <summary>
	/// Wait for every job, then rethrow the first one's exception if any threw.
	/// </summary>
	public void Wait()
	{
		if ( pending.CurrentCount > 0 ) pending.Wait();

		if ( error is { } e )
		{
			error = null;
			throw new AggregateException( "A job failed", e );
		}
	}

	void Claim()
	{
		int slot;
		while ( (slot = Interlocked.Increment( ref next ) - 1) < total )
		{
			try
			{
				run( slot );
			}
			catch ( Exception e )
			{
				Interlocked.CompareExchange( ref error, e, null );
			}
		}
	}

	sealed class Item( JobBatch batch ) : IThreadPoolWorkItem
	{
		public void Execute()
		{
			try
			{
				batch.Claim();
			}
			finally
			{
				batch.pending.Signal();
			}
		}
	}
}
