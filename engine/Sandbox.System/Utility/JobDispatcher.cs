using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Runs per-frame work across every core without allocating: an engine stand-in for
/// <see cref="System.Threading.Tasks.Parallel.For(int, int, Action{int})"/>. Work is split into ranges that the calling
/// thread and the dispatcher's own worker threads claim off a shared counter, the caller taking part rather than waiting.
/// State for each call is pooled, so a call needs nothing kept at its call site: pass what the work needs as the state
/// of a static lambda, or a struct <see cref="IBatchJob"/> for work the JIT can inline.
/// <para>
/// Jobs run in the calling thread's execution context, so async locals such as the current
/// <c>Engine.GlobalContext</c> are the caller's on every thread, and what a job changes in it doesn't leak back to
/// the caller or into later jobs. They run in the caller's <see cref="FloatingPointMode"/> too, so their results are the
/// same whichever thread runs them, and however many there are. When a job throws, no more are claimed, every running job is joined, and the first
/// exception is rethrown.
/// </para>
/// <para>
/// Jobs run without a <see cref="SynchronizationContext"/> on every thread, the caller's included, and joining doesn't
/// pump the caller's queue. A job must never wait on work queued to the main thread.
/// </para>
/// </summary>
[SkipHotload]
internal static class JobDispatcher
{
	/// <summary>
	/// The most threads, the caller's included, a call runs on. 1 runs everything on the calling thread, for profiling
	/// and benchmarking single threaded. Starts at the core count, or <c>SBOX_MAX_PARALLELISM</c> when that's set.
	/// </summary>
	public static int MaxDegreeOfParallelism
	{
		get => maxDegreeOfParallelism;
		set => maxDegreeOfParallelism = Math.Max( 1, value );
	}

	static int maxDegreeOfParallelism = int.TryParse( Environment.GetEnvironmentVariable( "SBOX_MAX_PARALLELISM" ), out var limit ) && limit > 0 ? limit : Environment.ProcessorCount;

	/// <summary>
	/// Work over a range of items, run on whichever thread claims it.
	/// </summary>
	public interface IBatchJob
	{
		/// <summary>
		/// Do items [<paramref name="start"/>, <paramref name="endExclusive"/>).
		/// </summary>
		void Process( int start, int endExclusive );
	}

	/// <summary>
	/// Run <paramref name="body"/> for each item in [0, <paramref name="count"/>) across threads, and return when all are
	/// done. Pass a static lambda and what it needs as <paramref name="state"/>, so the call doesn't allocate.
	/// </summary>
	/// <param name="count">How many items there are.</param>
	/// <param name="state">Passed to every <paramref name="body"/> call.</param>
	/// <param name="body">Called with the state and each item's index.</param>
	/// <param name="itemsPerBatch">How many items a thread claims at once. 0 splits the work into a few ranges per thread,
	/// which suits many small, even items; pass 1 for few, uneven ones, so a slow item can't hold others up.</param>
	/// <param name="maxDegreeOfParallelism">The most threads, the caller's included, to run on. 0 is
	/// <see cref="MaxDegreeOfParallelism"/>.</param>
	public static void For<TState>( int count, TState state, Action<TState, int> body, int itemsPerBatch = 0, int maxDegreeOfParallelism = 0 )
		=> ForBatched( count, new ForJob<TState>( state, body ), itemsPerBatch, maxDegreeOfParallelism );

	/// <summary>
	/// Run <paramref name="body"/> over ranges of [0, <paramref name="count"/>) across threads, and return when all are done.
	/// </summary>
	/// <inheritdoc cref="For{TState}(int, TState, Action{TState, int}, int, int)"/>
	public static void ForBatched<TState>( int count, TState state, Action<TState, int, int> body, int itemsPerBatch = 0, int maxDegreeOfParallelism = 0 )
		=> ForBatched( count, new RangeJob<TState>( state, body ), itemsPerBatch, maxDegreeOfParallelism );

