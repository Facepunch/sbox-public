using Sandbox.UI;
using System;

namespace EngineTests;

/// <summary>Checks the GPU payload emitted by immediate panel shapes.</summary>
[TestClass]
public partial class PanelDrawTest : PainterTestBase
{
	void WithBuffer( Action<PainterTestOutput> test )
	{
		using var scope = Paint.Scope();
		using var legacy = new LegacyPaint.Binding( PaintContext );
		var buffer = PaintContext;
		using var layer = new PainterTestOutput( buffer.Batcher );
		buffer.State = new();
		buffer.ScaleToScreen = 2;
		buffer.InheritedOpacity = 1;
		test( layer );
	}

	static void AssertPolygon( PainterTestOutput.Instance instance, Vector2[] points, bool ordered = true )
	{
		var min = new Vector2( points.Min( p => p.x ), points.Min( p => p.y ) );
		var max = new Vector2( points.Max( p => p.x ), points.Max( p => p.y ) );
		var rect = instance.GPU.Rect;
		Assert.AreEqual( min.x, rect.x, 0.001f );
		Assert.AreEqual( min.y, rect.y, 0.001f );
		Assert.AreEqual( max.x - min.x, rect.z, 0.001f );
		Assert.AreEqual( max.y - min.y, rect.w, 0.001f );
		var shape = instance.BorderShapeData;
		Assert.AreEqual( UICssBoxBatched.ShapeKind.Polygon, shape.Kind );
		Assert.AreEqual( points.Length, shape.PolygonCount );
		Assert.AreEqual( Vector4.Zero, shape.Circle );
		Vector4[] packed = [shape.Polygon01, shape.Polygon23, shape.Polygon45, shape.Polygon67];
		var remaining = points.ToList();
		for ( int i = 0; i < 8; i++ )
		{
			var pair = packed[i / 2];
			var point = i % 2 == 0 ? new Vector2( pair.x, pair.y ) : new Vector2( pair.z, pair.w );
			if ( i >= points.Length )
				Assert.AreEqual( Vector2.Zero, point, $"Unused vertex {i}" );
			else if ( ordered )
			{
				Assert.AreEqual( points[i].x - min.x, point.x, 0.001f );
				Assert.AreEqual( points[i].y - min.y, point.y, 0.001f );
			}
			else
			{
				var match = remaining.FindIndex( p => (p - min - point).Length < 0.001f );
				Assert.IsTrue( match >= 0, $"Unexpected vertex {point}" );
				remaining.RemoveAt( match );
			}
		}
	}

	/// <summary>Vertices stay in layout pixels and pack relative to their bounds in either winding.</summary>
	[TestMethod]
	public void PolygonsPackAndDeduplicate()
	{
		WithBuffer( layer =>
		{
			Vector2[] triangle = [new( 10, 20 ), new( 70, 30 ), new( 30, 80 )];
			PaintFill = Color.White;
			Paint.Triangle( triangle[0], triangle[1], triangle[2] );
			AssertPolygon( layer.Instances.Single(), triangle );
			Vector2[] quad = [new( 10, 20 ), new( 70, 30 ), new( 60, 80 ), new( 20, 70 )];
			Paint.Quad( quad[0], quad[1], quad[2], quad[3] );
			AssertPolygon( layer.Instances[1], quad );
			Vector2[] concave = [new( 10, 20 ), new( 90, 20 ), new( 90, 80 ), new( 70, 80 ), new( 70, 40 ), new( 30, 40 ), new( 30, 80 ), new( 10, 80 )];
			foreach ( var points in new[] { triangle, quad, concave[..5], concave[..6], concave[..7], concave } )
			{
				foreach ( var winding in new[] { points, points.Reverse().ToArray() } )
				{
					PaintFill = Color.White;
					Paint.Polygon( winding );
					var first = layer.Instances[^1];
					AssertPolygon( first, winding );
					var translated = winding.Select( p => p + new Vector2( 640, 300 ) ).ToArray();
					PaintFill = Color.Red;
					Paint.Polygon( translated );
					AssertPolygon( layer.Instances[^1], translated );
					var batcher = new PainterBatcher( new Sandbox.Rendering.CommandList() );
					Assert.AreEqual( batcher.GetOrAddShape( first.BorderShapeData ), batcher.GetOrAddShape( layer.Instances[^1].BorderShapeData ) );
					Assert.AreEqual( 1, batcher.Shapes.Count );
				}
			}
		} );
	}

