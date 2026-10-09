using System;

namespace UITests;

[TestClass]
public class PainterRingTest
{
	readonly Painter.Context PaintContext = new( new Sandbox.Rendering.CommandList() );
	Painter Paint => PaintContext.Painter;

	[TestInitialize]
	public void BeginRecording() => PaintContext.Begin( new Rect( 0, 0, 1000, 1000 ) );

	[TestCleanup]
	public void DiscardRecording()
	{
		PaintContext.CommandList.Reset();
		PaintContext.Batcher.Dispose();
	}

	[TestMethod]
	public void RingWithNoFillOnlyStrokesItsTwoEdges()
	{
		var painter = Paint;
		painter.Stroke = Stroke.Dashed( Color.Blue, 2 );
		painter.Ring( new Vector2( 50 ), 10, 20 );
		Assert.AreEqual( 2, PaintContext.Batcher.Instances.Count );
		Assert.AreEqual( Fill.None, painter.Fill );
		Assert.AreEqual( Stroke.Dashed( Color.Blue, 2 ), painter.Stroke );
	}

	[TestMethod]
	public void ZeroInnerRadiusMatchesCircle()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Stroke = new Stroke( Color.Blue, 2 );
		painter.Ring( new Vector2( 50 ), 0, 20 );
		painter.Circle( new Vector2( 50 ), 20 );
		var instances = PaintContext.Batcher.Instances;
		Assert.AreEqual( 4, instances.Count );
		Assert.AreEqual( instances[0].Rect, instances[2].Rect );
		Assert.AreEqual( instances[1].Rect, instances[3].Rect );
	}

	[TestMethod]
	public void CircleSizeUsesFullWidthAndHeight()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Circle( new Vector2( 50 ), new Vector2( 40, 20 ) );
		painter.Circle( new Rect( 30, 40, 40, 20 ) );
		var instances = PaintContext.Batcher.Instances;
		Assert.AreEqual( 2, instances.Count );
		Assert.AreEqual( instances[0].Rect, instances[1].Rect );
		Assert.AreEqual( new Vector4( 30, 40, 40, 20 ), instances[0].Rect );
	}

	[TestMethod]
	public void RingSectorRespectsSweepDirection()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		foreach ( var sweep in new[] { 90f, -90f } )
		{
			int first = PaintContext.Batcher.Instances.Count;
			painter.Ring( new Vector2( 50 ), 10, 20, 0, sweep );
			var instances = PaintContext.Batcher.Instances.Skip( first ).ToArray();
			Assert.IsTrue( instances.Length > 0 );
			foreach ( var instance in instances )
			{
				Assert.IsTrue( instance.Rect.x >= 49.999f );
				Assert.IsTrue( instance.Rect.x + instance.Rect.z <= 70.001f );
				if ( sweep > 0 ) Assert.IsTrue( instance.Rect.y >= 49.999f );
				else Assert.IsTrue( instance.Rect.y + instance.Rect.w <= 50.001f );
			}
		}
	}

	[TestMethod]
	public void FullRingSectorHasNoRadialSeam()
	{
		var painter = Paint;
		painter.Stroke = Stroke.Solid( Color.Blue, 2 );
		painter.Ring( new Vector2( 50 ), 10, 20, 45, -360 );
		painter.Ring( new Vector2( 50 ), 10, 20 );
		var instances = PaintContext.Batcher.Instances;
		Assert.AreEqual( 4, instances.Count );
		Assert.AreEqual( instances[0].Rect, instances[2].Rect );
		Assert.AreEqual( instances[1].Rect, instances[3].Rect );
	}

	[TestMethod]
	public void InvalidRingSectorsDoNotDraw()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Stroke = Stroke.Solid( Color.Blue, 2 );
		painter.Ring( Vector2.Zero, 10, 20, 0, 0 );
		painter.Ring( Vector2.Zero, 10, 20, float.NaN, 90 );
		painter.Ring( Vector2.Zero, 10, 20, 0, float.PositiveInfinity );
		painter.Ring( Vector2.Zero, -1, 20, 0, 90 );
		painter.Ring( Vector2.Zero, 20, 10, 0, 90 );
		Assert.AreEqual( 0, PaintContext.Batcher.Instances.Count );
	}

	[TestMethod]
	public void PolygonHelpersDrawAndPreserveState()
	{
		var painter = Paint;
		painter.Fill = Color.Red.WithAlpha( 0.5f );
		painter.Stroke = Stroke.Solid( Color.Blue, 2 );
		int previous = 0;
		painter.Star( new Vector2( 50 ), 10, 20 );
		Assert.IsTrue( PaintContext.Batcher.Instances.Count > previous );
		previous = PaintContext.Batcher.Instances.Count;
		painter.Cross( new Vector2( 50 ), 10, 40 );
		Assert.IsTrue( PaintContext.Batcher.Instances.Count > previous );
		previous = PaintContext.Batcher.Instances.Count;
		painter.Tick( new Vector2( 50 ), 10, 40 );
		Assert.IsTrue( PaintContext.Batcher.Instances.Count > previous );
		Assert.AreEqual( (Fill)Color.Red.WithAlpha( 0.5f ), painter.Fill );
		Assert.AreEqual( Stroke.Solid( Color.Blue, 2 ), painter.Stroke );
	}

	[TestMethod]
	public void InvalidPolygonHelpersDoNotDraw()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Star( Vector2.Zero, 0, 20 );
		painter.Star( Vector2.Zero, 10, -1 );
		painter.Star( Vector2.Zero, 10, 20, points: 2 );
		painter.Star( Vector2.Zero, 10, 20, rotation: float.NaN );
		painter.Cross( Vector2.Zero, 0, 40 );
		painter.Cross( Vector2.Zero, 50, 40 );
		painter.Tick( Vector2.Zero, 0, 40 );
		painter.Tick( Vector2.Zero, 30, 40 );
		painter.Tick( Vector2.Zero, 10, float.NaN );
		Assert.AreEqual( 0, PaintContext.Batcher.Instances.Count );
	}

	[TestMethod]
	public void AnalyticSymbolsSubmitOneFillEach()
	{
		var painter = Paint;
		painter.Fill = Color.Red.WithAlpha( 0.5f );
		painter.Capsule( new Vector2( 50 ), new Vector2( 100, 50 ), 10, 20 );
		painter.Crescent( new Vector2( 50 ), 30, 25, new Vector2( 15, 0 ) );
		painter.Heart( new Vector2( 50 ), 40 );
		Assert.AreEqual( 3, PaintContext.Batcher.Instances.Count );
	}

	[TestMethod]
	public void EnclosedCrescentCutoutSupportsStrokeAlignment()
	{
		var painter = Paint;
		foreach ( var alignment in Enum.GetValues<Stroke.StrokeAlignment>() )
		{
			int before = PaintContext.Batcher.Instances.Count;
			painter.Stroke = Stroke.Solid( Color.Blue, 2 ).WithAlignment( alignment );
			painter.Crescent( new Vector2( 50 ), 30, 10, new Vector2( 5, 0 ) );
			Assert.AreEqual( before + 2, PaintContext.Batcher.Instances.Count );
		}
	}

	[TestMethod]
	public void ContainedCapsuleMatchesLargerCircle()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Capsule( new Vector2( 50 ), new Vector2( 55, 50 ), 30, 10 );
		painter.Circle( new Vector2( 50 ), 30 );
		Assert.AreEqual( 2, PaintContext.Batcher.Instances.Count );
		Assert.AreEqual( PaintContext.Batcher.Instances[0].Rect, PaintContext.Batcher.Instances[1].Rect );
	}

	[TestMethod]
	public void CoveredAndInvalidSymbolsDoNotDraw()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Crescent( Vector2.Zero, 20, 30, Vector2.Zero );
		painter.Crescent( Vector2.Zero, 20, 30, new Vector2( float.NaN, 0 ) );
		painter.Capsule( Vector2.Zero, new Vector2( 10 ), -1, 5 );
		painter.Heart( Vector2.Zero, 0 );
		painter.Heart( Vector2.Zero, float.PositiveInfinity );
		Assert.AreEqual( 0, PaintContext.Batcher.Instances.Count );
	}

	[TestMethod]
	public void InvalidRingsDoNotDraw()
	{
		var painter = Paint;
		painter.Fill = Color.Red;
		painter.Stroke = new Stroke( Color.Blue, 2 );
		painter.Ring( Vector2.Zero, -1, 20 );
		painter.Ring( Vector2.Zero, 20, 10 );
		painter.Ring( Vector2.Zero, 20, 20 );
		painter.Ring( Vector2.Zero, float.NaN, 20 );
		painter.Ring( Vector2.Zero, 10, float.PositiveInfinity );
		painter.Ring( new Vector2( float.NaN, 0 ), 10, 20 );
		Assert.AreEqual( 0, PaintContext.Batcher.Instances.Count );
	}
}
