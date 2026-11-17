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
	/// A texture's pixels read back from the device's copy (what <see cref="Texture.GetPixels(int)"/> reads when the
	/// device is the process's): a rectangle, converted to the format asked for, at a place in a bigger image.
	/// </summary>
	[TestMethod]
	public unsafe void ReadsPixelsBackFromTheCopy()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, ( x, y ) => new Color32( (byte)x, (byte)y, 0, 255 ) ) ).Finish();

		// 3x2 pixels from (4, 5), as BGRA, into an 8 pixel wide image at (1, 1)
		var image = new byte[8 * 4 * 4];
		fixed ( byte* data = image )
		{
			Assert.IsTrue( borealis.ReadPixels( texture, new NativeRect( 4, 5, 3, 2 ), 0, 0, new NativeRect( 1, 1, 3, 2 ), (IntPtr)data, ImageFormat.BGRA8888, 8 * 4 ) );
		}

		byte[] At( int x, int y ) => image.AsSpan( (y * 8 + x) * 4, 4 ).ToArray();
		CollectionAssert.AreEqual( new byte[] { 0, 5, 4, 255 }, At( 1, 1 ), "the rectangle's first pixel, as BGRA" );
		CollectionAssert.AreEqual( new byte[] { 0, 6, 6, 255 }, At( 3, 2 ), "its last" );
		CollectionAssert.AreEqual( new byte[4], At( 0, 0 ), "outside the rectangle, untouched" );
		Assert.AreEqual( 0, borealis.CopiedFromSource2, "nothing was read back from Source 2" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// With the device the process's, reading a texture back - at once or asynchronously - reads the device's copy.
	/// </summary>
	[TestMethod]
	public void TextureReadsGoToTheProcessDevice()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		RenderBackend.SetDefault( borealis );
		try
		{
			const int size = 8;
			using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, ( x, y ) => new Color32( (byte)(x * 10), (byte)(y * 10), 7, 255 ) ) ).Finish();

			Assert.AreEqual( new Color32( 30, 50, 7, 255 ), texture.GetPixels()[5 * size + 3] );
			Assert.AreEqual( new Color32( 70, 10, 7, 255 ), texture.GetPixel( 7, 1 ) );

			Color32[] read = null;
			using var done = new System.Threading.ManualResetEventSlim();
			texture.GetPixelsAsync( pixels => { read = pixels.ToArray(); done.Set(); } );
			Assert.IsTrue( done.Wait( 10000 ), "the asynchronous read called back" );
			Assert.AreEqual( size * size, read.Length );
			Assert.AreEqual( new Color32( 30, 50, 7, 255 ), read[5 * size + 3] );
		}
		finally
		{
			RenderBackend.SetDefault( null );
		}

		Assert.AreEqual( 0, borealis.CopiedFromSource2, "nothing was read back from Source 2" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A texture read from a material, with no path of its own (a sky's cubemap), is the material's texture as Borealis's
	/// draws read it, not a copy of Source 2's pixels.
	/// </summary>
	[TestMethod]
	public void MaterialTexturesAreTheMaterials()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
		var texture = material.GetTexture( "g_tSkyTexture" );
		Assert.IsNotNull( texture );

		var expected = (Borealis.Backend.BackendTexture)borealis.Services.MaterialTexture( material.ResourcePath, "g_tSkyTexture" );
		Assert.IsNotNull( expected );
		Assert.AreEqual( expected.Handle, borealis.TextureOf( texture ) );
		Assert.AreEqual( 0, borealis.CopiedFromSource2, "nothing was read back from Source 2" );
	}

	/// <summary>
	/// Without Source 2's device (the game under -borealisgpu), a GPU buffer is the process's device's alone: no Source 2
	/// buffer is made, it reads back from the device, and it's valid until disposed.
	/// </summary>
	[TestMethod]
	public void BuffersWithoutSource2DeviceAreTheDevices()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var hadDevice = Graphics.HasSource2Device;
		RenderBackend.SetDefault( borealis );
		Graphics.HasSource2Device = false;
		try
		{
			var buffer = new GpuBuffer<uint>( 4 );
			Assert.IsTrue( buffer.IsValid );
			Assert.AreEqual( IntPtr.Zero, (IntPtr)buffer.native, "no Source 2 buffer" );

			buffer.SetData( new uint[] { 1, 2, 3, 4 } );
			Assert.AreEqual( 1, borealis.UploadsRecorded, "the write went to the device's backing" );

			var read = new uint[3];
			buffer.GetData( read, 1, 3 );
			CollectionAssert.AreEqual( new uint[] { 2, 3, 4 }, read, "read back from the device's backing" );

			buffer.Dispose();
			Assert.IsFalse( buffer.IsValid );
		}
		finally
		{
			Graphics.HasSource2Device = hadDevice;
			RenderBackend.SetDefault( null );
		}

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

	/// <summary>
	/// A volume's copy is a 3D texture with every slice, and storage when it was made for compute. It was a 2D texture before,
	/// so a compute shader's storage binding of it had no view and its sampled binding the wrong shape.
	/// </summary>
	[TestMethod]
	public void VolumesAreThreeDimensional()
	{
		// A device of its own: the volumes are made from their descriptions, whatever Source 2's device is
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		using var storage = Texture.CreateVolume( 8, 4, 6, ImageFormat.R11G11B10F ).WithUAVBinding().WithName( "test storage volume" ).Finish();
		var info = borealis.Device.InfoOf( borealis.TextureOf( storage ) );
		Assert.AreEqual( Borealis.Gpu.TextureDimension.Texture3D, info.Dimension );
		Assert.AreEqual( (8, 4, 6), (info.Width, info.Height, info.Depth) );
		Assert.AreEqual( Borealis.Gpu.Format.RG11B10Float, info.Format );
		Assert.IsTrue( info.Usage.HasFlag( Borealis.Gpu.TextureUsage.Storage ), "made for compute" );

		using var filled = Texture.CreateVolume( 1, 1, 1 ).WithData( [255, 255, 255, 255] ).WithName( "test filled volume" ).Finish();
		var filledInfo = borealis.Device.InfoOf( borealis.TextureOf( filled ) );
		Assert.AreEqual( Borealis.Gpu.TextureDimension.Texture3D, filledInfo.Dimension );
		Assert.AreEqual( Borealis.Gpu.Format.RGBA8Unorm, filledInfo.Format );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}
}
