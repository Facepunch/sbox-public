using Borealis.Backend.Meshlets;
using System.IO;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// Where the meshlet and LOD stores build their ranges: on a scheduler of their own that runs as many at once as the store
/// has threads and keeps the rest queued, not as thread pool work items that each wait for a thread of the store's.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BuildWorkersTests : GpuTest
{
	sealed class CountingWriter : StringWriter
	{
		int lines;
		public int Lines => Volatile.Read( ref lines );
		public override void WriteLine( string value ) => Interlocked.Increment( ref lines );
	}

	/// <summary>
	/// Ranges asked for faster than they build wait in the store's queue: the thread pool holds none of them. Before, each was
	/// a pool work item that took a pool thread and blocked it until one of the store's threads was free, so a map's
	/// thousands of ranges starved the pool (Construct: 3800 items waiting, 460 threads); the load's own work on the pool (its
	/// navmesh tiles, timers, continuations) waited behind them for seconds.
	/// </summary>
	[TestMethod]
	public void QueuedRangesWaitOutsideTheThreadPool()
	{
		const int ranges = 500;
		using var meshlets = new MeshletStore( Device, MeshletConfig.Default, 1 );

		// A source with no buffers: each build fails at once (and says so on the error stream), which is all this needs
		var source = new MeshletStore.Source( default, Borealis.Gpu.IndexFormat.UInt32, null, 0, null, default, 0 );
		var error = Console.Error;
		using var failures = new CountingWriter();
		Console.SetError( failures );
		try
		{
			for ( uint i = 0; i < ranges; i++ ) meshlets.Request( new MeshletStore.Range( 0, i * 3, 3, 0 ), source );
			var waiting = ThreadPool.PendingWorkItemCount;
			Assert.AreEqual( ranges, meshlets.Requested );
			Assert.IsTrue( waiting < 50, $"{waiting} work items waiting in the thread pool for {ranges} ranges" );

			// Every one is built (fails) in the end, before the store goes
			Assert.IsTrue( SpinWait.SpinUntil( () => failures.Lines >= ranges, TimeSpan.FromSeconds( 30 ) ), $"{failures.Lines} of {ranges} built" );
		}
		finally
		{
			Console.SetError( error );
		}
	}
}
