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
}
