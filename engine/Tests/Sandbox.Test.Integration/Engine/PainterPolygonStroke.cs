using System;

namespace EngineTests;

/// <summary>
/// Compares shared polygon outlines with the original segment-and-join renderer.
/// </summary>
[TestClass]
public class PainterPolygonStrokeTest
{
	/// <summary>
	/// Concavity, winding, thin strokes, alignment and paint mapping survive geometry sharing.
	/// </summary>
	[TestMethod]
	[DataRow( 4, 0.25f, true )]
	[DataRow( 4, 0.25f, false )]
	[DataRow( 4, 4f, true )]
	[DataRow( 4, 4f, false )]
	[DataRow( 5, 0.25f, true )]
	[DataRow( 5, 0.25f, false )]
	[DataRow( 5, 4f, true )]
	[DataRow( 5, 4f, false )]
	[DataRow( 9, 0.25f, true )]
	[DataRow( 9, 0.25f, false )]
	[DataRow( 9, 4f, true )]
	[DataRow( 9, 4f, false )]
	[DataRow( 9, 16f, true )]
	[DataRow( 9, 16f, false )]
	[DataRow( 8, 0.25f, false )]
	[DataRow( 8, 4f, false )]
	public void SharedOutlineMatchesGeneralRenderer( int count, float width, bool hasFill )
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		var points = new Vector2[count];
		for ( int i = 0; i < count; i++ )
		{
			// Five points traverse a crossing star; nine points form a simple concave contour.
			float angle = MathF.Tau * (count == 5 ? i * 2 : i) / count;
			float radius = i % 2 == 0 ? 29 : 19;
			points[i] = new Vector2( 44 + MathF.Cos( angle ) * radius, 44 + MathF.Sin( angle ) * radius );
		}

		foreach ( var alignment in Enum.GetValues<Stroke.StrokeAlignment>() )
			foreach ( bool reversed in new[] { false, true } )
			{
				if ( reversed ) Array.Reverse( points );
				using var expected = Texture.CreateRenderTarget().WithSize( 96, 96 ).Create();
				using var actual = Texture.CreateRenderTarget().WithSize( 96, 96 ).Create();
				Draw( expected, points, width, alignment, false, hasFill );
				Draw( actual, points, width, alignment, true, hasFill );
				using var a = actual.GetBitmap();
				using var b = expected.GetBitmap();
				double error = 0;
				float maximum = 0;
				for ( int y = 0; y < 96; y++ )
					for ( int x = 0; x < 96; x++ )
					{
						var difference = a.GetPixel( x, y ) - b.GetPixel( x, y );
						float delta = MathF.Max( MathF.Abs( difference.a ), MathF.Max( MathF.Abs( difference.r ), MathF.Max( MathF.Abs( difference.g ), MathF.Abs( difference.b ) ) ) );
						error += delta;
						maximum = MathF.Max( maximum, delta );
					}
				Assert.IsTrue( error / (96 * 96) < 0.002, $"{alignment}, reversed={reversed}: mean pixel error {error / (96 * 96)}" );
				Assert.IsTrue( maximum < 0.12f, $"{alignment}, reversed={reversed}: maximum pixel error {maximum}" );
				if ( reversed ) Array.Reverse( points );
			}
	}

	static void Draw( Texture target, Vector2[] points, float width, Stroke.StrokeAlignment alignment, bool shared, bool hasFill )
	{
		using var painter = Painter.Begin( target );
		painter.Clear( Color.Transparent );
		painter.Translate( 3.25f, 2.75f );
		painter.Rotate( 3 );
		painter.Scale( 1.05f, 0.9f );
		painter.Clip( new Rect( 12, 10, 70, 70 ) );
		painter.Opacity = 0.7f;
		painter.Fill = hasFill ? Fill.LinearGradient( Color.Red.WithAlpha( 0.4f ), Color.Blue.WithAlpha( 0.6f ) ) : Fill.None;
		painter.Stroke = Stroke.Solid( Fill.LinearGradient( Color.White, Color.Green ), width ).WithAlignment( alignment );
		if ( shared )
		{
			painter.Polygon( points );
			var batcher = painter.ActiveContext.Batcher;
			Assert.AreEqual( UICssBoxBatched.ShapeKind.PolygonStroke, batcher.Shapes[batcher.Instances[hasFill ? 1 : 0].ShapeIndex].Kind );
		}
		else
		{
			if ( hasFill ) Painter.Path.DrawPolygon( painter.ActiveContext, points, painter.Fill );
			Painter.Path.DrawPolyline( painter.ActiveContext, points, painter.Stroke, closed: true );
		}
	}

	/// <summary>
	/// Sharing the contour must not apply a fill's texture mask to its outline.
	/// </summary>
	[TestMethod]
	public void SharedOutlineIgnoresFillMask()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );

		using var mask = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( mask ) )
		{
			painter.Clear( Color.Transparent );
			painter.Fill = Color.White;
			painter.Rect( new Rect( 0, 0, 32, 64 ) );
		}
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.MaskFill( mask, new Rect( 0, 0, 64, 64 ) );
			painter.Fill = Color.Red;
			painter.Stroke = Stroke.Solid( Color.Blue, 4 );
			painter.Polygon( [new Vector2( 8, 8 ), new Vector2( 56, 8 ), new Vector2( 56, 56 ), new Vector2( 8, 56 )] );
			var batcher = painter.ActiveContext.Batcher;
			Assert.AreEqual( UICssBoxBatched.ShapeKind.PolygonStroke, batcher.Shapes[batcher.Instances[1].ShapeIndex].Kind );
		}
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( 1f, bitmap.GetPixel( 20, 32 ).a, 0.02f );
		Assert.AreEqual( 0f, bitmap.GetPixel( 40, 32 ).a, 0.02f );
		Assert.AreEqual( 1f, bitmap.GetPixel( 56, 32 ).b, 0.02f );
	}
}
