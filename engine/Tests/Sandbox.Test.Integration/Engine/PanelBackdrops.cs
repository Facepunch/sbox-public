using Sandbox.Rendering;
using Sandbox.UI;

namespace EngineTests;

[TestClass]
public class PanelBackdropsTest
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OffsetOverlappingBackdropsSampleTheirDestination( bool isolated )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		using var target = Texture.CreateRenderTarget().WithSize( 256, 256 ).Create();
		var bounds = new Rect( 64, 64, 128, 128 );
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			// Use the CSS panel target path: it preserves screen coordinates inside the layer.
			var layer = isolated ? painter.Target( "Backdrop regression", bounds ) : default;
			try
			{
				painter.Fill = Color.Red;
				painter.Rect( bounds );
				painter.Fill = Color.Blue;
				painter.Rect( new Rect( 128, 64, 64, 128 ) );
				painter.FilterBackdrop( new Rect( 72, 72, 112, 112 ), new Painter.Filter { Invert = 1 }, BorderRadii.Zero );
				painter.FilterBackdrop( new Rect( 80, 112, 96, 64 ), new Painter.Filter { Invert = 1 }, BorderRadii.Zero );
			}
			finally
			{
				layer.Dispose();
			}

			if ( isolated )
			{
				painter.Composite( new RenderTargetHandle { Name = "Backdrop regression" }, bounds,
					new Painter.Filter(), null, MaskScope.Default, [], 0, Color.Transparent );
			}
		}

		using var bitmap = target.GetBitmap();
		// The first pane inverts both halves; the overlap inverts the previous result back.
		AssertColor( Color.Cyan, bitmap.GetPixel( 96, 96 ) );
		AssertColor( Color.Yellow, bitmap.GetPixel( 160, 96 ) );
		AssertColor( Color.Red, bitmap.GetPixel( 96, 144 ) );
		AssertColor( Color.Blue, bitmap.GetPixel( 160, 144 ) );
	}

	static void AssertColor( Color expected, Color actual )
	{
		Assert.AreEqual( expected.r, actual.r, 0.02f, "Red" );
		Assert.AreEqual( expected.g, actual.g, 0.02f, "Green" );
		Assert.AreEqual( expected.b, actual.b, 0.02f, "Blue" );
		Assert.AreEqual( expected.a, actual.a, 0.02f, "Alpha" );
	}
}