	/// <summary>
	/// Run <paramref name="job"/> over ranges of [0, <paramref name="count"/>) across threads, and return when all are done.
	/// Each thread works on its own copy of a struct job; a class job is shared.
	/// </summary>
	/// <inheritdoc cref="For{TState}(int, TState, Action{TState, int}, int, int)"/>
	public static void ForBatched<TJob>( int count, TJob job, int itemsPerBatch = 0, int maxDegreeOfParallelism = 0 ) where TJob : IBatchJob
	{
		if ( count <= 0 ) return;

		// The caller takes part, so it needs one worker fewer than there are threads
		var threads = Math.Min( Degree( maxDegreeOfParallelism ), count );
		Start( count, job, itemsPerBatch, threads, threads - 1 ).Join();
	}

	/// <summary>
	/// Start <paramref name="job"/> on the worker threads and return straight away, so the caller can do other work before
	/// it <see cref="JobHandle.Join"/>s - which it must, exactly once, and which runs whatever is left on the caller.
	/// </summary>
	/// <inheritdoc cref="For{TState}(int, TState, Action{TState, int}, int, int)"/>
	public static JobHandle Begin<TJob>( int count, TJob job, int itemsPerBatch = 0, int maxDegreeOfParallelism = 0 ) where TJob : IBatchJob
	{
		if ( count <= 0 ) return default;

		// The caller is busy until it joins, so workers start on every item they can, a single one included
		var workers = Math.Min( Degree( maxDegreeOfParallelism ) - 1, count );
		return Start( count, job, itemsPerBatch, workers + 1, workers );
	}

	/// <inheritdoc cref="Begin{TJob}(int, TJob, int, int)"/>
	public static JobHandle Begin<TState>( int count, TState state, Action<TState, int> body, int itemsPerBatch = 0, int maxDegreeOfParallelism = 0 )
		=> Begin( count, new ForJob<TState>( state, body ), itemsPerBatch, maxDegreeOfParallelism );

	// A few ranges for each thread, so one slow range doesn't leave the others idle at the end
	const int BatchesPerThread = 4;

	static int Degree( int maxDegreeOfParallelism )
	{
		var degree = maxDegreeOfParallelism > 0 ? Math.Min( maxDegreeOfParallelism, MaxDegreeOfParallelism ) : MaxDegreeOfParallelism;
		return Math.Min( degree, Workers.Count + 1 );
	}

	static JobHandle Start<TJob>( int count, TJob job, int itemsPerBatch, int threads, int workers ) where TJob : IBatchJob
	{
		var perBatch = itemsPerBatch > 0 ? itemsPerBatch : Math.Max( 1, count / (Math.Max( 1, threads ) * BatchesPerThread) );
		workers = Math.Clamp( workers, 0, Workers.Count );

		var batch = Batch<TJob>.Borrow( job, count, perBatch, workers );
		var handle = new JobHandle( batch );
		Workers.Queue( batch, workers );
		return handle;
	}

	[SkipHotload]
	readonly struct ForJob<TState>( TState state, Action<TState, int> body ) : IBatchJob
	{
		public void Process( int start, int endExclusive )
		{
			for ( int i = start; i < endExclusive; i++ )
				body( state, i );
		}
	}

	[SkipHotload]
	readonly struct RangeJob<TState>( TState state, Action<TState, int, int> body ) : IBatchJob
	{
		public void Process( int start, int endExclusive ) => body( state, start, endExclusive );
	}

	/// <summary>
	/// An empty execution context, which jobs run in when the caller suppressed flow, so nothing one sets reaches the next.
	/// </summary>
	static readonly Lazy<ExecutionContext> EmptyContext = new( () =>
	{
		ExecutionContext empty = null;
		var thread = new Thread( () => empty = ExecutionContext.Capture() );
		thread.UnsafeStart();
		thread.Join();
		return empty;
	} );

