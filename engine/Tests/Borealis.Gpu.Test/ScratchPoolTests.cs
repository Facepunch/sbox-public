using Borealis.Backend;

namespace BorealisGpuTests;

/// <summary>
/// LOD and meshlet builds' scratch arrays are kept between uses, and let go once nothing has rented any for a while (a
/// load has finished): in the shared array pool about 240 MB stayed for minutes after Construct's load.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ScratchPoolTests
{
	[TestMethod]
	public void KeptArraysGoOnceNothingRents()
	{
		ScratchPools.Clear();
		var array = ScratchPool<uint>.Rent( 1000 );
		Assert.AreEqual( 1024, array.Length, "rounded up to a power of two" );
		ScratchPool<uint>.Return( array );
		ScratchPool<float>.Return( ScratchPool<float>.Rent( 1 << 16 ) );
		Assert.AreEqual( 4096L + (4L << 16), ScratchPools.KeptBytes, "returned arrays are kept, counted in bytes across element types" );
		Assert.AreSame( array, ScratchPool<uint>.Rent( 1024 ), "and rented again" );
		ScratchPool<uint>.Return( array );

		ScratchPools.TrimIdle( 3600 );
		Assert.AreEqual( 4096L + (4L << 16), ScratchPools.KeptBytes, "kept while builds rented lately" );

		ScratchPools.TrimIdle( 0 );
		Assert.AreEqual( 0L, ScratchPools.KeptBytes, "let go once nothing has rented for the idle time" );
		Assert.AreNotSame( array, ScratchPool<uint>.Rent( 1024 ), "made again after" );
	}

	[TestMethod]
	public void KeepsNoMoreThanTheRoom()
	{
		var max = ScratchPools.MaxKeptBytes;
		ScratchPools.Clear();
		try
		{
			ScratchPools.MaxKeptBytes = 1 << 20;
			var arrays = new byte[8][];
			for ( int i = 0; i < arrays.Length; i++ ) arrays[i] = ScratchPool<byte>.Rent( 256 << 10 );
			foreach ( var array in arrays ) ScratchPool<byte>.Return( array );
			Assert.AreEqual( 1L << 20, ScratchPools.KeptBytes, "those past the room are let go" );

			// Not from the pool: not kept
			ScratchPool<byte>.Return( new byte[1000] );
			Assert.AreEqual( 1L << 20, ScratchPools.KeptBytes );
		}
		finally
		{
			ScratchPools.MaxKeptBytes = max;
			ScratchPools.Clear();
		}
	}
}
