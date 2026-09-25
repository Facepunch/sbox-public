using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	/// <summary>With Fill disabled, shapes use the current centered stroke and emit no fill.</summary>
	[TestMethod]
	public void GeometryOverloadsUseCurrentStroke()
	{
		WithBuffer( layer =>
		{
			var rect = new Rect( 10, 20, 60, 40 );
			Action[] draws = [
				() => Paint.Line( [rect.TopLeft, rect.TopRight, rect.BottomRight] ),
				() => Paint.Polygon( [rect.TopLeft, rect.TopRight, rect.BottomRight] ),
				() => Paint.Line( rect.TopLeft, rect.BottomRight ),
				() => Paint.Line( new Line( new Vector3( 10, 20, 100 ), new Vector3( 70, 60, -100 ) ) ),
				() => Paint.Polygon( [rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft] ),
				() => Paint.Triangle( rect.TopLeft, rect.TopRight, rect.BottomLeft ),
				() => Paint.Triangle( new Triangle( new Vector3( 10, 20, 100 ), new Vector3( 70, 20, -100 ), new Vector3( 10, 60, 50 ) ) ),
				() => Paint.Quad( rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft ),
				() => Paint.Rect( rect ),
				() => Paint.Rect( rect, corners: 8 ),
				() => Paint.Rect( rect, new Painter.CornerRadii( new( 4 ), new( 2 ), new( 1 ), new( 3 ) ) ),
				() => Paint.Circle( rect ),
				() => Paint.Circle( rect.Center, 20 ),
				() => Paint.Arc( rect.Center, 20, 0, 360 ),
				() => Paint.Arc( rect.Center, 20, 15, -270 ),
				() => Paint.Pie( rect.Center, 20, 0, 90 ),
				() => Paint.Pie( rect.Center, 20, 0, 360 ),
				() => Paint.Arrow( rect.TopLeft, rect.BottomRight ),
				() => Paint.Outline( rect ),
				() => Paint.Outline( rect, cornerRadius: 8, offset: 3 ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomRight ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight )];
			var stroke = new Stroke( Color.Red.WithAlpha( 0.8f ), 4 )
			{
				Cap = Stroke.LineCap.Round,
				Style = BorderStyle.Dashed,
				Offset = 3
			};
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			foreach ( var draw in draws )
			{
				layer.Clear();
				PaintStroke = Stroke.None;
				draw();
				Assert.AreEqual( 0, layer.Instances.Count, "A default stroke draws nothing." );
				PaintStroke = stroke;
				draw();
				var instance = layer.Instances.Single();
				AssertStroke( instance );
				Assert.AreEqual( 4f, instance.BorderShapeData.Circle.z );
				Assert.AreEqual( Color.Red.WithAlpha( 0.4f ), instance.GPU.Color );
				Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
				Assert.IsTrue( Primitives( layer ).Length > 0 || layer.Instances.Any( i => i.BorderShapeData.Kind == UICssBoxBatched.ShapeKind.SimpleLine ) );
				Assert.AreEqual( stroke, PaintStroke, "Drawing must not change the current stroke." );
			}
		} );
	}

	/// <summary>Stroke values are copied into recorded draws; disabling the stroke leaves fills enabled.</summary>
	[TestMethod]
	public void CurrentStrokeHasValueSemantics()
	{
		WithBuffer( layer =>
		{
			var from = new Vector2( 10, 20 );
			var to = new Vector2( 60, 20 );
			PaintStroke = new Stroke( Color.Red, 4 );
			Paint.Line( from, to );
			PaintStroke = new Stroke( Color.Blue, 7 );
			Paint.Line( from, to );
			var copy = PaintStroke;
			copy = copy with { Width = 9 };
			Assert.AreEqual( 7f, PaintStroke.Width );
			PaintStroke = copy;
			Paint.Line( from, to );
			PaintStroke = Stroke.None;
			Paint.Line( from, to );
			Assert.AreEqual( 3, layer.Instances.Count );
			CollectionAssert.AreEqual( new[] { 4f, 7f, 9f }, layer.Instances.Select( i => i.BorderShapeData.Circle.z ).ToArray() );
			CollectionAssert.AreEqual( new[] { Color.Red, Color.Blue, Color.Blue }, layer.Instances.Select( i => i.GPU.Color ).ToArray() );
			Assert.AreEqual( Stroke.None, PaintStroke );
			layer.Clear();
			PaintFill = Color.Green;
			Paint.Rect( new Rect( 0, 0, 20, 20 ) );
			PaintFill = new Fill( Color.Green );
			Paint.Rect( new Rect( 0, 0, 20, 20 ) );
			Assert.AreEqual( 2, layer.Instances.Count );
			Assert.IsTrue( layer.Instances.All( i => i.PathData is null ), "Disabling the stroke must leave fills enabled." );
			layer.Clear();
			PaintStroke = copy;
			PaintFill = Color.Green;
			Paint.Rect( new Rect( 0, 0, 20, 20 ) );
			PaintFill = new Fill( Color.Green );
			Paint.Rect( new Rect( 0, 0, 20, 20 ) );
			Assert.AreEqual( 4, layer.Instances.Count );
			CollectionAssert.AreEqual( new[] { Color.Green, Color.Blue, Color.Green, Color.Blue }, layer.Instances.Select( i => i.GPU.Color ).ToArray() );
			Assert.AreEqual( copy, PaintStroke );
		} );
	}

	/// <summary>Outline offsets place the inner stroke edge relative to the source box and support patterns.</summary>
	[TestMethod]
	public void OutlineUsesCurrentStrokeAndOffset()
	{
		WithBuffer( layer =>
		{
			foreach ( float offset in new[] { -6f, 0f, 6f } )
			{
				layer.Clear();
				PaintStroke = Stroke.Solid( Color.Red, 4 );
				Paint.Outline( new Rect( 10, 20, 60, 40 ), offset: offset );
				var top = Primitives( layer ).Single( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A.y == p.A.w && p.A.y < 40 );
				Assert.AreEqual( 20 - offset - 2, top.A.y );
				Assert.AreEqual( 10 - offset - 2, top.A.x );
				Assert.AreEqual( 70 + offset + 2, top.A.z );
			}
			layer.Clear();
			PaintStroke = Stroke.Dotted( Color.Red, 2, 3, 4 );
			Paint.Outline( new Rect( 10, 20, 60, 40 ), cornerRadius: 8 );
			var dots = Primitives( layer );
			Assert.IsTrue( dots.Length > 0 && dots.All( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc ) );
		} );
	}

	[TestMethod]
	public void OutlineCornerOverloadsMatch()
	{
		WithBuffer( layer =>
		{
			var bounds = new Rect( 10, 20, 60, 40 );
			PaintStroke = Stroke.Solid( Color.Red, 4 );
			foreach ( float radius in new[] { 0f, 2f, 8f, 200f } )
				foreach ( float offset in new[] { -6f, 0f, 12f } )
				{
					layer.Clear();
					Paint.Outline( bounds, cornerRadius: radius, offset: offset );
					var expected = Primitives( layer );
					layer.Clear();
					Paint.Outline( bounds, new Painter.CornerRadii( radius ), offset );
					CollectionAssert.AreEqual( expected, Primitives( layer ), $"Radius {radius}, offset {offset}" );
				}
		} );
	}

	[TestMethod]
	public void EllipticalCornerDetailUsesBothAxes()
	{
		WithBuffer( layer =>
		{
			PaintStroke = Stroke.Solid( Color.Red, 2 );
			var bounds = new Rect( 0, 0, 100, 100 );
			Paint.Rect( bounds, new Painter.CornerRadii( new Vector2( 2, 40 ) ) );
			int tallCount = Primitives( layer ).Length;
			layer.Clear();
			Paint.Rect( bounds, new Painter.CornerRadii( new Vector2( 40, 2 ) ) );
			Assert.AreEqual( tallCount, Primitives( layer ).Length, "Rotating elliptical corners must preserve their curve detail." );
		} );
	}

	sealed class DrawStatePanel : Panel
	{
		internal delegate void DrawCallback( Painter painter );
		internal DrawCallback Drawing;
		internal Stroke InitialStroke;
		internal Fill InitialFill;
		internal Matrix InitialTransform;
		internal TextStyle InitialTextStyle;
		internal int DrawCount;
		public DrawStatePanel()
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = 64;
		}
		public override void OnDraw( Painter painter )
		{
			InitialStroke = painter.Stroke;
			InitialFill = painter.Fill;
			InitialTransform = painter.Transform;
			InitialTextStyle = painter.TextStyle;
			DrawCount++;
			Drawing?.Invoke( painter );
		}
	}

	/// <summary>Each callback starts disabled, including after an earlier panel throws or is invalidated.</summary>
	[TestMethod]
	public void DrawStateResetsForEveryPanelDraw()
	{
		WithBuffer( layer =>
		{
			var root = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 256, 256 ) };
			root.Style.FlexDirection = FlexDirection.Column;
			var first = root.AddChild<DrawStatePanel>();
			var second = root.AddChild<DrawStatePanel>();
			var saved = new Stroke( Color.Green, 9 );
			PaintStroke = saved;
			var savedTransform = Matrix.CreateTranslation( new Vector3( 10, 20, 0 ) );
			PaintTransform = savedTransform;
			var savedFill = Fill.Solid( Color.Cyan );
			PaintFill = savedFill;
			try
			{
				foreach ( bool throwAfterDrawing in new[] { false, true } )
				{
					first.Drawing = painter =>
					{
						painter.Stroke = new Stroke( Color.Red, 4 );
						painter.Fill = Color.Yellow;
						painter.Translate( 30, 40 );
						painter.TextStyle = TextStyle.Default with { FontSize = 27 };
						painter.Rect( new Rect( 0, 0, 20, 10 ) );
						if ( throwAfterDrawing ) throw new InvalidOperationException( "Expected OnDraw failure in draw state isolation test." );
					};
					second.Drawing = painter => painter.Rect( new Rect( 0, 0, 20, 10 ) );
					root.Layout();
					var frame = PanelDrawSnapshot.Build( root );
					Assert.AreEqual( default( Stroke ), first.InitialStroke );
					Assert.AreEqual( default( Stroke ), second.InitialStroke );
					Assert.AreEqual( Fill.None, first.InitialFill );
					Assert.AreEqual( Fill.None, second.InitialFill );
					Assert.AreEqual( Matrix.Identity, first.InitialTransform );
					Assert.AreEqual( Matrix.Identity, second.InitialTransform );
					Assert.AreEqual( TextStyle.Default, first.InitialTextStyle );
					Assert.AreEqual( TextStyle.Default, second.InitialTextStyle );
					Assert.AreEqual( 2, frame.Instances.Length, "Only the first panel emits a fill and stroke." );
					Assert.AreEqual( 1, frame.Instances.Count( i => i.Color == Color.Yellow ) );
					Assert.AreEqual( 1, frame.Instances.Count( i => i.Color == Color.Red ) );
					Assert.AreEqual( saved, PaintStroke );
					Assert.AreEqual( savedFill, PaintFill );
					Assert.AreEqual( savedTransform, PaintTransform );
					Assert.AreSame( layer.Batcher, PaintContext.Batcher );
				}
				Assert.AreEqual( 2, first.DrawCount );
				Assert.AreEqual( 2, second.DrawCount );
			}
			finally
			{
				root.Delete( true );
			}
		} );
	}

	/// <summary>Nested descriptor building restores the outer fill, stroke and target before the caller resumes drawing.</summary>
	[TestMethod]
	public void NestedPanelDrawingRestoresDrawContext()
	{
		var outerRoot = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 256, 256 ) };
		var innerRoot = new RootPanel { RenderedManually = true, PanelBounds = new Rect( 0, 0, 256, 256 ) };
		var outer = outerRoot.AddChild<DrawStatePanel>();
		var inner = innerRoot.AddChild<DrawStatePanel>();
		inner.Drawing = painter =>
		{
			painter.Stroke = new Stroke( Color.Blue, 2 );
			painter.Fill = Color.Green;
			painter.Rect( new Rect( 0, 0, 20, 10 ) );
		};
		Stroke restored = default;
		Fill restoredFill = default;
		Matrix restoredTransform = default;
		outer.Drawing = painter =>
		{
			painter.Stroke = new Stroke( Color.Red, 4 );
			painter.Fill = Color.Yellow;
			painter.Translate( 30, 40 );
			painter.TextStyle = TextStyle.Default with { FontSize = 27 };
			painter.Rect( new Rect( 0, 0, 20, 10 ) );

			innerRoot.BuildCommandList();
			restored = painter.Stroke;
			restoredFill = painter.Fill;
			restoredTransform = painter.Transform;
			painter.Rect( new Rect( 0, 0, 40, 10 ) );
		};
		try
		{
			innerRoot.Layout();
			outerRoot.Layout();
			var outerFrame = PanelDrawSnapshot.Build( outerRoot );
			var innerFrame = PanelDrawSnapshot.Read( innerRoot );
			Assert.AreEqual( 1, inner.DrawCount );
			Assert.AreEqual( 1, outer.DrawCount );
			Assert.AreEqual( default( Stroke ), inner.InitialStroke );
			Assert.AreEqual( Fill.None, inner.InitialFill );
			Assert.AreEqual( Matrix.Identity, inner.InitialTransform );
			Assert.AreEqual( TextStyle.Default, inner.InitialTextStyle );
			Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 30, 40, 0 ) ), restoredTransform );
			Assert.AreEqual( Fill.None, outer.InitialFill );
			Assert.AreEqual( Fill.Solid( Color.Yellow ), restoredFill );
			Assert.AreEqual( 2, outerFrame.Instances.Count( i => i.Color == Color.Yellow ) );
			Assert.AreEqual( 1, innerFrame.Instances.Count( i => i.Color == Color.Green ) );
			Assert.AreEqual( new Stroke( Color.Red, 4 ), restored );
			Assert.AreEqual( 2, outerFrame.Instances.Count( i => i.Color == Color.Red ) );
			Assert.IsTrue( outerFrame.Shapes.All( i => i.Circle.z == 4 ) );
			Assert.AreEqual( 1, innerFrame.Instances.Count( i => i.Color == Color.Blue ) );
			Assert.AreEqual( 2f, innerFrame.Shapes.Single().Circle.z );
		}
		finally
		{
			innerRoot.Delete( true );
			outerRoot.Delete( true );
		}
	}
}
