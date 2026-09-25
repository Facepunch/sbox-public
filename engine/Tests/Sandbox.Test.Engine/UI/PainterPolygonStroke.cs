using System;

namespace UITests;

/// <summary>
/// Checks shared polygon storage and the cases which must retain general stroke geometry.
/// </summary>
[TestClass]
public class PainterPolygonStrokeTest : PainterTestBase
{
	/// <summary>
	/// The outline references one contour, with a fill instance only when requested.
	/// </summary>
	[TestMethod]
	[DataRow( 4, Stroke.StrokeAlignment.Center, true )]
	[DataRow( 9, Stroke.StrokeAlignment.Center, true )]
	[DataRow( 9, Stroke.StrokeAlignment.Inside, true )]
	[DataRow( 9, Stroke.StrokeAlignment.Outside, true )]
	[DataRow( 4, Stroke.StrokeAlignment.Center, false )]
	[DataRow( 4, Stroke.StrokeAlignment.Inside, false )]
	[DataRow( 4, Stroke.StrokeAlignment.Outside, false )]
	[DataRow( 8, Stroke.StrokeAlignment.Center, false )]
	[DataRow( 8, Stroke.StrokeAlignment.Inside, false )]
	[DataRow( 8, Stroke.StrokeAlignment.Outside, false )]
	[DataRow( 9, Stroke.StrokeAlignment.Center, false )]
	[DataRow( 9, Stroke.StrokeAlignment.Inside, false )]
	[DataRow( 9, Stroke.StrokeAlignment.Outside, false )]
	public void FillAndOutlineShareContour( int count, Stroke.StrokeAlignment alignment, bool hasFill )
	{
		var points = new Vector2[count];
		for ( int i = 0; i < count; i++ )
		{
			float angle = MathF.Tau * i / count;
			points[i] = new Vector2( 100 + MathF.Cos( angle ) * 80, 100 + MathF.Sin( angle ) * 80 );
		}
		var painter = Paint;
		painter.Fill = hasFill ? Color.Red : Fill.None;
		painter.Stroke = Stroke.Solid( Color.Blue, 4 ).WithAlignment( alignment );
		painter.Polygon( points );

		var batcher = PaintContext.Batcher;
		Assert.AreEqual( hasFill ? 2 : 1, batcher.Instances.Count );
		Assert.AreEqual( 2, batcher.Shapes.Count );
		int fillIndex = hasFill ? batcher.Instances[0].ShapeIndex : 0;
		var outline = batcher.Shapes[batcher.Instances[hasFill ? 1 : 0].ShapeIndex];
		Assert.AreEqual( UICssBoxBatched.ShapeKind.PolygonStroke, outline.Kind );
		Assert.AreEqual( fillIndex, outline.PolygonStrokeShapeIndex );
		Assert.AreEqual( count > 8 ? count : 0, batcher.PolygonPoints.Count );
		Assert.AreEqual( 0, batcher.Paths.Count );
		Assert.AreEqual( 0, batcher.PathNodes.Count );
		Assert.AreEqual( 0, outline.PathCount );
		Assert.AreEqual( alignment == Stroke.StrokeAlignment.Center ? 4f : 8f, outline.PolygonStrokeWidth );
		Assert.AreEqual( alignment == Stroke.StrokeAlignment.Center ? 0f : alignment == Stroke.StrokeAlignment.Inside ? 1f : -1f, outline.PolygonStrokeAlignmentSign );
		float offset = alignment == Stroke.StrokeAlignment.Inside ? 0 : alignment == Stroke.StrokeAlignment.Center ? -2 : -4;
		Assert.AreEqual( new Vector2( offset, offset ), outline.PolygonStrokeOriginOffset );
	}

	/// <summary>
	/// Patterns, other joins and endpoint-clipped round joins preserve their established geometry.
	/// </summary>
	[TestMethod]
	[DataRow( "short", true )]
	[DataRow( "short", false )]
	[DataRow( "duplicate", true )]
	[DataRow( "duplicate", false )]
	[DataRow( "miter", true )]
	[DataRow( "miter", false )]
	[DataRow( "bevel", true )]
	[DataRow( "bevel", false )]
	[DataRow( "dashed", true )]
	[DataRow( "dashed", false )]
	[DataRow( "dotted", true )]
	[DataRow( "dotted", false )]
	public void UnsupportedOutlinesKeepGeneralPath( string kind, bool hasFill )
	{
		Vector2[] points = [new( 20, 20 ), new( 80, 20 ), new( 80, 80 ), new( 20, 80 )];
		if ( kind == "short" ) points[1] = new( 21, 20 );
		if ( kind == "duplicate" ) points[1] = points[0];
		var painter = Paint;
		painter.Fill = hasFill ? Color.Red : Fill.None;
		painter.Stroke = kind switch
		{
			"miter" => Stroke.Solid( Color.White, 8 ) with { Join = Stroke.LineJoin.Miter },
			"bevel" => Stroke.Solid( Color.White, 8 ) with { Join = Stroke.LineJoin.Bevel },
			"dashed" => Stroke.Dashed( Color.White, 8, 8, 8 ),
			"dotted" => Stroke.Dotted( Color.White, 8, 8 ),
			_ => Stroke.Solid( Color.White, 8 )
		};
		painter.Polygon( points );
		var batcher = PaintContext.Batcher;
		Assert.AreEqual( hasFill ? 2 : 1, batcher.Instances.Count );
		Assert.AreEqual( UICssBoxBatched.ShapeKind.StrokePath, batcher.Shapes[batcher.Instances[hasFill ? 1 : 0].ShapeIndex].Kind );
	}
}
