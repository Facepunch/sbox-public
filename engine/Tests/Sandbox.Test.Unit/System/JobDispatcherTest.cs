using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SystemTests;

[TestClass]
public class JobDispatcherTest
{
	// Workers only take part when there are some and parallelism isn't capped at 1 (SBOX_MAX_PARALLELISM)
	static bool Parallel => JobDispatcher.Workers.Count > 0 && JobDispatcher.MaxDegreeOfParallelism > 1;

	[TestMethod]
	public void RunsEveryItemOnce()
	{
		foreach ( var count in new[] { 1, 2, 7, 64, 1000, 4097 } )
		{
			foreach ( var perBatch in new[] { 0, 1, 13 } )
			{
				var hits = new int[count];
				for ( int run = 0; run < 5; run++ )
					JobDispatcher.For( count, hits, static ( h, i ) => Interlocked.Increment( ref h[i] ), itemsPerBatch: perBatch );

				Assert.IsTrue( hits.All( x => x == 5 ), $"{count} items, {perBatch} a batch" );
			}
		}
	}

	[TestMethod]
	public void BatchesCoverTheRangeWithoutOverlap()
	{
		var hits = new int[10_000];
		var ranges = 0;
		JobDispatcher.ForBatched( hits.Length, hits, ( h, start, end ) =>
		{
			Interlocked.Increment( ref ranges );
			for ( int i = start; i < end; i++ ) Interlocked.Increment( ref h[i] );
		} );

		Assert.IsTrue( hits.All( x => x == 1 ) );
		Assert.IsTrue( ranges < hits.Length, $"{ranges} ranges: claimed in ranges, not one by one" );
		if ( Parallel ) Assert.IsTrue( ranges > 1, "split between threads" );
	}

	[TestMethod]
	public void HugeBatchesDoNotWrap()
	{
		// Claims stop at the end rather than adding past it, so a late claim can't overflow into a range below zero
		for ( int run = 0; run < 200; run++ )
		{
			var hits = new int[2];
			var bad = 0;
			JobDispatcher.ForBatched( hits.Length, hits, ( h, start, end ) =>
			{
				if ( start < 0 || end > h.Length || start >= end ) Interlocked.Increment( ref bad );
				else for ( int i = start; i < end; i++ ) Interlocked.Increment( ref h[i] );
			}, itemsPerBatch: int.MaxValue );

			Assert.AreEqual( 0, bad );
			Assert.IsTrue( hits.All( x => x == 1 ) );
		}
	}

	[TestMethod]
	public void HugeCountsKeepToTheirDegreeOfParallelism()
	{
		// Counting the caller in mustn't overflow at the top of the range and let a serial call run on workers
		var caller = Environment.CurrentManagedThreadId;
		var elsewhere = 0;
		JobDispatcher.Begin( int.MaxValue, new RangeCheck { Caller = caller, Elsewhere = () => Interlocked.Increment( ref elsewhere ) }, itemsPerBatch: int.MaxValue / 4, maxDegreeOfParallelism: 1 ).Join();
		Assert.AreEqual( 0, elsewhere );
	}

	struct RangeCheck : JobDispatcher.IBatchJob
	{
		public int Caller;
		public Action Elsewhere;

		public void Process( int start, int endExclusive )
		{
			if ( Environment.CurrentManagedThreadId != Caller ) Elsewhere();
		}
	}

	[TestMethod]
	public void EmptyAndSingleRunOnCaller()
	{
		var caller = Environment.CurrentManagedThreadId;
		var calls = 0;
		var thread = -1;

		JobDispatcher.For( 0, 0, ( _, i ) => calls++ );
		Assert.AreEqual( 0, calls );

		JobDispatcher.For( 1, 0, ( _, i ) => { calls++; thread = Environment.CurrentManagedThreadId; } );
		Assert.AreEqual( 1, calls );
		Assert.AreEqual( caller, thread );

		// One thread at most: everything on the caller
		var threads = new HashSet<int>();
		JobDispatcher.For( 256, 0, ( _, i ) => threads.Add( Environment.CurrentManagedThreadId ), maxDegreeOfParallelism: 1 );
		Assert.IsTrue( threads.SetEquals( [caller] ) );
	}

