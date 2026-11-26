using Borealis.Source2.Compression;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>Zstandard decoding's scratch is kept between decodes, whichever thread decodes.</summary>
[TestClass]
[DoNotParallelize]
public class ZstdTests
{
	/// <summary>One frame holding <paramref name="data"/> as a raw block, its size in the header.</summary>
	static byte[] RawFrame( byte[] data )
	{
		var block = 1 | (data.Length << 3);
		return [0x28, 0xB5, 0x2F, 0xFD, 0x20, (byte)data.Length, (byte)block, (byte)(block >> 8), (byte)(block >> 16), .. data];
	}

	[TestMethod]
	public void ARawFrameDecodes()
	{
		var frame = RawFrame( [1, 2, 3, 4, 5] );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4, 5 }, Zstd.Decompress( frame, 5 ) );
	}

	/// <summary>
	/// A thread that hasn't decoded before (the thread pool's workers that stream textures come and go) takes a kept scratch
	/// rather than making its own.
	/// </summary>
	[TestMethod]
	public void ANewThreadsDecodeAllocatesNothing()
	{
		var frame = RawFrame( [1, 2, 3, 4, 5, 6, 7, 8] );
		var output = new byte[8];
		Zstd.Decompress( frame, output );

		long allocated = -1;
		var thread = new Thread( () =>
		{
			Zstd.Decompress( frame, output );
			var before = GC.GetAllocatedBytesForCurrentThread();
			Zstd.Decompress( frame, output );
			allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		} );

		// The first decode on the thread is measured too: it mustn't make a scratch of its own
		long first = -1;
		var fresh = new Thread( () =>
		{
			var before = GC.GetAllocatedBytesForCurrentThread();
			Zstd.Decompress( frame, output );
			first = GC.GetAllocatedBytesForCurrentThread() - before;
		} );

		thread.Start();
		thread.Join();
		fresh.Start();
		fresh.Join();
		Assert.AreEqual( 0, allocated, "a thread's second decode" );
		Assert.AreEqual( 0, first, "a new thread's first decode" );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, output );
	}
}
