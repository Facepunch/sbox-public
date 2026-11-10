using Sandbox.UI;

namespace EngineTests;

[TestClass]
public class PainterCssStrokeTest : PainterTestBase
{
	[TestMethod]
	[DataRow( BorderStyle.Solid )]
	[DataRow( BorderStyle.Dashed )]
	[DataRow( BorderStyle.Dotted )]
	[DataRow( BorderStyle.Double )]
	[DataRow( BorderStyle.Groove )]
	[DataRow( BorderStyle.Ridge )]
	[DataRow( BorderStyle.Inset )]
	[DataRow( BorderStyle.Outset )]
	public void CssAndPainterUseIdenticalEdgeStrokes( BorderStyle style )
	{
		var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 200, 200 ) };
		var panel = root.AddChild<Panel>();
		panel.Style.Set( "width: 100px; height: 80px; border-radius: 12px; background-color: black;" );
		panel.Style.BorderLeftWidth = 2;
		panel.Style.BorderTopWidth = 4;
		panel.Style.BorderRightWidth = 6;
		panel.Style.BorderBottomWidth = 8;
		panel.Style.BorderLeftColor = Color.Transparent;
		panel.Style.BorderTopColor = Color.Red;
		panel.Style.BorderRightColor = Color.Green;
		panel.Style.BorderBottomColor = Color.Blue;
		panel.Style.BorderStyle = style;
		try
		{
			root.Layout();
			var css = PanelDrawSnapshot.Build( root ).Instances.Single();
			using var output = new PainterTestOutput( PaintContext.Batcher );
			var painter = Paint;
			painter.Fill = Color.Black;
			painter.Stroke = new Stroke { Style = style };
			painter.Rect( panel.Box.Rect, new Vector4( 2, 4, 6, 8 ), Color.Transparent, Color.Red, Color.Green, Color.Blue, 12 );
			var drawn = output.Instances.Single().GPU;
			Assert.AreEqual( css.BorderSize, drawn.BorderSize );
			Assert.AreEqual( css.BorderColorL, drawn.BorderColorL );
			Assert.AreEqual( css.BorderColorT, drawn.BorderColorT );
			Assert.AreEqual( css.BorderColorR, drawn.BorderColorR );
			Assert.AreEqual( css.BorderColorB, drawn.BorderColorB );
			Assert.AreEqual( css.BorderStyle, drawn.BorderStyle );
			Assert.AreEqual( css.BorderRadius, drawn.BorderRadius );
			Assert.AreEqual( css.BorderRadiusV, drawn.BorderRadiusV );
			Assert.AreEqual( 0, output.Batcher.Paths.Count, "CSS edge styles share the single box instance." );
		}
		finally { root.Delete( true ); }
	}

	[TestMethod]
	public void TransparentLeftEdgeDoesNotDisableTheOtherEdges()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Rect( new Rect( 8, 8, 48, 48 ), new Vector4( 4, 6, 8, 10 ), Color.Transparent, Color.Red, Color.Green, Color.Blue, 8 );
		}
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( 0f, bitmap.GetPixel( 10, 32 ).a, 0.02f );
		Assert.AreEqual( Color.Red, bitmap.GetPixel( 32, 10 ) );
		Assert.AreEqual( Color.Green, bitmap.GetPixel( 52, 32 ) );
		Assert.AreEqual( Color.Blue, bitmap.GetPixel( 32, 52 ) );
		Assert.AreEqual( 0f, bitmap.GetPixel( 32, 32 ).a, 0.02f );
	}
}
