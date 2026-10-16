using System;

namespace UITests;

[TestClass]
public class PainterPathStorageTest
{
	/// <summary>
	/// Repeated strokes reuse temporary storage, including individual dash runs and dot enumeration.
	/// </summary>
	[TestMethod, TestCategory( Allocations.Category )]
	[DoNotParallelize] // Other tests can trigger GC and trim the shared pool during this measurement.
	[DataRow( 2, false, BorderStyle.Solid )]
	[DataRow( 2, false, BorderStyle.Dashed )]
	[DataRow( 16, false, BorderStyle.Solid )]
	[DataRow( 16, false, BorderStyle.Dashed )]
	[DataRow( 16, false, BorderStyle.Dotted )]
	[DataRow( 256, false, BorderStyle.Solid )]
	[DataRow( 256, false, BorderStyle.Dashed )]
	[DataRow( 256, false, BorderStyle.Dotted )]
	[DataRow( 2, true, BorderStyle.Solid )]
	[DataRow( 2, true, BorderStyle.Dashed )]
	[DataRow( 2, true, BorderStyle.Dotted )]
	public void WarmedLinesAndArcsDoNotAllocate( int count, bool arc, BorderStyle style )
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );
		var points = MakePoints( count );
		try
		{
			Allocations.AssertSettles( "32 draws", () =>
			{
				for ( int i = 0; i < 32; i++ ) Draw( context, points, arc, style );
			} );
			Assert.AreEqual( 1, context.Batcher.Instances.Count );
			Assert.AreEqual( arc || count > 2, context.Batcher.Paths.Count > 0 );
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	static void Draw( Painter.Context context, Vector2[] points, bool arc, BorderStyle style )
	{
		context.CommandList.Reset();
		context.Begin( new Rect( 0, 0, 1000, 1000 ) );
		var painter = context.Painter;
		painter.Stroke = Stroke.Dashed( Color.White, 2, 8, 5, 3 ) with { Style = style };
		if ( arc ) painter.Arc( new Vector2( 500 ), 400, 0, 360 );
		else painter.Line( points );
	}

	[TestMethod]
	[DataRow( BorderStyle.Solid, false )]
	[DataRow( BorderStyle.Solid, true )]
	[DataRow( BorderStyle.Dashed, false )]
	[DataRow( BorderStyle.Dashed, true )]
	[DataRow( BorderStyle.Dotted, false )]
	[DataRow( BorderStyle.Dotted, true )]
	public void PathStorageAccommodatesCapsJoinsAndPatterns( BorderStyle style, bool closed )
	{
		var context = new Painter.Context( new Sandbox.Rendering.CommandList() );

		// Solid strokes ignore the phase. The dash period is 13, so -10 (wraps to 3), 0 and 8 start the
		// pattern inside a dash, at one, and at the gap; dotted (period 7) gets 4, 0 and 1.
		var phases = style == BorderStyle.Solid ? new[] { 0f } : new[] { -10f, 0, 8 };
		try
		{
			// Each count takes a different storage path: <= 8 points, <= MaxPolygonPoints, and above
			foreach ( int count in new[] { 3, 17, 257 } )
			{
				var points = MakePoints( count );
				foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
					foreach ( var join in Enum.GetValues<Stroke.LineJoin>() )
						foreach ( float phase in phases )
						{
							context.CommandList.Reset();
							context.Begin( new Rect( 0, 0, 1000, 1000 ) );
							var painter = context.Painter;
							painter.Stroke = Stroke.Dashed( Color.White, 2, 8, 5, phase ) with { Style = style, Cap = cap, Join = join };
							if ( closed ) painter.Polygon( points );
							else painter.Line( points );
							Assert.AreEqual( 1, context.Batcher.Instances.Count );
							var shape = context.Batcher.Shapes[context.Batcher.Instances[0].ShapeIndex];
							if ( shape.Kind == UICssBoxBatched.ShapeKind.PolygonStroke )
							{
								shape = context.Batcher.Shapes[shape.PolygonStrokeShapeIndex];
								Assert.AreEqual( count > 8 ? count : 0, shape.PathCount );
								Assert.AreEqual( count > PainterBatcher.MaxPolygonPoints ? count * 2 - 1 : count > 8 ? -1 : 0, shape.PathNodeCount );
								continue;
							}
							Assert.IsTrue( shape.PathCount > 0 );
							Assert.AreEqual( shape.PathCount * 2 - 1, shape.PathNodeCount );
						}
			}
		}
		finally
		{
			context.CommandList.Reset();
			context.Batcher.Dispose();
		}
	}

	static Vector2[] MakePoints( int count )
	{
		var points = new Vector2[count];
		for ( int i = 0; i < count; i++ )
		{
			float angle = i * MathF.Tau / count;
			points[i] = new Vector2( 500 + MathF.Cos( angle ) * 400, 500 + MathF.Sin( angle ) * 400 );
		}
		return points;
	}
}
