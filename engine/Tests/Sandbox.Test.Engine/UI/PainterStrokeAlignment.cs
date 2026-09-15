using System;

namespace UITests;

[TestClass]
public class PainterStrokeAlignmentTest
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
	public void InsideBoxCombinesFillAndStroke()
	{
		var painter = Paint;
		painter.Fill = Fill.LinearGradient( Color.Red, Color.Blue );
		painter.Stroke = Stroke.Solid( Color.White, 4 ).WithAlignment( Stroke.StrokeAlignment.Inside );
		painter.Rect( new Rect( 20, 20, 100, 60 ), 12 );
		Assert.AreEqual( 1, PaintContext.Batcher.Instances.Count );
		var box = PaintContext.Batcher.Instances[0];
		Assert.AreEqual( new Vector4( 4 ), box.BorderSize );
		Assert.AreEqual( -1, box.ShapeIndex );
		Assert.IsTrue( box.TextureIndex < 0, "The fill gradient is preserved in the combined box." );
	}

	[TestMethod]
	public void OpenPathsIgnoreAlignment()
	{
		var painter = Paint;
		foreach ( var alignment in Enum.GetValues<Stroke.StrokeAlignment>() )
		{
			painter.Stroke = Stroke.Dotted( Color.White, 4 ).WithAlignment( alignment );
			painter.Line( new Vector2( 20 ), new Vector2( 80, 20 ) );
		}
		var boxes = PaintContext.Batcher.Instances;
		Assert.AreEqual( 3, boxes.Count );
		Assert.AreEqual( boxes[0].Rect, boxes[1].Rect );
		Assert.AreEqual( boxes[0].Rect, boxes[2].Rect );
		foreach ( var box in boxes )
		{
			var path = PaintContext.Batcher.Shapes[box.ShapeIndex];
			Assert.AreEqual( 4f, path.Circle.z );
			Assert.AreEqual( 0, path.PolygonCount, "Open paths have no alignment mask." );
		}
	}

	[TestMethod]
	public void AlignmentIsRestoredByScope()
	{
		var painter = Paint;
		painter.Stroke = Stroke.Solid( Color.White, 4 ).WithAlignment( Stroke.StrokeAlignment.Outside );
		using ( painter.Scope() ) painter.Stroke = painter.Stroke.WithAlignment( Stroke.StrokeAlignment.Inside );
		Assert.AreEqual( Stroke.StrokeAlignment.Outside, painter.Stroke.Alignment );
	}

	[TestMethod]
	public void InsideGradientUsesTheShapeBounds()
	{
		var painter = Paint;
		painter.Stroke = Stroke.Solid( Fill.LinearGradient( Color.Red, Color.Blue ), 6 ).WithAlignment( Stroke.StrokeAlignment.Inside );
		painter.Rect( new Rect( 20, 30, 100, 60 ) );
		var box = PaintContext.Batcher.Instances[0];
		Assert.AreEqual( new Vector4( 20, 30, 100, 60 ), box.Rect );
		Assert.AreEqual( new Vector4( 0, 0, 100, 60 ), box.BackgroundRect );
	}
}
