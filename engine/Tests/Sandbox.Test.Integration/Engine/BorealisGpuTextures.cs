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
		TestGraphics.Require();
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
	/// A texture updated before the device has a copy of it (a game's screen painted once, then first drawn) gets its copy
	/// then, from the pixels it was made with, with the update over them: the engine forgets those pixels once it's
	/// updated, and under -borealisgpu Source 2's copy has nothing to read back.
	/// </summary>
	[TestMethod]
	public void UpdatesBeforeTheCopyIsMadeAreKept()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var texture = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, ( x, y ) => new Color32( (byte)x, (byte)y, 0, 255 ) ) ).Finish();
		texture.Update( Pixels( 4, 4, ( _, _ ) => new Color32( 0, 255, 0, 255 ) ), 4, 4, 4, 4 );

		var pixels = borealis.ReadPixels( texture );
		Assert.AreEqual( new Color32( 0, 255, 0, 255 ), pixels[5 * size + 5], "the update is in the copy" );
		Assert.AreEqual( new Color32( 12, 1, 0, 255 ), pixels[1 * size + 12], "outside it, the pixels the texture was made with" );
		Assert.AreEqual( 0, borealis.CopiedFromSource2, "nothing was read back from Source 2" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A texture array made empty and filled a layer at a time (<see cref="Texture.Update3D"/>, Rubble's voxel atlas) has a
	/// copy here that's an array of every layer, each with what was written to it; and a draw's attributes read a named one
	/// (its name is no file) as that copy. The copy was a 2D texture that kept no layer past the first.
	/// </summary>
	[TestMethod]
	public unsafe void ArrayLayersUpdatedOneAtATimeAreKept()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );
		var previous = RenderBackend.Default;
		var host = Borealis.RenderHost.Current;
		RenderBackend.SetDefault( borealis );
		Borealis.RenderHost.Current = new DeviceHost( borealis.Services );
		try
		{
			const int size = 4, layers = 3;
			using var texture = Texture.CreateArray( size, size, layers ).WithName( "test_atlas" ).Finish();
			for ( int layer = 0; layer < layers; layer++ )
			{
				var red = (byte)(80 * (layer + 1));
				texture.Update3D( Pixels( size, size, ( _, _ ) => new Color32( red, 0, 0, 255 ) ), 0, 0, layer, size, size, 1 );
			}

			for ( int layer = 0; layer < layers; layer++ )
			{
				var pixels = new byte[size * size * 4];
				fixed ( byte* data = pixels )
				{
					Assert.IsTrue( borealis.ReadPixels( texture, new NativeRect( 0, 0, size, size ), layer, 0, new NativeRect( 0, 0, size, size ), (IntPtr)data, ImageFormat.RGBA8888, size * 4 ), $"layer {layer} reads back" );
				}

				Assert.AreEqual( 80 * (layer + 1), pixels[0], $"layer {layer} has what was written to it" );
			}

			var mirrored = Sandbox.Rendering.Bridge.MirrorContent.Texture( texture );
			Assert.IsNotNull( mirrored, "the mirror has the engine's copy" );
			Assert.IsTrue( mirrored.IsArray, "as an array" );
		}
		finally
		{
			Borealis.RenderHost.Current = host;
			RenderBackend.SetDefault( previous );
		}

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// A scene object's attributes as Borealis draws read them (<see cref="Sandbox.Rendering.Bridge.MirrorContent.ObjectParameters"/>)
	/// carry its buffers, as the engine buffer's copy on the device: a package shader's per-instance data (gamma's
	/// <c>ZoneInstances</c>). The wrapper borrows the copy, so disposing it leaves the buffer to the engine's.
	/// </summary>
	[TestMethod]
	public void ObjectAttributesCarryBuffers()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var host = Borealis.RenderHost.Current;
		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		Borealis.RenderHost.Current = new DeviceHost( borealis.Services );
		try
		{
			using var buffer = new GpuBuffer<uint>( 4 );
			buffer.SetData( new uint[] { 1, 2, 3, 4 } );

			var attributes = new RenderAttributes();
			attributes.KeepManagedCopy();
			attributes.Set( "ZoneInstances", buffer );
			attributes.Set( "ZoneCut", 1.0f );

			var parameters = (Borealis.Backend.BackendParameters)Sandbox.Rendering.Bridge.MirrorContent.ObjectParameters( attributes );
			var bound = parameters.Get( new StringToken( "ZoneInstances" ) ) as Borealis.GpuBuffer;
			Assert.IsNotNull( bound, "the buffer reaches the draws' attributes" );
			Assert.IsTrue( Borealis.Backend.BackendDevice.Binds( bound, borealis.BufferOf( buffer ) ), "as the engine buffer's copy on the device" );
			Assert.AreEqual( 1.0f, parameters.Get( new StringToken( "ZoneCut" ) ) );

			bound.Dispose();
			var read = new uint[4];
			buffer.GetData( read, 0, 4 );
			CollectionAssert.AreEqual( new uint[] { 1, 2, 3, 4 }, read, "the engine's buffer outlives the wrapper" );
		}
		finally
		{
			Borealis.RenderHost.Current = host;
			RenderBackend.SetDefault( previous );
		}

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// <see cref="Texture.Clear"/> clears the device's copy too: one already made is cleared in place, and a render target
	/// whose copy isn't made yet starts with the colour it was last cleared to, not the one it was made with.
	/// </summary>
	[TestMethod]
	public void ClearsLandInTheCopy()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int size = 16;
		using var image = Texture.Create( size, size, ImageFormat.RGBA8888 ).WithData( Pixels( size, size, ( x, y ) => new Color32( (byte)x, (byte)y, 0, 255 ) ) ).Finish();
		Assert.AreEqual( new Color32( 5, 7, 0, 255 ), borealis.ReadPixels( image )[7 * size + 5], "the copy has the texture's pixels" );

		image.Clear( Color.Blue );
		Assert.AreEqual( new Color32( 0, 0, 255, 255 ), borealis.ReadPixels( image )[7 * size + 5], "the copy was cleared" );

		using var target = Texture.CreateRenderTarget().WithSize( size, size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Red ).Create();
		target.Clear( Color.Green );
		Assert.AreEqual( new Color32( 0, 255, 0, 255 ), borealis.ReadPixels( target )[3 * size + 3], "a copy made after the clear has its colour" );

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	/// <summary>
	/// What draws on a Borealis frame through the engine (custom objects, camera stages) get of the view: its lookup tables,
	/// as Borealis's own draws have them. Without the BRDF lookup a forward shader drawn that way (Mow the Lawn's terrain)
	/// has no environment reflection.
	/// </summary>
	[TestMethod]
	public void FrameDrawsGetTheViewsLookupTables()
	{
		RequireVulkan();

		var attributes = RenderAttributes.CreateManaged();
		BorealisGpuFrame.SetViewTextures( attributes );

		foreach ( var name in new[] { "BRDFLookup", "BlueNoise", "LtcAmplitudeLookup", "LtcMatrixLookup" } )
		{
			Assert.IsTrue( attributes.GetTexture( name ) is { IsValid: true } texture && !texture.IsError, name );
		}
	}

	/// <summary>
	/// A material made in code from a shader (<see cref="Material.Create(string, string, bool)"/>: WildRoute's sky) draws
	/// on Borealis.Gpu with that shader, not with BackendDevice's one-colour material, which its <c>CreateMaterial( string,
	/// Color )</c> made when the shader's path bound to it as a colour.
	/// </summary>
	[TestMethod]
	public void MaterialsMadeFromAShaderDrawThatShader()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		var host = Borealis.RenderHost.Current;
		Borealis.RenderHost.Current = new DeviceHost( borealis.Services );
		try
		{
			var material = Material.Create( "borealis_made_from_sky", "shaders/sky.shader" );
			var converted = Sandbox.Rendering.Bridge.MirrorContent.Material( material ) as Borealis.Backend.BackendMaterial;
			Assert.IsNotNull( converted?.Shader );
			StringAssert.Contains( converted.Shader.Name.Replace( '\\', '/' ), "/sky.shader" );
		}
		finally
		{
			Borealis.RenderHost.Current = host;
		}

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}

	sealed class DeviceHost( Borealis.GpuDevice device ) : Borealis.RenderHost
	{
		public override Borealis.GpuDevice Device => device;
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

		var previous = RenderBackend.Default;
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
			RenderBackend.SetDefault( previous );
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
		var previous = RenderBackend.Default;
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
			RenderBackend.SetDefault( previous );
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

	/// <summary>
	/// A compute shader dispatched with its own attributes outside any render block (gizmo text rendered while it lays out)
	/// runs at once in a block of its own, as Source 2's device runs one, with what was set on its attributes.
	/// </summary>
	[TestMethod]
	public unsafe void ComputeOutsideABlockRunsAtOnce()
	{
		RequireVulkan();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		// The process's device, as under -borealisgpu: shaders made now keep their attributes for it
		var previous = RenderBackend.Default;
		RenderBackend.SetDefault( borealis );
		try
		{
			// A 4x4 checkerboard of black and white; the second mip starts as its box filter, grey
			using var texture = Texture.Create( 4, 4, ImageFormat.RGBA8888 ).WithMips( 2 ).WithUAVBinding()
				.WithData( Pixels( 4, 4, ( x, y ) => ((x + y) & 1) == 0 ? new Color32( 0, 0, 0, 255 ) : new Color32( 255, 255, 255, 255 ) ) ).Finish();

			var downsample = new ComputeShader( "downsample_cs" );
			downsample.Attributes.SetCombo( "D_DOWNSAMPLE_METHOD", (int)Graphics.DownsampleMethod.Max );
			downsample.Attributes.Set( "MipLevel0", texture, 0 );
			downsample.Attributes.Set( "MipLevel1", texture, 1 );
			downsample.Attributes.Set( "TextureSize", new Vector2( 2, 2 ) );
			downsample.Attributes.Set( "InvTextureSize", new Vector2( 0.5f, 0.5f ) );

			Assert.IsFalse( Graphics.IsActive );
			downsample.Dispatch( 2, 2, 1 );

			var mip = new byte[2 * 2 * 4];
			fixed ( byte* data = mip )
			{
				Assert.IsTrue( borealis.ReadPixels( texture, new NativeRect( 0, 0, 2, 2 ), 0, 1, new NativeRect( 0, 0, 2, 2 ), (IntPtr)data, ImageFormat.RGBA8888, 2 * 4 ) );
			}

			Assert.IsTrue( mip.All( v => v == 255 ), "the second mip is the max of the first's: white, not the grey it started as" );
		}
		finally
		{
			RenderBackend.SetDefault( previous );
		}

		Assert.AreEqual( 0, borealis.ValidationErrors );
	}
}