	[TestMethod]
	public void KeepsToItsDegreeOfParallelism()
	{
		var threads = new int[512];
		JobDispatcher.For( threads.Length, threads, static ( t, i ) =>
		{
			Thread.SpinWait( 2000 );
			t[i] = Environment.CurrentManagedThreadId;
		}, itemsPerBatch: 1, maxDegreeOfParallelism: 2 );

		Assert.IsTrue( threads.Distinct().Count() <= 2 );
	}

	[TestMethod]
	public void WorkersSeeTheCallersAsyncLocals()
	{
		// GlobalContext.Current is an async local: menu scene work on workers has to see the menu's context
		var local = new AsyncLocal<int>();
		var seen = new int[256];
		var threads = new int[256];
		var ran = 0;

		for ( int value = 1; value <= 3; value++ )
		{
			local.Value = value;
			ran = 0;
			var handle = JobDispatcher.Begin( seen.Length, 0, ( _, i ) =>
			{
				seen[i] = local.Value;
				threads[i] = Environment.CurrentManagedThreadId;
				Interlocked.Increment( ref ran );
			}, itemsPerBatch: 1 );

			// Workers start before the caller joins: give them the chance to run some
			if ( Parallel ) SpinWait.SpinUntil( () => Volatile.Read( ref ran ) > 0, 5000 );
			handle.Join();
			Assert.IsTrue( seen.All( x => x == value ), $"run {value}" );
		}

		if ( Parallel ) Assert.IsTrue( threads.Any( x => x != Environment.CurrentManagedThreadId ), "Workers took part" );
	}

	[TestMethod]
	public void JobsDoNotChangeTheCallersContext()
	{
		// A job run on the caller, such as a user callback setting an async local, mustn't leave it changed there
		var local = new AsyncLocal<int>();
		local.Value = 1;

		JobDispatcher.For( 1, local, static ( l, i ) => l.Value = 2 );
		Assert.AreEqual( 1, local.Value, "one item, run on the caller alone" );

		// Workers hold on to their first item until the caller has run one, so the caller always takes part
		var caller = Environment.CurrentManagedThreadId;
		using var callerRan = new ManualResetEventSlim();
		JobDispatcher.For( 64, local, ( l, i ) =>
		{
			l.Value = 2;
			if ( Environment.CurrentManagedThreadId == caller ) callerRan.Set();
			else callerRan.Wait( 5000 );
		}, itemsPerBatch: 1, maxDegreeOfParallelism: 4 );

		Assert.IsTrue( callerRan.IsSet );
		Assert.AreEqual( 1, local.Value, "the caller's share of a parallel run" );
	}

	[TestMethod]
	public void ExceptionFiltersSeeTheCallersContext()
	{
		// The job's context is undone before the exception reaches the caller's handlers, filters included
		var local = new AsyncLocal<int>();
		local.Value = 1;
		var caught = false;

		try
		{
			JobDispatcher.For( 1, local, static ( l, i ) =>
			{
				l.Value = 2;
				throw new InvalidOperationException();
			} );
		}
		catch ( InvalidOperationException ) when ( local.Value == 1 )
		{
			caught = true;
		}

		Assert.IsTrue( caught );
	}

	[TestMethod]
	public void RunsWithFlowSuppressed()
	{
		// With flow suppressed jobs run in an empty context: nothing of the caller's, and nothing a job sets reaches a later
		// call's jobs, on the caller or a worker. (Jobs one thread runs in the same call share its context, as a loop would.)
		var local = new AsyncLocal<int>();
		local.Value = 1;
		var seen = new int[256];

		using ( ExecutionContext.SuppressFlow() )
		{
			for ( int run = 0; run < 3; run++ )
			{
				var marker = 100 + run;
				JobDispatcher.For( seen.Length, local, ( l, i ) =>
				{
					seen[i] = l.Value;
					l.Value = marker;
				}, itemsPerBatch: 1 );

				Assert.IsTrue( seen.All( x => x == 0 || x == marker ), $"run {run}: {string.Join( ",", seen.Distinct() )}" );
				Assert.AreEqual( 1, local.Value );
			}
		}

		// Started with flow, joined with it suppressed: joining leaves the suppression as it was
		var handle = JobDispatcher.Begin( 64, 0, static ( _, i ) => { } );
		using ( ExecutionContext.SuppressFlow() ) handle.Join();
	}

