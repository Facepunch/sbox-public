namespace UITests;

/// <summary>
/// Verifies simple-line specialization and fallback behavior without rendering.
/// </summary>
[TestClass]
public class PainterSimpleLineTest : PainterTestBase
{
	/// <summary>
	/// Open two-point lines bypass path storage regardless of caps or pattern.
	/// </summary>
	[TestMethod]
	[DataRow( BorderStyle.Solid, Stroke.LineCap.Round, false, true )]
	[DataRow( BorderStyle.Dashed, Stroke.LineCap.Round, false, true )]
	[DataRow( BorderStyle.Dotted, Stroke.LineCap.Round, false, true )]
	[DataRow( BorderStyle.Solid, Stroke.LineCap.Butt, false, true )]
	[DataRow( BorderStyle.Solid, Stroke.LineCap.Square, false, true )]
	[DataRow( BorderStyle.Dashed, Stroke.LineCap.Triangle, false, true )]
	[DataRow( BorderStyle.Dashed, Stroke.LineCap.Arrow, false, true )]
	[DataRow( BorderStyle.Solid, Stroke.LineCap.Round, true, false )]
	public void SimpleLinesUseAnalyticGeometry( BorderStyle style, Stroke.LineCap cap, bool closed, bool analytic )
	{
		var stroke = Stroke.Solid( Color.Red, 6 ) with { Style = style, Cap = cap };
		Painter.Path.DrawPolyline( PaintContext, [new( 20, 30 ), new( 70, 60 )], stroke, closed );

		Assert.AreEqual( 1, PaintContext.Batcher.Instances.Count );
		var shape = PaintContext.Batcher.Shapes[PaintContext.Batcher.Instances[0].ShapeIndex];
		Assert.AreEqual( analytic ? UICssBoxBatched.ShapeKind.SimpleLine : UICssBoxBatched.ShapeKind.StrokePath, shape.Kind );
		Assert.AreEqual( analytic, PaintContext.Batcher.Paths.Count == 0 );
	}

	/// <summary>
	/// Coincident and non-finite endpoints must not turn into visible discs.
	/// </summary>
	[TestMethod]
	public void DegenerateSimpleLinesRemainEmpty()
	{
		var painter = Paint;
		painter.Stroke = Stroke.Solid( Color.White, 6 ).WithCap( Stroke.LineCap.Round );
		painter.Line( new Vector2( 20 ), new Vector2( 20 ) );
		painter.Line( new Vector2( float.NaN, 20 ), new Vector2( 40 ) );
		painter.Line( new Vector2( 20 ), new Vector2( float.PositiveInfinity, 40 ) );
		Assert.AreEqual( 0, PaintContext.Batcher.Instances.Count );
	}
}