	/// <summary>
	/// One call's shared state: its claim counter, completion and exception, and its job. Pooled, and reference counted by
	/// the caller and each queued worker entry, so a late entry never sees it reused.
	/// </summary>
	[SkipHotload]
	internal abstract class Batch
	{
		int next;
		int total;
		int perBatch;
		int done;
		int inside;
		int references;
		long ticket;
		static long lastTicket;
		volatile bool failed;
		Exception error;
		ExecutionContext context;
		uint floatingPointMode;
		readonly object gate = new();

		/// <summary>
		/// This use's ticket, which its <see cref="JobHandle"/> hands back to join it: different every time it's borrowed
		/// (a 64-bit count, so it never repeats), never 0, and 0 once joined.
		/// </summary>
		internal long Ticket => Volatile.Read( ref ticket );

		/// <summary>
		/// Claim and run ranges until there are none left or a job has thrown, on this thread's own copy of the job.
		/// </summary>
		protected abstract void ClaimAndRun();

		protected abstract void Recycle();

		protected void Reset( int count, int itemsPerBatch, int queued )
		{
			next = 0;
			total = count;
			perBatch = itemsPerBatch;
			done = 0;
			inside = 0;
			failed = false;
			error = null;
			references = queued + 1;

			var issued = Interlocked.Increment( ref lastTicket );
			Volatile.Write( ref ticket, issued );

			// The caller's context as it stands, no copy, or an empty one when it suppressed flow
			context = ExecutionContext.Capture() ?? EmptyContext.Value;

			// Workers flush denormals exactly as the caller does, or bits of a result would depend on the thread
			floatingPointMode = FloatingPointMode.Current;
		}

		bool Complete => Volatile.Read( ref done ) >= total || (failed && Volatile.Read( ref inside ) == 0);

		/// <summary>Whether there's a range left to claim: a worker waking another for it isn't wasted.</summary>
		internal bool HasWork => !failed && Volatile.Read( ref next ) < total;

		/// <summary>
		/// Claim the next range, or false when there's none left. Never moves past the end, so a late claim can't wrap.
		/// </summary>
		protected bool TryClaim( out int start, out int end )
		{
			while ( !failed )
			{
				start = Volatile.Read( ref next );
				if ( start >= total ) break;

				end = (int)Math.Min( (long)start + perBatch, total );
				if ( Interlocked.CompareExchange( ref next, end, start ) == start ) return true;
			}

			start = end = 0;
			return false;
		}

		protected void Ran( int count )
		{
			if ( Interlocked.Add( ref done, count ) >= total ) Signal();
		}

		protected void Threw( Exception e )
		{
			Interlocked.CompareExchange( ref error, e, null );
			failed = true;
		}

		static readonly ContextCallback WorkIn = state => ((Batch)state).Work();

		void Work()
		{
			Interlocked.Increment( ref inside );
			try
			{
				ClaimAndRun();
			}
			finally
			{
				// After a throw the caller waits for every running job to stop instead of for every item
				if ( Interlocked.Decrement( ref inside ) == 0 && failed ) Signal();
			}
		}

		void Signal()
		{
			lock ( gate ) Monitor.PulseAll( gate );
		}

		void WaitForWorkers()
		{
			lock ( gate )
			{
				while ( !Complete ) Monitor.Wait( gate );
			}
		}

		/// <summary>
		/// A worker's entry: take part if there's anything left, in the batch's context, then let go of it.
		/// </summary>
		internal void RunQueued()
		{
			try
			{
				FloatingPointMode.Set( floatingPointMode );
				ExecutionContext.Run( context, WorkIn, this );
			}
			finally
			{
				Release();
			}
		}

