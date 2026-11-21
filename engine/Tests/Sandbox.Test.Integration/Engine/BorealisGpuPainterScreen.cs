using Borealis;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace EngineTests;

/// <summary>
/// Painter's screen (<c>PainterScreenViewport</c>) in a camera's stages on a Borealis.Gpu device, as native's pipeline gives
/// its view's main viewport: the display size's while an upscaler draws the scene at its render size, so a list painting in
/// screen pixels (RUN IT AGAIN's menu, <c>Painter.Begin( commandList )</c>) lands where it would at the display size.
/// </summary>
[TestClass]
public class BorealisGpuPainterScreenTest
{
	/// <summary>
	/// With FSR3 at its Quality preset the menu drew half as big again: its 1600x900 screen pixels on the 1066x600 frame.
	/// Without an upscaler the screen is the viewport itself, which maps nothing.
	/// </summary>
	[TestMethod]
	public void StagesGivePainterTheDisplaysViewport()
	{
		TestGraphics.Require();
		using var borealis = BorealisGpuRenderBackend.Create( validation: true );

		const int width = 32, height = 18;
		using var color = borealis.Services.CreateTexture( TextureDesc.Target( width, height, TextureFormat.RGBA16161616F ), "stage colour" );
		using var depth = borealis.Services.CreateTexture( TextureDesc.Target( width, height, TextureFormat.D32FS8 ), "stage depth" );
		using var target = new ViewTarget( color, depth, null );

		using var frame = new BorealisGpuFrame( borealis );
		using var view = new BorealisGpuView( borealis ) { Viewport = new Rect( 0, 0, width, height ) };
		frame.Adopt( target, new GpuViewConstants { ViewportMaxZ = 1.0f } );

		Vector4 upscaled, plain;
		view.DisplayViewport = new Rect( 0, 0, width * 1.5f, height * 1.5f );
		using ( frame.Begin( view ) ) upscaled = Graphics.Attributes.GetVector4( "PainterScreenViewport" );
		view.DisplayViewport = null;
		using ( frame.Begin( view ) ) plain = Graphics.Attributes.GetVector4( "PainterScreenViewport" );

		frame.End();
		frame.Release();

		Assert.AreEqual( new Vector4( 0, 0, width * 1.5f, height * 1.5f ), upscaled, "the display's viewport while drawn at the render size" );
		Assert.AreEqual( new Vector4( 0, 0, width, height ), plain, "the viewport itself without an upscaler" );
		Assert.AreEqual( 0, borealis.ValidationErrors );
	}
}
