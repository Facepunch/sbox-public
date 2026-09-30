using Sandbox.UI;

namespace EngineTests;

/// <summary>
/// Exercises solid shape submission with the texture mask used by text-clipped backgrounds.
/// </summary>
[TestClass]
public class PainterSolidMasksTest
{
	/// <summary>
	/// Polygon fills honor the alpha mask while strokes ignore it, preserving opacity and drawing clips.
	/// </summary>
	[TestMethod]
	public void TextMaskClipsSolidPolygonButNotSolidLine()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		// A deterministic alpha mask exercises the same path as glyph rasterization.
		using var mask = Texture.CreateRenderTarget().WithSize( 48, 24 ).Create();
		using ( var painter = Painter.Begin( mask ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Color.White;
			painter.Rect( new Rect( 0, 0, 24, 24 ) );
		}

		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Opacity = 0.5f;
			painter.Clip( new Rect( 0, 0, 48, 64 ) );
			painter.MaskFill( mask, new Rect( 8, 8, 48, 24 ) );
			painter.Fill = Color.Red;
			painter.Polygon( [new Vector2( 8, 8 ), new Vector2( 56, 8 ), new Vector2( 56, 32 ), new Vector2( 8, 32 )] );
			painter.Stroke = Stroke.Solid( Color.Blue, 8 );
			painter.Line( new Vector2( 8, 48 ), new Vector2( 56, 48 ) );

			var instances = painter.ActiveContext.Batcher.Instances;
			Assert.AreEqual( 2, instances.Count );
			Assert.AreEqual( (int)BackgroundClip.Text, instances[0].BackgroundClip );
			Assert.AreEqual( mask.Index, instances[0].TextMaskIndex );
			Assert.AreEqual( new Vector4( 0, 0, 48, 24 ), instances[0].BackgroundClipRect );
			Assert.AreEqual( (int)BackgroundClip.BorderBox, instances[1].BackgroundClip );
			Assert.AreEqual( 0, instances[1].TextMaskIndex );
			Assert.AreEqual( instances[0].ScissorIndex, instances[1].ScissorIndex );
		}

		using var bitmap = target.GetBitmap();
		Assert.AreEqual( 0.5f, bitmap.GetPixel( 16, 20 ).a, 0.02f, "Opaque mask pixels retain polygon opacity." );
		Assert.AreEqual( 0f, bitmap.GetPixel( 40, 20 ).a, 0.02f, "Transparent mask pixels remove the polygon fill." );
		Assert.AreEqual( 0.5f, bitmap.GetPixel( 16, 48 ).a, 0.02f, "The line draws outside the fill mask's rectangle." );
		Assert.AreEqual( 0.5f, bitmap.GetPixel( 40, 48 ).a, 0.02f, "The line ignores the transparent half of the fill mask." );
		Assert.AreEqual( 0f, bitmap.GetPixel( 54, 48 ).a, 0.02f, "The drawing clip still clips the line." );
	}
}