		/// <summary>
		/// Join with the ticket the handle was given: run what's left on the caller, wait for what workers are still
		/// running, then rethrow what was thrown.
		/// </summary>
		internal void Join( long handleTicket )
		{
			// Checks the handle is this use's and takes the join in one step, so a stale handle can't join a later use
			if ( handleTicket == 0 || Interlocked.CompareExchange( ref ticket, 0, handleTicket ) != handleTicket )
				throw new InvalidOperationException( "This JobHandle has already been joined." );

			// No sync context while joining, as on the workers: the main thread's would pump its queue mid-batch, and
			// the runtime allocates a handle array for every blocking wait it forwards to it
			var syncContext = SynchronizationContext.Current;

			Exception thrown;
			try
			{
				SynchronizationContext.SetSynchronizationContext( null );

				// The caller's share runs in the batch's context, and Run puts the caller's own back, before any
				// exception leaves it
				ExecutionContext.Run( context, WorkIn, this );

				// Workers usually finish within a few spins of the caller
				var spin = new SpinWait();
				while ( !Complete && !spin.NextSpinWillYield ) spin.SpinOnce();

				if ( !Complete ) WaitForWorkers();

				thrown = error;
			}
			finally
			{
				// Release takes the pool lock, so the context goes back after it
				try { Release(); }
				finally { SynchronizationContext.SetSynchronizationContext( syncContext ); }
			}

			if ( thrown is not null ) ExceptionDispatchInfo.Throw( thrown );
		}

		void Release()
		{
			if ( Interlocked.Decrement( ref references ) != 0 ) return;

			// Nothing holds it now: drop what the call referenced and put it back for the next
			context = null;
			error = null;
			Recycle();
		}
	}

	[SkipHotload]
	sealed class Batch<TJob> : Batch where TJob : IBatchJob
	{
		static readonly Stack<Batch<TJob>> Pool = new();

		TJob job;

		public static Batch<TJob> Borrow( TJob job, int count, int itemsPerBatch, int queued )
		{
			Batch<TJob> batch;
			lock ( Pool )
			{
				if ( !Pool.TryPop( out batch ) ) batch = new Batch<TJob>();
			}

			batch.job = job;
			batch.Reset( count, itemsPerBatch, queued );
			return batch;
		}

		protected override void ClaimAndRun()
		{
			// Each thread's own copy, kept across its claims, so a struct job's fields are its own
			var local = job;
			while ( TryClaim( out var start, out var end ) )
			{
				try
				{
					local.Process( start, end );
				}
				catch ( Exception e )
				{
					Threw( e );
					return;
				}

				Ran( end - start );
			}
		}

		protected override void Recycle()
		{
			job = default;
			lock ( Pool ) Pool.Push( this );
		}
	}

	/// <summary>
	/// The dispatcher's own threads, one fewer than there are cores, started on first use. They only ever run dispatcher
	/// work, so nothing else blocking them can stall a frame, and they sleep on a last-in-first-out semaphore so the
	/// threads that just ran are the ones woken, still warm.
	/// <para>
	/// The caller wakes only <see cref="CallerWakes"/> of them: a wake is a system call, and on a many-core machine waking
	/// every worker from the main or render thread cost more than the work. Each worker that takes an entry whose batch
	/// still has work wakes up to <see cref="WorkerWakes"/> more, so the rest join in a few steps, off the caller.
	/// </para>
	/// </summary>
	[SkipHotload]
	internal static class Workers
	{
		static readonly WorkQueue queue = new();
		static readonly Lazy<Semaphore> semaphore = new( Start );

		/// <summary>Workers the queuing thread wakes itself.</summary>
		internal const int CallerWakes = 2;

		/// <summary>Workers each woken worker wakes in turn, while wakes are owed and its batch has work left.</summary>
		internal const int WorkerWakes = 2;

		// Wakes owed for entries queued beyond the caller's own
		static int owedWakes;

		/// <summary>
		/// How many worker threads there are: one fewer than there are cores, or two fewer with a render thread
		/// (<see cref="ReserveForRenderThread"/>), so the main thread, the render thread and the workers each have a core.
		/// </summary>
		public static int Count { get; private set; } = Math.Max( 0, Environment.ProcessorCount - 1 );

		static bool reserved;

		/// <summary>
		/// Leave a core for a render thread: one worker fewer. Before the workers start (the first job), one fewer starts;
		/// after, no job is given more than <see cref="Count"/> of them.
		/// </summary>
		internal static void ReserveForRenderThread()
		{
			if ( reserved ) return;
			reserved = true;
			Count = Math.Max( 0, Count - 1 );
		}

