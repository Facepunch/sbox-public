using System;
using System.Runtime.InteropServices;

namespace EngineTests;

/// <summary>
/// Engine textures' copies on a Borealis.Gpu device (<see cref="BorealisGpuRenderBackend"/>): made from the pixels a texture
/// was made with, by recorded writes rather than ones that wait, and kept current when the texture is updated.
/// </summary>
[TestClass]
public class BorealisGpuTexturesTest
{
	static void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );
	}

	static byte[] Pixels( int width, int height, Func<int, int, Color32> color )
	{
		var pixels = new Color32[width * height];
		for ( int y = 0; y < height; y++ )
			for ( int x = 0; x < width; x++ ) pixels[y * width + x] = color( x, y );

		return MemoryMarshal.AsBytes( pixels.AsSpan() ).ToArray();
	}

	/// <summary>
	/// A copy is made from the texture's own pixels with a recorded write, and an update of part of the texture lands in
	/// the copy without dropping it or reading Source 2's pixels back.
	/// </summary>
	[TestMethod]
	public void UpdatesLandInTheCopy()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 32;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, ( x, y ) => new Color32( (byte)x, (byte)y, 0, 255 ) ) ).Finish();

		var before = borealis.ReadPixels( texture );
		Assert.AreEqual( new Color32( 5, 7, 0, 255 ), before[7 * size + 5], "the copy has the texture's pixels" );
		Assert.IsTrue( borealis.TextureUploads > 0, "the copy was written by a recorded upload" );

		// A red square in the middle, on Source 2 and in the copy
		texture.Update( Pixels( 8, 8, ( _, _ ) => new Color32( 255, 0, 0, 255 ) ), 12, 12, 8, 8 );
		var after = borealis.ReadPixels( texture );

		Assert.AreEqual( new Color32( 255, 0, 0, 255 ), after[16 * size + 16], "the update landed in the copy" );
		Assert.AreEqual( new Color32( 5, 7, 0, 255 ), after[7 * size + 5], "outside the update, the copy is as it was" );
		Assert.AreEqual( 0, borealis.CopiedFromSource2, "nothing was read back from Source 2" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// Starting the device's next frame starts the renderer services' too, so GPU-driven drawing's per-frame memory comes
	/// back rather than growing until the bindless table runs out of slots.
	/// </summary>
	[TestMethod]
	public void NextFrameStartsTheServicesFrame()
	{
		// A device of its own: no Source 2 renderer needed
		using var borealis = BorealisGpuRenderBackend.Create();

		var before = borealis.ServicesFrame;
		borealis.NextFrame();
		borealis.NextFrame();
		Assert.AreEqual( before + 2, borealis.ServicesFrame );
	}
}