	[TestMethod]
	public void StructJobsAreCopiedForEachThread()
	{
		// A struct job's fields are each thread's own: a count kept in one only sees that thread's ranges
		var calls = new ConcurrentQueue<(int Thread, int Calls)>();
		JobDispatcher.ForBatched( 4096, new CountingJob { Log = calls }, itemsPerBatch: 1 );

		Assert.AreEqual( 4096, calls.Count );
		foreach ( var thread in calls.GroupBy( x => x.Thread ) )
		{
			var counts = thread.Select( x => x.Calls ).ToArray();
			CollectionAssert.AreEqual( Enumerable.Range( 1, counts.Length ).ToArray(), counts, $"thread {thread.Key}" );
		}
	}

	struct CountingJob : JobDispatcher.IBatchJob
	{
		public int Calls;
		public ConcurrentQueue<(int Thread, int Calls)> Log;

		public void Process( int start, int endExclusive )
		{
			Calls++;
			Log.Enqueue( (Environment.CurrentManagedThreadId, Calls) );
		}
	}

	[TestMethod]
	public void RethrowsAfterJoiningAndCarriesOn()
	{
		var running = 0;
		var e = Assert.ThrowsException<InvalidOperationException>( () => JobDispatcher.For( 64, 0, ( _, i ) =>
		{
			Interlocked.Increment( ref running );
			try
			{
				if ( i == 3 ) throw new InvalidOperationException( "item 3" );
				Thread.SpinWait( 1000 );
			}
			finally
			{
				Interlocked.Decrement( ref running );
			}
		}, itemsPerBatch: 1 ) );

		Assert.AreEqual( "item 3", e.Message );
		Assert.IsTrue( e.StackTrace.Contains( nameof( RethrowsAfterJoiningAndCarriesOn ) ), "thrown with the job's own stack" );
		Assert.AreEqual( 0, Volatile.Read( ref running ), "Every running job is joined before the throw" );

		var hits = new int[64];
		JobDispatcher.For( hits.Length, hits, static ( h, i ) => Interlocked.Increment( ref h[i] ) );
		Assert.IsTrue( hits.All( x => x == 1 ) );
	}

	[TestMethod]
	public void BeginThenJoin()
	{
		var hits = new int[1000];
		var handle = JobDispatcher.Begin( hits.Length, hits, static ( h, i ) => Interlocked.Increment( ref h[i] ), itemsPerBatch: 1 );

		// The caller's own work, while workers start on the batch
		Thread.SpinWait( 1000 );

		handle.Join();
		Assert.IsTrue( hits.All( x => x == 1 ) );
		Assert.ThrowsException<InvalidOperationException>( () => handle.Join() );

		// A stale handle can't join a later use of the same pooled state
		var next = JobDispatcher.Begin( hits.Length, hits, static ( h, i ) => Interlocked.Increment( ref h[i] ), itemsPerBatch: 1 );
		Assert.ThrowsException<InvalidOperationException>( () => handle.Join() );
		next.Join();
		Assert.IsTrue( hits.All( x => x == 2 ) );

		// Nothing to do: a default handle, which joins as nothing
		JobDispatcher.Begin( 0, hits, static ( h, i ) => { } ).Join();
	}

	[TestMethod]
	public void BeginStartsEvenOneItemOnAWorker()
	{
		// The caller is busy with its own work until it joins, so a single item mustn't wait for it
		if ( !Parallel ) return;

		var thread = 0;
		var handle = JobDispatcher.Begin( 1, 0, ( _, i ) => Volatile.Write( ref thread, Environment.CurrentManagedThreadId ) );
		Assert.IsTrue( SpinWait.SpinUntil( () => Volatile.Read( ref thread ) != 0, 5000 ), "ran before the join" );
		handle.Join();
		Assert.AreNotEqual( Environment.CurrentManagedThreadId, thread );
	}