		/// <summary>
		/// Whether the workers sleep on the runtime's own LIFO semaphore rather than the fallback.
		/// </summary>
		public static bool UsesLifoSemaphore => semaphore.Value.IsLifo;

		public static void Queue( Batch batch, int entries )
		{
			if ( entries <= 0 ) return;

			queue.Enqueue( batch, entries );

			// Owed first, so a worker woken below can pass them on
			var now = Math.Min( entries, CallerWakes );
			if ( entries > now ) Interlocked.Add( ref owedWakes, entries - now );
			semaphore.Value.Release( now );
		}

		// Pass owed wakes on to sleeping workers, when the batch this worker took still has ranges to claim. Wakes owed
		// for a batch with nothing left are dropped: whichever worker is awake takes its entries off the queue anyway.
		static void PassOnWakes( Batch batch )
		{
			var owed = Volatile.Read( ref owedWakes );
			while ( owed > 0 )
			{
				var take = Math.Min( owed, WorkerWakes );
				var seen = Interlocked.CompareExchange( ref owedWakes, owed - take, owed );
				if ( seen == owed )
				{
					if ( batch.HasWork ) semaphore.Value.Release( take );
					return;
				}

				owed = seen;
			}
		}

		static Semaphore Start()
		{
			var created = Semaphore.Create();
			for ( int i = 0; i < Count; i++ )
			{
				// Unsafe: the threads mustn't keep the context of whatever call happened to start them
				new Thread( Run )
				{
					Name = $"JobDispatcher {i}",
					IsBackground = true,
				}.UnsafeStart();
			}

			return created;
		}

		static void Run()
		{
			var wait = semaphore.Value;
			while ( true )
			{
				while ( queue.TryDequeue( out var batch ) )
				{
					PassOnWakes( batch );
					batch.RunQueued();
				}

				wait.Wait();
			}
		}
	}

	/// <summary>
	/// The workers' queue: a ring buffer under a lock, which only allocates when it grows. A concurrent queue allocates
	/// a share of a new segment for every entry.
	/// </summary>
	[SkipHotload]
	sealed class WorkQueue
	{
		Batch[] entries = new Batch[256];
		int head;
		int count;

		public void Enqueue( Batch batch, int copies )
		{
			lock ( this )
			{
				if ( count + copies > entries.Length )
				{
					var grown = new Batch[Math.Max( entries.Length * 2, count + copies )];
					for ( int i = 0; i < count; i++ ) grown[i] = entries[(head + i) % entries.Length];
					entries = grown;
					head = 0;
				}

				for ( int i = 0; i < copies; i++ )
					entries[(head + count++) % entries.Length] = batch;
			}
		}

		public bool TryDequeue( out Batch batch )
		{
			lock ( this )
			{
				if ( count == 0 )
				{
					batch = null;
					return false;
				}

				batch = entries[head];
				entries[head] = null;
				head = (head + 1) % entries.Length;
				count--;
				return true;
			}
		}
	}

	/// <summary>
	/// What the workers sleep on: the runtime thread pool's internal LIFO semaphore when it can be bound, which spins
	/// before sleeping and wakes the most recent sleeper, else a <see cref="SemaphoreSlim"/>. The runtime's semaphore
	/// changed shape in .NET 11, so both shapes are tried.
	/// </summary>
	[SkipHotload]
	internal sealed class Semaphore
	{
		readonly Action lifoWait;
		readonly Action<int> lifoRelease;
		readonly SemaphoreSlim fallback;

		public bool IsLifo => fallback is null;

		/// <summary>
		/// Which runtime shape was bound: "net10", "net11", or null for the fallback.
		/// </summary>
		public string Binding { get; }

		Semaphore( Action wait, Action<int> release, string binding )
		{
			lifoWait = wait;
			lifoRelease = release;
			Binding = binding;
		}

