using Borealis.Backend;
using Borealis.Backend.Upscalers;
using Borealis.Gpu;
using System.Numerics;

namespace BorealisGpuTests;

/// <summary>
/// The temporal upscalers on a Borealis.Gpu device (<see cref="TemporalUpscaler"/>, <c>borealis_upscalers</c>): FSR3 upscales
/// a flat colour into its output, recorded into a list of the device's, with validation clean. DLSS isn't made on any GPU
/// but NVIDIA's. Inconclusive where the library isn't built (not Windows).
/// </summary>
[TestClass]
public class TemporalUpscalerTests : GpuTest
{
	const int RenderSize = 32, DisplaySize = 64;

	[TestMethod]
	public void Fsr3UpscalesIntoItsOutput()
	{
		if ( !TemporalUpscaler.Available ) Assert.Inconclusive( "borealis_upscalers isn't built" );

		using var upscaler = TemporalUpscaler.Create( Device, TemporalUpscalerKind.Fsr3, (RenderSize, RenderSize), (DisplaySize, DisplaySize) );
		Assert.IsNotNull( upscaler, "FSR3 runs on every Vulkan 1.3 GPU" );
		Assert.AreEqual( 8 * 4, upscaler.JitterPhaseCount, "FidelityFX's 8 * (display / render)^2" );

		var list = Device.BeginCommandList( QueueType.Graphics, "FSR3" );
		var context = new GraphicsContext( Device, null, list );
		var color = context.CreateTarget( RenderSize, RenderSize, Format.RGBA16Float, name: "Colour" );
		var motion = context.CreateTarget( RenderSize, RenderSize, Format.RGBA16Float, name: "Motion vectors" );
		var reactive = context.CreateTarget( RenderSize, RenderSize, Format.R16Float, name: "Reactive" );
		var output = context.CreateTarget( DisplaySize, DisplaySize, Format.RGBA16Float, name: "Output" );
		var depthInfo = TextureInfo.RenderTarget( RenderSize, RenderSize, Format.D32Float, name: "Depth" );
		depthInfo.Usage |= TextureUsage.Sampled;
		var depth = Device.CreateTexture( depthInfo );
		context.Track( depth, ImageLayout.Undefined );

		context.SetTarget( color );
		context.Clear( new Vector4( 0.5f, 0.25f, 0.125f, 1 ) );
		context.SetTarget( motion );
		context.Clear( Vector4.Zero );
		context.SetTarget( reactive );
		context.Clear( Vector4.Zero );
		context.SetTarget( default, depth );
		context.ClearDepth();

		// A few frames, the jitter moving on as a camera's does: under FidelityFX's limit of dispatches whose image views it
		// keeps (FFX_MAX_QUEUED_FRAMES, 4), as one list holds them all
		for ( var i = 0; i < 3; i++ )
		{
			var (x, y) = upscaler.Jitter( i );
			var frame = new TemporalUpscaler.Frame { JitterX = x, JitterY = y, Sharpness = 0.5f, FrameTimeDeltaMs = 16.6f, CameraNear = 1, CameraFovVertical = 1.2f, Reset = i == 0 };
			Assert.IsTrue( upscaler.Dispatch( context, color, depth, motion, reactive, output, frame ), $"frame {i}" );
		}

		var readback = Device.CreateBuffer( new BufferInfo { Size = DisplaySize * DisplaySize * 8, Memory = MemoryLocation.Readback, Name = "Readback" } );
		context.CopyToBuffer( output, readback );
		Device.Wait( Device.Submit( context.List ) );
		var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>( Device.Map( readback ) );

		foreach ( var (px, py) in (ReadOnlySpan<(int, int)>)[(0, 0), (31, 17), (63, 63)] )
		{
			var i = (py * DisplaySize + px) * 4;
			Assert.AreEqual( 0.5f, (float)pixels[i], 0.03f, $"red at {px}, {py}" );
			Assert.AreEqual( 0.25f, (float)pixels[i + 1], 0.03f, $"green at {px}, {py}" );
			Assert.AreEqual( 0.125f, (float)pixels[i + 2], 0.03f, $"blue at {px}, {py}" );
		}

		Device.WaitIdle();
		context.Release();
		Device.Destroy( depth );
		Device.Destroy( readback );
	}

	[TestMethod]
	public void DlssOnlyOnNvidia()
	{
		if ( !TemporalUpscaler.Available ) Assert.Inconclusive( "borealis_upscalers isn't built" );
		if ( Device.Adapter.VendorId == 0x10DE ) Assert.Inconclusive( "An NVIDIA GPU: DLSS may run here" );

		using var dlss = TemporalUpscaler.Create( Device, TemporalUpscalerKind.Dlss, (RenderSize, RenderSize), (DisplaySize, DisplaySize) );
		Assert.IsNull( dlss );
		Assert.AreEqual( 0, TemporalUpscaler.DlssDeviceExtensions( Device.Native.Instance, Device.Native.PhysicalDevice ).Count );
	}
}
