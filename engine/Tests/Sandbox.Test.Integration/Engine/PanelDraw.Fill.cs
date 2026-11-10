using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	/// <summary>Every closed shape uses the current fill, followed by the current stroke.</summary>
	[TestMethod]
	public void ClosedShapesUseCurrentFill()
	{
		WithBuffer( layer =>
		{
			var rect = new Rect( 10, 20, 60, 40 );
			Action[] draws = [
				() => Paint.Polygon( [rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft] ),
				() => Paint.Triangle( rect.TopLeft, rect.TopRight, rect.BottomLeft ),
				() => Paint.Triangle( new Triangle( new Vector3( 10, 20, 100 ), new Vector3( 70, 20, -100 ), new Vector3( 10, 60, 50 ) ) ),
				() => Paint.Quad( rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft ),
				() => Paint.Rect( rect ),
				() => Paint.Rect( rect, 8 ),
				() => Paint.Rect( rect, new Painter.CornerRadii( new( 4 ), new( 2 ), new( 1 ), new( 3 ) ) ),
				() => Paint.Circle( rect ),
				() => Paint.Circle( rect.Center, 20 ),
				() => Paint.Pie( rect.Center, 20, 0, 90 ),
				() => Paint.Pie( rect.Center, 20, 0, 360 ),
				() => Paint.Arrow( rect.TopLeft, rect.BottomRight )];
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			Fill fill = Color.Red.WithAlpha( 0.8f );
			foreach ( var draw in draws )
			{
				layer.Clear();
				PaintFill = Fill.None;
				PaintStroke = Stroke.None;
				draw();
				Assert.AreEqual( 0, layer.Instances.Count );
				PaintFill = fill;
				draw();
				var instance = layer.Instances.Single();
				Assert.AreEqual( Color.Red.WithAlpha( 0.4f ), instance.GPU.Color );
				Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
				Assert.AreNotEqual( UICssBoxBatched.ShapeKind.StrokePath, instance.BorderShapeData.Kind );
				layer.Clear();
				PaintStroke = Stroke.Solid( Color.Blue, 4 );
				draw();
				Assert.AreEqual( 2, layer.Instances.Count );
				Assert.AreEqual( Color.Red.WithAlpha( 0.4f ), layer.Instances[0].GPU.Color );
				Assert.AreEqual( Color.Blue.WithAlpha( 0.5f ), layer.Instances[1].GPU.Color );
				AssertStroke( layer.Instances[1] );
				Assert.AreEqual( fill, PaintFill, "Drawing must not change the current fill." );
			}
		} );
	}

	/// <summary>Changing fill state cannot alter recorded solid, gradient or image paints; None leaves the stroke enabled.</summary>
	[TestMethod]
	public void FillStateCapturesPaint()
	{
		WithBuffer( layer =>
		{
			using var texture = Texture.Create( 2, 2 ).Finish();
			var rect = new Rect( 10, 20, 60, 40 );
			PaintContext.InheritedOpacity = 0.5f;
			PaintFill = Fill.Solid( Color.Red.WithAlpha( 0.8f ) );
			Paint.Rect( rect );
			var saved = PaintFill;
			PaintFill = Color.Blue;
			Paint.Rect( rect );
			PaintFill = saved;
			Paint.Circle( rect.Center, 20 );
			PaintFill = Fill.LinearGradient( Color.Red, Color.Blue, 30 );
			Paint.Quad( rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft );
			PaintFill = Fill.Image( texture, Color.White.WithAlpha( 0.4f ), width: Length.Percent( 50 ), height: Length.Percent( 200 ),
			offsetX: Length.Percent( -12.5f ), offsetY: Length.Percent( -100 ) );
			Paint.Triangle( rect.TopLeft, rect.TopRight, rect.BottomLeft );
			PaintFill = Fill.None;
			Paint.Rect( rect );
			Assert.AreEqual( 5, layer.Instances.Count );
			CollectionAssert.AreEqual( new[] { Color.Red.WithAlpha( 0.4f ), Color.Blue.WithAlpha( 0.5f ), Color.Red.WithAlpha( 0.4f ), Color.Transparent, Color.Transparent },
				layer.Instances.Select( i => i.GPU.Color ).ToArray() );
			var gradient = layer.Instances[3].BackgroundGradient;
			Assert.AreEqual( 2, gradient.Count );
			Assert.AreEqual( Color.Red, (Color)gradient.StopColors[0] );
			Assert.AreEqual( Color.Blue, (Color)gradient.StopColors[1] );
			Assert.AreEqual( Color.White.WithAlpha( 0.5f ), layer.Instances[3].GPU.BackgroundTint );
			var image = layer.Instances[4];
			Assert.AreSame( texture, image.BackgroundImage );
			Assert.AreEqual( Color.White.WithAlpha( 0.2f ), image.GPU.BackgroundTint );
			Assert.AreEqual( new Vector4( -7.5f, -40, 30, 80 ), image.GPU.BackgroundRect );
			PaintStroke = Stroke.Solid( Color.Green, 2 );
			Paint.Rect( rect );
			Assert.AreEqual( 6, layer.Instances.Count );
			Assert.AreEqual( Color.Green.WithAlpha( 0.5f ), layer.Instances[^1].GPU.Color );
			AssertStroke( layer.Instances[^1] );
			Assert.AreEqual( Fill.None, PaintFill );
		} );
	}

	/// <summary>Path and outline commands ignore Fill, even for closed contours, and do not change it.</summary>
	[TestMethod]
	public void StrokeOnlyCommandsIgnoreFill()
	{
		WithBuffer( layer =>
		{
			var rect = new Rect( 10, 20, 60, 40 );
			Action[] draws = [
				() => Paint.Line( rect.TopLeft, rect.BottomRight ),
				() => Paint.Line( new Line( new Vector3( 10, 20, 100 ), new Vector3( 70, 60, -100 ) ) ),
				() => Paint.Line( [rect.TopLeft, rect.TopRight, rect.BottomRight] ),
				() => Paint.Arc( rect.Center, 20, 15, 270 ),
				() => Paint.Arc( rect.Center, 20, 0, 360 ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomRight ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight ),
				() => Paint.Outline( rect ),
				() => Paint.Outline( rect, cornerRadius: 8, offset: 3 )];
			var fill = Fill.Solid( Color.Green );
			PaintFill = fill;
			foreach ( var draw in draws )
			{
				layer.Clear();
				PaintStroke = Stroke.None;
				draw();
				Assert.AreEqual( 0, layer.Instances.Count );
				PaintStroke = Stroke.Solid( Color.Red, 4 );
				draw();
				var instance = layer.Instances.Single();
				Assert.AreEqual( Color.Red, instance.GPU.Color );
				AssertStroke( instance );
				Assert.AreEqual( fill, PaintFill );
			}
		} );
	}
}
