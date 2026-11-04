using Sandbox.UI;

namespace UITests;

[TestClass]
public class PainterLineSmoothTest
{
	readonly Painter.Context _context = new( new Sandbox.Rendering.CommandList() );
	Painter Paint => _context.Painter;

	[TestInitialize]
	public void BeginRecording() => _context.Begin( new Rect( 0, 0, 1000, 1000 ) );

	[TestCleanup]
	public void DiscardRecording()
	{
		_context.CommandList.Reset();
		_context.Batcher.Dispose();
	}

	UICssBoxBatched.PathPrimitive[] LastPath()
	{
		var shape = _context.Batcher.Shapes[_context.Batcher.Instances[^1].ShapeIndex];
		return _context.Batcher.Paths.Skip( shape.PathOffset ).Take( shape.PathCount ).ToArray();
	}

	[TestMethod]
	public void CurvePassesThroughPointsWithOnlyTwoEndpointCaps()
	{
		var painter = Paint;
		Vector2[] points = [new( 10, 50 ), new( 70, 10 ), new( 90, 90 ), new( 180, 40 )];
		painter.Fill = Color.Red;
		painter.Stroke = new Stroke( Color.Blue, 2, Stroke.LineCap.Round );
		painter.LineSmooth( points );
		Assert.AreEqual( 1, _context.Batcher.Instances.Count );
		var path = LastPath();
		var segments = path.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).ToArray();
		Assert.IsTrue( segments.Length > points.Length - 1, "The curve must contain curved segments, not just connect the inputs." );
		foreach ( var point in points )
			Assert.IsTrue( segments.Any( p => new Vector2( p.A.x, p.A.y ) == point || new Vector2( p.A.z, p.A.w ) == point ) );
		var caps = path.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc ).ToArray();
		Assert.AreEqual( 2, caps.Length );
		Assert.IsTrue( caps.Any( p => new Vector2( p.A.x, p.A.y ) == points[0] ) );
		Assert.IsTrue( caps.Any( p => new Vector2( p.A.x, p.A.y ) == points[^1] ) );
		Assert.AreEqual( Fill.Solid( Color.Red ), painter.Fill );
	}

	[TestMethod]
	public void TwoPointsAndStraightPatternsMatchLine()
	{
		var painter = Paint;
		Vector2[][] paths = [[new( 10, 20 ), new( 150, 20 )], [new( 10, 20 ), new( 33, 20 ), new( 37, 20 ), new( 150, 20 )]];
		foreach ( var stroke in new[] { Stroke.Solid( Color.Blue, 2 ), Stroke.Dashed( Color.Blue, 2, 8, 5, 3 ), Stroke.Dotted( Color.Blue, 2, 5, 3 ) } )
			foreach ( var points in paths )
			{
				painter.Stroke = stroke;
				painter.Line( points );
				var expected = LastPath();
				painter.LineSmooth( points );
				CollectionAssert.AreEqual( expected, LastPath() );
			}
	}

	[TestMethod]
	public void ConsecutiveDuplicatesDoNotChangeCurve()
	{
		var painter = Paint;
		Vector2 a = new( 10, 20 ), b = new( 60, 80 ), c = new( 150, 10 );
		painter.Stroke = Stroke.Dashed( Color.Blue, 2 );
		painter.LineSmooth( [a, b, c] );
		var expected = LastPath();
		painter.LineSmooth( [a, a, b, b, c, c] );
		CollectionAssert.AreEqual( expected, LastPath() );
	}

	[TestMethod]
	public void EmptyDegenerateAndInvalidPathsDrawNothing()
	{
		var painter = Paint;
		painter.LineSmooth( [Vector2.Zero, new( 30, 50 ), new( 100, 20 )] );
		painter.Stroke = Stroke.Solid( Color.Blue, 2 );
		painter.LineSmooth( [] );
		painter.LineSmooth( [Vector2.Zero] );
		painter.LineSmooth( [Vector2.Zero, Vector2.Zero, Vector2.Zero] );
		foreach ( var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
			painter.LineSmooth( [Vector2.Zero, new( 30, 50 ), new( 100, 20 ), new( value, 10 )] );
		Assert.AreEqual( 0, _context.Batcher.Instances.Count );
	}
}
