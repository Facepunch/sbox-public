using Borealis.Source2;

namespace BorealisGpuTests;

/// <summary>
/// The arrays content streams through are kept between uses, and let go once nothing has streamed for a while (a map has
/// gone): up to 128 MiB stayed at the menu after a map.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContentBuffersTests
{
	[TestMethod]
	public void KeptArraysGoOnceNothingStreams()
	{
		ContentBuffers.Clear();
		ContentBuffers.Return( ContentBuffers.Rent( 1 << 20 ) );
		Assert.IsTrue( ContentBuffers.KeptBytes >= 1 << 20, "a returned array is kept for the next rent" );

		ContentBuffers.TrimIdle( 3600 );
		Assert.IsTrue( ContentBuffers.KeptBytes >= 1 << 20, "kept while content streamed lately" );

		ContentBuffers.TrimIdle( 0 );
		Assert.AreEqual( 0L, ContentBuffers.KeptBytes, "let go once nothing has streamed for the idle time" );
	}

	/// <summary>
	/// With the kept arrays full of small mips' arrays (as a load leaves them), a big file buffer streaming rents now and then
	/// is kept for its next rent, others making room, instead of being let go and made again each time.
	/// </summary>
	[TestMethod]
	public void ABigBufferIsKeptWhenSmallOnesFillTheRoom()
	{
		var max = ContentBuffers.MaxKeptBytes;
		ContentBuffers.Clear();
		try
		{
			ContentBuffers.MaxKeptBytes = 16 << 20;
			var big = ContentBuffers.Rent( 4 << 20 );
			var small = new byte[(16 << 20) / (64 << 10)][];
			for ( int i = 0; i < small.Length; i++ ) small[i] = ContentBuffers.Rent( 64 << 10 );
			foreach ( var array in small ) ContentBuffers.Return( array );
			Assert.AreEqual( 16L << 20, ContentBuffers.KeptBytes, "the small arrays fill the room" );

			// The small size was rented after the big one, so it isn't the staler
			ContentBuffers.Return( big );
			var made = ContentBuffers.MadeBytes;
			for ( int i = 0; i < 4; i++ ) ContentBuffers.Return( ContentBuffers.Rent( 4 << 20 ) );
			Assert.AreEqual( made, ContentBuffers.MadeBytes, "the big buffer was kept and rented again" );
			Assert.IsTrue( ContentBuffers.KeptBytes <= 16L << 20, "within the room" );
		}
		finally
		{
			ContentBuffers.MaxKeptBytes = max;
			ContentBuffers.Clear();
		}
	}
}
