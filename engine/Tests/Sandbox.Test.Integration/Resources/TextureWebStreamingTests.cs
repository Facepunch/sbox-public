using System;
using System.IO;

namespace ResourceTests;

[TestClass]
public class TextureWebStreamingTests
{
	[TestMethod]
	public void PrefetchWaitsDuringLoadingAndDiscardsPreviousGameRequests()
	{
		var generation = NativeGlue.Resources.CancelTextureWebStreamingPrefetch();
		try
		{
			NativeGlue.Resources.SetTextureWebStreamingState( true, 1024 );
			Assert.IsFalse( NativeGlue.Resources.PrefetchTextureWebStreaming( "unused.vtex_c", "https://textures.invalid/unused", 4096, generation ) );
			NativeGlue.Resources.CancelTextureWebStreamingPrefetch();
			Assert.IsTrue( NativeGlue.Resources.PrefetchTextureWebStreaming( "unused.vtex_c", "https://textures.invalid/unused", 4096, generation ) );
			Assert.IsFalse( NativeGlue.Resources.HasTextureWebStreamingRequests() );
		}
		finally
		{
			NativeGlue.Resources.SetTextureWebStreamingState( false, 128 * 1024 );
		}
	}

	[TestMethod]
	public void PrefetchReusesResidentCacheWithoutResourceCallbacks()
	{
		var path = Path.Combine( Path.GetTempPath(), $"texture-prefetch-{Guid.NewGuid():N}.vtex_c" );
		var bytes = Enumerable.Range( 0, 512 ).Select( i => (byte)i ).ToArray();
		try
		{
			File.WriteAllBytes( path, bytes[..128] );
			File.WriteAllBytes( path + ".resident", bytes );
			var generation = NativeGlue.Resources.CancelTextureWebStreamingPrefetch();
			NativeGlue.Resources.SetTextureWebStreamingState( false, 128 * 1024 );
			Assert.IsTrue( NativeGlue.Resources.PrefetchTextureWebStreaming( path, "https://textures.invalid/unused", bytes.Length, generation ) );
			Assert.IsFalse( NativeGlue.Resources.HasTextureWebStreamingRequests() );
			Assert.IsFalse( NativeGlue.Resources.IsTextureWebStreamingActive() );
			CollectionAssert.AreEqual( bytes, File.ReadAllBytes( path + ".resident" ) );
		}
		finally
		{
			File.Delete( path );
			File.Delete( path + ".resident" );
		}
	}
}
