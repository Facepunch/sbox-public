using System;

namespace UITests;

/// <summary>
/// Checks transient alignment masks and their storage across repeated draws.
/// </summary>
[TestClass]
public class PainterMaskStorageTest
{
	/// <summary>
	/// Aligned polygon and analytic masks reuse batch storage without allocating retained geometry.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Other tests can trigger GC and trim the shared pool during this measurement.
	[DataRow( "Polygon" )]
	[DataRow( "Circle" )]
	[DataRow( "Ring" )]
	[DataRow( "Crescent" )]
	public void WarmedMasksDoNotAllocate( string shape )
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		Vector2[] points = [new( 100, 100 ), new( 500, 100 ), new( 700, 300 ), new( 500, 400 ), new( 700, 700 ),
			new( 400, 800 ), new( 100, 700 ), new( 200, 500 ), new( 100, 300 )];
		try
		{
			foreach ( var alignment in Enum.GetValues<Stroke.StrokeAlignment>() )
			{
				foreach ( var style in new[] { BorderStyle.Solid, BorderStyle.Dashed, BorderStyle.Dotted } )
				{
					for ( int i = 0; i < 32; i++ ) Draw( context, points, shape, alignment, style );
					long start = GC.GetAllocatedBytesForCurrentThread();
					for ( int i = 0; i < 32; i++ ) Draw( context, points, shape, alignment, style );
					long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
					Assert.AreEqual( 0L, allocated, $"{shape}, {alignment}, {style}" );

					int expectedDraws = shape is "Ring" or "Crescent" ? 2 : 1;
					Assert.AreEqual( expectedDraws, context.Batcher.Instances.Count );
					if ( shape == "Polygon" && style == BorderStyle.Solid )
					{
						// Solid round outlines reference the contour directly; no separate mask is needed.
						Assert.AreEqual( 2, context.Batcher.Shapes.Count );
						var outline = context.Batcher.Shapes[context.Batcher.Instances[0].ShapeIndex];
						Assert.AreEqual( UICssBoxBatched.ShapeKind.PolygonStroke, outline.Kind );
						var contour = context.Batcher.Shapes[outline.PolygonStrokeShapeIndex];
						Assert.AreEqual( points.Length, contour.PathCount );
						Assert.AreEqual( alignment == Stroke.StrokeAlignment.Center ? 0f : alignment == Stroke.StrokeAlignment.Inside ? 1f : -1f,
							outline.PolygonStrokeAlignmentSign );
						continue;
					}
					int maskIndex = 0;
					foreach ( var instance in context.Batcher.Instances )
					{
						var stroke = context.Batcher.Shapes[instance.ShapeIndex];
						if ( alignment == Stroke.StrokeAlignment.Center )
						{
							Assert.AreEqual( 0, stroke.PolygonCount );
							continue;
						}

						Assert.IsTrue( stroke.PolygonCount > 0 );
						Assert.AreEqual( alignment == Stroke.StrokeAlignment.Inside ? 1f : -1f, stroke.Circle.w );
						if ( maskIndex != 0 ) Assert.AreEqual( maskIndex, stroke.PolygonCount, "Paired arcs share their mask." );
						maskIndex = stroke.PolygonCount;
						var mask = context.Batcher.Shapes[maskIndex - 1];
						Assert.AreEqual( shape == "Polygon", mask.PathCount > 0 );
					}
					Assert.AreEqual( expectedDraws + (alignment == Stroke.StrokeAlignment.Center ? 0 : 1), context.Batcher.Shapes.Count );
				}
			}
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	static void Draw( Painter.Context context, Vector2[] points, string shape, Stroke.StrokeAlignment alignment, BorderStyle style )
	{
		context.CommandList.Reset();
		context.Begin( new Rect( 0, 0, 1000, 1000 ) );
		var painter = context.Painter;
		painter.Stroke = Stroke.Dashed( Color.White, 6, 8, 5, 3 ) with { Style = style, Alignment = alignment };
		switch ( shape )
		{
			case "Polygon": painter.Polygon( points ); break;
			case "Circle": painter.Circle( new Vector2( 500 ), 400 ); break;
			case "Ring": painter.Ring( new Vector2( 500 ), 200, 400 ); break;
			case "Crescent": painter.Crescent( new Vector2( 500 ), 400, 100, new Vector2( 100, 0 ) ); break;
		}
	}
}