	/// <summary>Segments use perpendicular widths and arrows clamp the head to short segments.</summary>
	[TestMethod]
	public void LinesAndArrowsHaveExpectedVertices()
	{
		WithBuffer( layer =>
		{
			var from = new Vector2( 10, 20 );
			foreach ( var (to, side) in new[] { (new Vector2( 50, 20 ), new Vector2( 0, 5 )), (new Vector2( 10, 60 ), new Vector2( -5, 0 )), (new Vector2( 40, 60 ), new Vector2( -4, 3 )) } )
			{
				foreach ( var reverse in new[] { false, true } )
				{
					PaintStroke = Stroke.Solid( Color.White, 10 );
					Paint.Line( reverse ? to : from, reverse ? from : to );
					var instance = layer.Instances[^1];
					Assert.AreEqual( 1, instance.PathData.Primitives.Length );
					var segment = instance.PathData.Primitives[0];
					var start = reverse ? to : from;
					var end = reverse ? from : to;
					Assert.AreEqual( new Vector4( start.x, start.y, end.x, end.y ), segment.A );
					Assert.AreEqual( 10f, instance.BorderShapeData.Circle.z );
					foreach ( var corner in new[] { from + side, to + side, to - side, from - side } )
						Assert.IsTrue( new Rect( instance.GPU.Rect.x, instance.GPU.Rect.y, instance.GPU.Rect.z, instance.GPU.Rect.w ).Grow( 0.001f ).IsInside( corner ) );
				}
			}
			PaintStroke = Stroke.None;
			PaintFill = Color.White;
			Paint.Triangle( new Vector2( 10, 12 ), new Vector2( 14, 20 ), new Vector2( 10, 28 ) );
			AssertPolygon( layer.Instances[^1], [new( 10, 12 ), new( 14, 20 ), new( 10, 28 )], ordered: false );
			foreach ( var length in new[] { 4f, 40f } )
			{
				foreach ( var width in new[] { 4f, 20f } )
				{
					Paint.Arrow( from, from + new Vector2( length, 0 ), width );
					var neck = 10 + Math.Max( 0, length - 16 );
					var halfHead = Math.Max( width, 16 ) / 2;
					AssertPolygon( layer.Instances[^1], [new( 10, 20 - width / 2 ), new( neck, 20 - width / 2 ), new( neck, 20 - halfHead ), new( 10 + length, 20 ), new( neck, 20 + halfHead ), new( neck, 20 + width / 2 ), new( 10, 20 + width / 2 )], ordered: false );
				}
			}
			Assert.AreEqual( 11, layer.Instances.Count );
		} );
	}

	/// <summary>Struct overloads preserve drawing options and use only X/Y coordinates.</summary>
	[TestMethod]
	public void StructOverloadsMatchVertices()
	{
		WithBuffer( layer =>
		{
			PaintContext.InheritedOpacity = 0.5f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			var fill = Color.Blue.WithAlpha( 0.6f );
			var border = Color.Green.WithAlpha( 0.8f );
			var line = new Line( new Vector3( 10, 20, 100 ), new Vector3( 70, 40, -100 ) );
			var triangle = new Triangle( new Vector3( 10, 20, 100 ), new Vector3( 70, 40, -100 ), new Vector3( 30, 80, 50 ) );
			PaintFill = fill;
			Action[] draws = [
				() => Paint.Line( new Vector2( 10, 20 ), new Vector2( 70, 40 ) ),
				() => Paint.Line( line ),
				() => Paint.Triangle( new Vector2( 10, 20 ), new Vector2( 70, 40 ), new Vector2( 30, 80 ) ),
				() => Paint.Triangle( triangle )];
			PaintStroke = Stroke.Solid( border, 3 );
			for ( int i = 0; i < draws.Length; i += 2 )
			{
				layer.Clear();
				draws[i]();
				var expected = layer.Instances.ToArray();
				layer.Clear();
				draws[i + 1]();
				Assert.AreEqual( expected.Length, layer.Instances.Count );
				for ( int j = 0; j < expected.Length; j++ )
				{
					var actual = layer.Instances[j];
					Assert.AreEqual( expected[j].GPU, actual.GPU );
					Assert.AreEqual( expected[j].BorderShapeData, actual.BorderShapeData );
					if ( expected[j].PathData is not null )
						Assert.IsTrue( expected[j].PathData.Primitives.SequenceEqual( actual.PathData.Primitives ) );
				}
			}
		} );
	}