	[TestMethod]
	public void NestedDispatchDoesNotDeadlock()
	{
		// More outer items than workers, each dispatching its own inner work: callers claim their own work, so nothing
		// waits on a worker that's busy waiting itself
		var inner = 0;
		var outerCount = Environment.ProcessorCount * 4;

		var watch = System.Diagnostics.Stopwatch.StartNew();
		for ( int run = 0; run < 50; run++ )
		{
			JobDispatcher.For( outerCount, 0, ( _, i ) =>
				JobDispatcher.For( 16, 0, ( _, j ) => { Interlocked.Increment( ref inner ); Thread.SpinWait( 100 ); }, itemsPerBatch: 1 ), itemsPerBatch: 1 );
		}

		Assert.AreEqual( 50 * outerCount * 16, inner );
		Assert.IsTrue( watch.Elapsed.TotalSeconds < 10, $"took {watch.Elapsed.TotalSeconds:F1}s" );
	}

	[TestMethod]
	public void DoesNotAllocateAfterWarmup()
	{
		var sum = new long[1];
		for ( int run = 0; run < 2000; run++ )
			JobDispatcher.For( 256, sum, static ( s, i ) => Interlocked.Add( ref s[0], i ) );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int run = 0; run < 1000; run++ )
			JobDispatcher.For( 256, sum, static ( s, i ) => Interlocked.Add( ref s[0], i ) );
		var perRun = (GC.GetAllocatedBytesForCurrentThread() - before) / 1000.0;

		// The pool and queue only grow when more calls are in flight than ever before
		Assert.IsTrue( perRun < 16, $"{perRun:F1} bytes per run" );
		Assert.AreEqual( 3000L * (255 * 256 / 2), Interlocked.Read( ref sum[0] ) );
	}

	[TestMethod]
	public void WorkersSleepOnTheRuntimesLifoSemaphore()
	{
		// Bound by reflection: tells us when a runtime update moves it, rather than quietly falling back
		if ( JobDispatcher.Workers.Count == 0 ) return;

		JobDispatcher.For( 64, 0, static ( _, i ) => { } );
		Assert.IsTrue( JobDispatcher.Workers.UsesLifoSemaphore );
	}

	/// <summary>
	/// The runtime's LIFO semaphore changed shape in .NET 11; both shapes bind, checked with stand-ins of each since only
	/// one runtime runs the tests.
	/// </summary>
	[TestMethod]
	public void BindsEitherShapeOfTheLifoSemaphore()
	{
		var net10 = JobDispatcher.Semaphore.Bind( typeof( Net10Shape ) );
		Assert.AreEqual( "net10", net10?.Binding );
		net10.Release( 3 );
		net10.Wait();
		Assert.AreEqual( 2, Net10Shape.Last.Count );

		var net11 = JobDispatcher.Semaphore.Bind( typeof( Net11Shape ) );
		Assert.AreEqual( "net11", net11?.Binding );
		net11.Release( 3 );
		net11.Wait();
		Assert.AreEqual( 2, Net11Shape.Last.Count, "a release of 3 is 3 signals" );

		Assert.IsNull( JobDispatcher.Semaphore.Bind( typeof( object ) ), "neither shape: the fallback" );
	}

	sealed class Net10Shape
	{
		[ThreadStatic] public static Net10Shape Last;
		public int Count;

		public Net10Shape( int initialSignalCount, int maximumSignalCount, int spinCount, Action onWait ) => Last = this;

		public bool Wait( int timeoutMs, bool spinWait )
		{
			Count--;
			return true;
		}

		public void Release( int count ) => Count += count;
	}

	sealed class Net11Shape
	{
		[ThreadStatic] public static Net11Shape Last;
		public int Count;

		public Net11Shape( Action onWait ) => Last = this;

		public bool Wait( int timeoutMs )
		{
			Count--;
			return true;
		}

		public void Signal() => Count++;
	}
}
