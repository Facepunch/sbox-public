using System;

namespace EngineTests;

[TestClass]
public class PainterStrokeAlignmentTest
{
	[TestMethod]
	[DataRow( "Rect", false )]
	[DataRow( "Rect", true )]
	[DataRow( "Circle", false )]
	[DataRow( "Circle", true )]
	[DataRow( "Ellipse", false )]
	[DataRow( "Ellipse", true )]
	[DataRow( "Polygon", false )]
	[DataRow( "Polygon", true )]
	[DataRow( "ReversedPolygon", false )]
	[DataRow( "ReversedPolygon", true )]
	public void ClosedStrokesStayOnSelectedSide( string shape, bool outside )
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			// A gradient forces the general stroke path, including for rectangles.
			painter.Stroke = Stroke.Solid( Fill.LinearGradient( Color.White, Color.White ), 6 )
				.WithAlignment( outside ? Stroke.StrokeAlignment.Outside : Stroke.StrokeAlignment.Inside );
			switch ( shape )
			{
				case "Rect": painter.Rect( new Rect( 16, 16, 32, 32 ), 6 ); break;
				case "Circle": painter.Circle( new Vector2( 32 ), 16 ); break;
				case "Ellipse": painter.Circle( new Rect( 16, 20, 32, 24 ) ); break;
				default:
					// Concave, with enough vertices to require a path buffer; test both windings.
					Vector2[] points = [new( 16, 16 ), new( 32, 16 ), new( 48, 16 ), new( 48, 32 ),
						new( 48, 48 ), new( 32, 48 ), new( 16, 48 ), new( 16, 40 ), new( 24, 32 ), new( 16, 24 )];
					if ( shape == "ReversedPolygon" ) Array.Reverse( points );
					painter.Polygon( points );
					break;
			}
		}
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( outside ? 0f : 1f, bitmap.GetPixel( 45, 32 ).a, 0.02f, "Inside the boundary" );
		Assert.AreEqual( outside ? 1f : 0f, bitmap.GetPixel( 50, 32 ).a, 0.02f, "Outside the boundary" );
		Assert.AreEqual( 0f, bitmap.GetPixel( 57, 32 ).a, 0.02f, "Beyond the stroke" );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RingAlignmentUsesTheBandInterior( bool outside )
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Stroke = Stroke.Solid( Color.White, 4 ).WithAlignment( outside ? Stroke.StrokeAlignment.Outside : Stroke.StrokeAlignment.Inside );
			painter.Ring( new Vector2( 32 ), 8, 20 );
		}
		using var bitmap = target.GetBitmap();
		Assert.AreEqual( outside ? 1f : 0f, bitmap.GetPixel( 38, 32 ).a, 0.02f, "Inside the hole" );
		Assert.AreEqual( outside ? 0f : 1f, bitmap.GetPixel( 42, 32 ).a, 0.02f, "Band beside the hole" );
		Assert.AreEqual( outside ? 0f : 1f, bitmap.GetPixel( 50, 32 ).a, 0.02f, "Band beside the outer edge" );
		Assert.AreEqual( outside ? 1f : 0f, bitmap.GetPixel( 54, 32 ).a, 0.02f, "Beyond the outer edge" );
	}

	static void RequireVulkan()
	{
		if ( g_pRenderDevice.GetRenderDeviceAPI() != NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN )
			Assert.Inconclusive( "Requires Vulkan rendering." );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void PatternsAreClippedToTheSelectedSide( bool dotted, bool outside )
	{
		RequireVulkan();
		using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using ( var painter = Painter.Begin( target ) )
		{
			painter.Clear( Color.Transparent );
			painter.Stroke = (dotted ? Stroke.Dotted( Color.White, 4, 8 ) : Stroke.Dashed( Color.White, 4, 8, 8 ))
				.WithAlignment( outside ? Stroke.StrokeAlignment.Outside : Stroke.StrokeAlignment.Inside );
			painter.Rect( new Rect( 16, 16, 32, 32 ) );
		}
		using var bitmap = target.GetBitmap();
		int painted = 0;
		for ( int y = 0; y < 64; y++ )
			for ( int x = 0; x < 64; x++ )
			{
				var alpha = bitmap.GetPixel( x, y ).a;
				if ( alpha < 0.1f ) continue;
				painted++;
				bool inside = x >= 16 && x < 48 && y >= 16 && y < 48;
				Assert.AreEqual( !outside, inside, $"Pattern crossed the boundary at ({x}, {y})." );
			}
		Assert.IsTrue( painted > 30, "The pattern must draw visible ink." );
	}
}