	/// <summary>Ellipses retain independent horizontal and vertical radii.</summary>
	[TestMethod]
	public void EllipseRadiiFollowBounds()
	{
		WithBuffer( layer =>
		{
			foreach ( var size in new[] { new Vector2( 120, 40 ), new Vector2( 40, 120 ) } )
			{
				PaintFill = Color.White;
				Paint.Circle( new Rect( new Vector2( 10, 20 ), size ) );
				var instance = layer.Instances[^1];
				Assert.AreEqual( new Vector4( 10, 20, size.x, size.y ), instance.GPU.Rect );
				Assert.AreEqual( new Vector4( size.x / 2 ), instance.GPU.BorderRadius );
				Assert.AreEqual( new Vector4( size.y / 2 ), instance.GPU.BorderRadiusV );
				Assert.AreEqual( UICssBoxBatched.ShapeKind.None, instance.BorderShapeData.Kind );
			}
			Assert.AreEqual( 2, layer.Instances.Count );
		} );
	}

	/// <summary>Filled shapes inherit the centered stroke, opacity and blend mode; invalid widths disable only the stroke.</summary>
	[TestMethod]
	public void ShapeStrokesAndRenderState()
	{
		WithBuffer( layer =>
		{
			PaintContext.InheritedOpacity = 0.25f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			var fill = new Color( 0.2f, 0.4f, 0.6f, 0.8f );
			var strokeColor = new Color( 0.6f, 0.4f, 0.2f, 0.4f );
			Vector2 a = new( 10, 20 ), b = new( 50, 20 ), c = new( 50, 60 ), d = new( 10, 60 );
			var rect = new Rect( 10, 20, 40, 40 );
			PaintFill = fill;
			Action[] draws = [
				() => Paint.Triangle( a, b, c ),
				() => Paint.Quad( a, b, c, d ),
				() => Paint.Polygon( [a, b, c, d] ),
				() => Paint.Arrow( a, b ),
				() => Paint.Rect( rect ),
				() => Paint.Rect( rect, 8 ),
				() => Paint.Rect( rect, new Painter.CornerRadii( new( 4 ), new( 2 ), new( 1 ), new( 3 ) ) ),
				() => Paint.Circle( rect ),
				() => Paint.Circle( rect.Center, 20 ),
				() => Paint.Pie( rect.Center, 20, 0, 90 ),
				() => Paint.Pie( rect.Center, 20, 0, 360 )];
			foreach ( var width in new[] { 3f, 0f, -2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
				foreach ( var draw in draws )
				{
					layer.Clear();
					PaintStroke = Stroke.Solid( strokeColor, width );
					draw();
					Assert.AreEqual( width == 3 ? 2 : 1, layer.Instances.Count );
					Assert.AreEqual( fill.WithAlpha( 0.2f ), layer.Instances[0].GPU.Color );
					if ( width == 3 )
					{
						var stroke = layer.Instances[1];
						Assert.AreEqual( UICssBoxBatched.ShapeKind.StrokePath, stroke.BorderShapeData.Kind );
						Assert.AreEqual( 3f, stroke.BorderShapeData.Circle.z );
						Assert.AreEqual( strokeColor.WithAlpha( 0.1f ), stroke.GPU.Color );
					}
					foreach ( var instance in layer.Instances )
					{
						Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
						Assert.AreEqual( Vector4.Zero, instance.GPU.BorderSize );
					}
				}
		} );
	}

	/// <summary>Invalid geometry emits nothing, but invalid polygon counts throw.</summary>
	[TestMethod]
	public void DegenerateShapesAndInvalidCounts()
	{
		WithBuffer( layer =>
		{
			Vector2 a = new( 10, 20 ), b = new( 50, 60 );
			PaintFill = Color.White;
			Paint.Triangle( a, a, a );
			Paint.Quad( a, a, a, a );
			Paint.Polygon( [a, a, a] );
			PaintStroke = Stroke.Solid( Color.White );
			Paint.Line( a, a );
			Paint.Arrow( a, a );
			foreach ( var width in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
			{
				PaintStroke = Stroke.Solid( Color.White, width );
				Paint.Line( a, b );
				Paint.Arrow( a, b, width );
				Paint.Arrow( a, b, headSize: width );
				Paint.Circle( new Rect( 10, 20, width, 40 ) );
				Paint.Circle( new Rect( 10, 20, 40, width ) );
			}
			PaintStroke = Stroke.Solid( Color.White );
			foreach ( var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
			{
				var invalid = new Vector2( value, 30 );
				Paint.Triangle( a, b, invalid );
				Paint.Quad( a, b, invalid, new Vector2( 10, 60 ) );
				Paint.Polygon( [a, invalid, b] );
				Paint.Line( invalid, b );
				Paint.Arrow( invalid, b );
				Paint.Circle( new Rect( value, 20, 40, 40 ) );
			}
			foreach ( var count in new[] { 0, 1, 2 } )
				Assert.ThrowsException<ArgumentOutOfRangeException>( () => Paint.Polygon( new Vector2[count] ) );
			Assert.AreEqual( 0, layer.Instances.Count );
		} );
	}
}
