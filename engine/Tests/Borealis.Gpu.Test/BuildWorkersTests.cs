using Borealis.Backend.Lods;
using Borealis.Backend.Meshlets;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
	/// <summary>A build's failure message, held at the first one until released: a build still running.</summary>
	sealed class HoldingWriter : StringWriter
	{
		int lines;
		public readonly ManualResetEventSlim Entered = new(), Released = new();
		public int Lines => Volatile.Read( ref lines );

		public override void WriteLine( string value )
		{
			Interlocked.Increment( ref lines );
			Entered.Set();
			Released.Wait( TimeSpan.FromSeconds( 30 ) );
		}
	}

	/// <summary>
	/// A meshlet store's Dispose waits for the build running on its worker, and the ones still queued never run: a build
	/// making its staging buffer after the device went crashed the GPU tests' host.
	/// </summary>
	[TestMethod]
	public void MeshletBuildsInFlightFinishBeforeTheStoreGoes()
	{
		// Disposed by the check itself, on another thread
#pragma warning disable CA2000
		var meshlets = new MeshletStore( Device, MeshletConfig.Default, 1 );
#pragma warning restore CA2000
		var source = new MeshletStore.Source( default, Borealis.Gpu.IndexFormat.UInt32, null, 0, null, default, 0 );
		BuildsInFlightFinishBeforeTheStoreGoes( meshlets, i => meshlets.Request( new MeshletStore.Range( 0, i * 3, 3, 0 ), source ) );
	}

	/// <summary>
	/// A LOD store's Dispose waits for the build running on its worker, and the ones still queued never run, as the meshlet
	/// store's: a build staging its levels after the device went would make a buffer on a device that's gone.
	/// </summary>
	[TestMethod]
	public void LodBuildsInFlightFinishBeforeTheStoreGoes()
	{
		using var tables = new Borealis.Backend.GpuDrivenTables( Device );
		// Disposed by the check itself, on another thread
#pragma warning disable CA2000
		var lods = new LodStore( Device, tables, LodConfig.Default, 1 );
#pragma warning restore CA2000
		var source = new MeshletStore.Source( default, Borealis.Gpu.IndexFormat.UInt32, null, 0, null, default, 0 );
		var count = (uint)LodConfig.Default.MinRangeTriangles * 3;
		BuildsInFlightFinishBeforeTheStoreGoes( lods, i => lods.Request( new MeshletStore.Range( 0, i * count, count, 0 ), source, true ) );
	}

	static void BuildsInFlightFinishBeforeTheStoreGoes( IDisposable store, Action<uint> request )
	{
		// Sources with no buffers fail at once and say so on the error stream, which holds the first build there
		var error = Console.Error;
		using var held = new HoldingWriter();
		Console.SetError( held );
		try
		{
			for ( uint i = 0; i < 4; i++ ) request( i );
			Assert.IsTrue( held.Entered.Wait( TimeSpan.FromSeconds( 10 ) ), "the first build never ran" );

			var disposing = Task.Run( store.Dispose );
			Assert.IsFalse( disposing.Wait( 300 ), "Dispose returned while a build was still running" );
			held.Released.Set();
			Assert.IsTrue( disposing.Wait( TimeSpan.FromSeconds( 10 ) ), "Dispose didn't return once the build finished" );

			// The three queued behind it are dropped, not built on a store (and device) that's gone
			Thread.Sleep( 100 );
			Assert.AreEqual( 1, held.Lines, "builds ran after the store went" );
		}
		finally
		{
			held.Released.Set();
			Console.SetError( error );
		}
	}
}