		Semaphore()
		{
			fallback = new SemaphoreSlim( 0 );
		}

		public static Semaphore Create()
		{
			var type = typeof( object ).Assembly.GetType( "System.Threading.LowLevelLifoSemaphore" );
			if ( type is not null && Bind( type ) is { } lifo ) return lifo;

			Log.Warning( "JobDispatcher couldn't bind the runtime's LIFO semaphore (System.Threading.LowLevelLifoSemaphore), workers wake in turn instead" );
			return new Semaphore();
		}

		const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

		/// <summary>
		/// Bind <paramref name="type"/> as either shape of the runtime's LIFO semaphore, or null when it's neither.
		/// </summary>
		internal static Semaphore Bind( Type type ) => Net10( type ) ?? Net11( type );

		/// <summary>
		/// .NET 10: <c>LowLevelLifoSemaphore( int initialSignalCount, int maximumSignalCount, int spinCount, Action onWait )</c>,
		/// <c>bool Wait( int timeoutMs, bool spinWait )</c> and <c>void Release( int count )</c>.
		/// </summary>
		static Semaphore Net10( Type type )
		{
			try
			{
				var constructor = type.GetConstructor( Members, [typeof( int ), typeof( int ), typeof( int ), typeof( Action )] );
				var wait = type.GetMethod( "Wait", Members, [typeof( int ), typeof( bool )] );
				var release = type.GetMethod( "Release", Members, [typeof( int )] );
				if ( constructor is null || wait?.ReturnType != typeof( bool ) || release is null ) return null;

				var instance = constructor.Invoke( [0, int.MaxValue, 70, new Action( () => { } )] );
				var waitFor = wait.CreateDelegate<Func<int, bool, bool>>( instance );
				return new Semaphore( () => waitFor( -1, true ), release.CreateDelegate<Action<int>>( instance ), "net10" );
			}
			catch ( Exception )
			{
				return null;
			}
		}

		/// <summary>
		/// .NET 11: <c>LowLevelLifoSemaphore( Action onWait )</c>, <c>bool Wait( int timeoutMs )</c>, which spins by itself,
		/// and <c>void Signal()</c>, which wakes one.
		/// </summary>
		static Semaphore Net11( Type type )
		{
			try
			{
				var constructor = type.GetConstructor( Members, [typeof( Action )] );
				var wait = type.GetMethod( "Wait", Members, [typeof( int )] );
				var signal = type.GetMethod( "Signal", Members, Type.EmptyTypes );
				if ( constructor is null || wait?.ReturnType != typeof( bool ) || signal is null ) return null;

				var instance = constructor.Invoke( [new Action( () => { } )] );
				var waitFor = wait.CreateDelegate<Func<int, bool>>( instance );
				var signalOne = signal.CreateDelegate<Action>( instance );
				return new Semaphore( () => waitFor( -1 ), count =>
				{
					for ( int i = 0; i < count; i++ ) signalOne();
				}, "net11" );
			}
			catch ( Exception )
			{
				return null;
			}
		}

		public void Wait()
		{
			if ( fallback is null ) lifoWait();
			else fallback.Wait();
		}

		public void Release( int count )
		{
			if ( fallback is null ) lifoRelease( count );
			else fallback.Release( count );
		}
	}
}

/// <summary>
/// Work started by <see cref="JobDispatcher.Begin{TJob}(int, TJob, int, int)"/>. <see cref="Join"/> it exactly once.
/// </summary>
[SkipHotload]
internal readonly struct JobHandle
{
	readonly JobDispatcher.Batch batch;
	readonly long ticket;

	internal JobHandle( JobDispatcher.Batch batch )
	{
		this.batch = batch;
		ticket = batch.Ticket;
	}

	/// <summary>
	/// Run whatever is left on the calling thread, wait for the rest, then rethrow the first exception if a job threw.
	/// </summary>
	public void Join()
	{
		if ( batch is null ) return;
		batch.Join( ticket );
	}
}
