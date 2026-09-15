using Sandbox.Rendering;
using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	static UICssBoxBatched.PathPrimitive[] Primitives( PainterTestOutput layer )
	{
		return layer.Instances.Where( i => i.PathData is not null ).SelectMany( i => i.PathData.Primitives.ToArray() ).Distinct().ToArray();
	}

	/// <summary>Caps and joins encode centered geometry, including miter-limit fallback.</summary>
	[TestMethod]
	public void StyledCapsAndJoins()
	{
		WithBuffer( layer =>
		{
			foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
			{
				layer.Clear();
				PaintStroke = new Stroke( Color.Red, 4 ) { Cap = cap };
				Paint.Line( new Vector2( 0, 0 ), new Vector2( 20, 0 ) );
				var data = Primitives( layer );
				var segment = data.Single( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment );
				Assert.AreEqual( new Vector4( 0, 0, 20, 0 ), segment.A );
				Assert.AreEqual( cap == Stroke.LineCap.Square ? UICssBoxBatched.PathCap.SquareStart | UICssBoxBatched.PathCap.SquareEnd : 0, segment.Count );
				bool pointed = cap == Stroke.LineCap.Triangle || cap == Stroke.LineCap.Arrow;
				Assert.AreEqual( pointed ? new Vector4( (int)cap, (int)cap, 0, 0 ) : Vector4.Zero, segment.B );
				Assert.AreEqual( cap == Stroke.LineCap.Round ? 3 : 1, data.Length );
				CollectionAssert.AreEquivalent( cap == Stroke.LineCap.Round ? new[] { Vector4.Zero, new Vector4( 20, 0, 0, 0 ) } : Array.Empty<Vector4>(), data.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc ).Select( p => p.A ).ToArray() );
				foreach ( var tile in layer.Instances )
				{
					Assert.AreEqual( 4f, tile.BorderShapeData.Circle.z );
					Assert.AreEqual( Vector4.Zero, tile.GPU.BorderSize );
					Assert.IsTrue( tile.GPU.Rect.y <= -2 && tile.GPU.Rect.y + tile.GPU.Rect.w >= 2 );
				}
			}
			foreach ( var join in Enum.GetValues<Stroke.LineJoin>() )
				foreach ( var limit in new[] { 1f, 4f } )
					foreach ( var direction in new[] { -1f, 1f } )
					{
						layer.Clear();
						PaintStroke = new Stroke( Color.White, 4 ) { Join = join, MiterLimit = limit };
						Paint.Line( [new( 0, 0 ), new( 20, 0 ), new( 20, 20 * direction )] );
						var corner = Primitives( layer ).Single( p => p.Kind != UICssBoxBatched.PathPrimitiveKind.Segment );
						bool miter = join == Stroke.LineJoin.Miter && limit == 4;
						Assert.AreEqual( join == Stroke.LineJoin.Round ? UICssBoxBatched.PathPrimitiveKind.RoundJoin : UICssBoxBatched.PathPrimitiveKind.Join, corner.Kind );
						Assert.AreEqual( miter ? 5 : 4, corner.Count );
						if ( join == Stroke.LineJoin.Round )
						{
							Assert.AreEqual( new Vector4( 20, 0, 0, 0 ), corner.A );
							Assert.AreEqual( new Vector4( 20, 20, 0, 0 ), corner.B );
							Assert.AreEqual( new Vector4( 1, 0, 0, direction ), corner.C );
							continue;
						}
						Assert.AreEqual( new Vector4( 20, 0, 0, -direction ), corner.A );
						Assert.AreEqual( 1f, corner.B.x, 0.001f );
						Assert.AreEqual( miter ? -direction : 0, corner.B.y, 0.001f );
					}
		} );
	}

	/// <summary>Closed paths remove duplicate endpoints and use joins instead of caps.</summary>
	[TestMethod]
	public void ClosedPathsAndDuplicatePoints()
	{
		WithBuffer( layer =>
		{
			Vector2[] points = [new( 0, 0 ), new( 10, 0 ), new( 10, 10 ), new( 0, 10 )];
			foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
			{
				layer.Clear();
				var stroke = new Stroke( Color.White, 2 ) { Cap = cap };
				PaintStroke = stroke;
				Paint.Polygon( points );
				var expected = Primitives( layer );
				Assert.AreEqual( 4, expected.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.Count == 0 ) );
				Assert.IsTrue( expected.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).All( p => p.B == Vector4.Zero ), "Closed paths have no pointed caps." );
				Assert.AreEqual( 4, expected.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.RoundJoin ) );
				Assert.AreEqual( 8, expected.Length );
				layer.Clear();
				PaintStroke = stroke;
				Paint.Polygon( [points[0], points[0], points[1], points[1], points[2], points[3], points[0]] );
				CollectionAssert.AreEquivalent( expected, Primitives( layer ) );
			}
			foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
			{
				layer.Clear();
				PaintStroke = new Stroke( Color.White, 2 ) { Style = BorderStyle.Dashed, DashLength = 8, Gap = 4, Offset = 2, Cap = cap };
				Paint.Polygon( points );
				var dashed = Primitives( layer );
				Assert.IsTrue( dashed.Any( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.RoundJoin && p.A.x == 0 && p.A.y == 0 ), "A dash crossing the closing vertex must join." );
				Assert.AreEqual( cap == Stroke.LineCap.Round ? 6 : 0, dashed.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc ), "Three runs have six caps, not eight." );
				if ( cap == Stroke.LineCap.Triangle || cap == Stroke.LineCap.Arrow )
				{
					Assert.AreEqual( 3, dashed.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.B.x == (int)cap ) );
					Assert.AreEqual( 3, dashed.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.B.y == (int)cap ) );
					Assert.IsTrue( dashed.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A.x == 0 && p.A.y == 0 ).All( p => p.B.x == (int)Stroke.LineCap.Butt ) );
					Assert.IsTrue( dashed.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A.z == 0 && p.A.w == 0 ).All( p => p.B.y == (int)Stroke.LineCap.Butt ) );
				}
			}
		} );
	}

	/// <summary>Dash phase is continuous through vertices; dots are spaced by width plus gap.</summary>
	[TestMethod]
	public void DashOffsetsAndDotCenters()
	{
		WithBuffer( layer =>
		{
			Vector2[] points = [new( 0, 0 ), new( 6, 0 ), new( 6, 14 )];
			foreach ( var offset in new[] { 0f, 12f, -12f, 2f, -10f } )
			{
				layer.Clear();
				PaintStroke = Stroke.Dashed( Color.White, 2, 8, 4, offset );
				Paint.Line( points );
				Vector4[] expected = offset == 2 || offset == -10
					? [new( 0, 0, 6, 0 ), new( 6, 4, 6, 12 )]
					: [new( 0, 0, 6, 0 ), new( 6, 0, 6, 2 ), new( 6, 6, 6, 14 )];
				CollectionAssert.AreEquivalent( expected, Primitives( layer ).Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).Select( p => p.A ).ToArray() );
			}
			layer.Clear();
			PaintStroke = Stroke.Dotted( Color.White, 2, 3, 2 );
			Paint.Line( points );
			var dots = Primitives( layer );
			Assert.IsTrue( dots.All( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc ) );
			CollectionAssert.AreEquivalent( new[] { new Vector4( 3, 0, 0, 0 ), new Vector4( 6, 2, 0, 0 ), new Vector4( 6, 7, 0, 0 ), new Vector4( 6, 12, 0, 0 ) }, dots.Select( p => p.A ).ToArray() );
		} );
	}

	/// <summary>Long polygons and strokes retain every edge and own their input geometry.</summary>
	[TestMethod]
	public void LongPathsAreOwnedAndNeverTruncated()
	{
		WithBuffer( layer =>
		{
			var points = Enumerable.Range( 0, 2000 ).Select( i => new Vector2( i * 2, i % 2 * 8 ) ).ToArray();
			foreach ( var count in new[] { 9, 2000 } )
			{
				layer.Clear();
				PaintStroke = Stroke.Solid( Color.Red, 2 );
				PaintFill = Color.White;
				Paint.Polygon( points.AsSpan( 0, count ) );
				Assert.AreEqual( 2, layer.Instances.Count );
				var polygon = layer.Instances[0];
				Assert.AreEqual( UICssBoxBatched.ShapeKind.PolygonPath, polygon.BorderShapeData.Kind );
				Assert.AreEqual( new Vector4( 0, 0, (count - 1) * 2, 8 ), polygon.GPU.Rect );
				Assert.AreEqual( Vector4.Zero, polygon.GPU.BorderSize );
				Assert.AreEqual( 2f, layer.Instances[1].BorderShapeData.Circle.z );
				Assert.AreEqual( count, layer.Instances[1].PathData.Primitives.ToArray().Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ) );
				Assert.AreEqual( count, polygon.PathData.Primitives.Length );
				Assert.AreEqual( new Vector4( points[count - 1].x, points[count - 1].y, 0, 0 ), polygon.PathData.Primitives[^1].A );
			}
			var polygonData = layer.Instances[0].PathData;
			var polygonCopy = polygonData.Primitives.ToArray();
			layer.Clear();
			PaintStroke = Stroke.Solid( Color.Red, 2 );
			Paint.Line( points );
			Assert.AreEqual( 1, layer.Instances.Count );
			var data = Primitives( layer );
			var segments = data.Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).Select( p => p.A ).ToHashSet();
			Assert.AreEqual( 1999, segments.Count );
			for ( int i = 0; i < points.Length - 1; i++ )
				Assert.IsTrue( segments.Contains( new Vector4( points[i].x, points[i].y, points[i + 1].x, points[i + 1].y ) ), $"Missing edge {i}" );
			Array.Fill( points, new Vector2( float.NaN ) );
			CollectionAssert.AreEqual( data, Primitives( layer ) );
			Assert.IsTrue( polygonData.Primitives.SequenceEqual( polygonCopy ) );
			Assert.IsTrue( layer.Instances.All( i => i.BorderShapeData.Kind == UICssBoxBatched.ShapeKind.StrokePath && i.GPU.ShapeIndex >= 0 ) );
		} );
	}

	/// <summary>Paths retain whole-path paint coordinates, opacity and blend, regardless of dash phase.</summary>
	[TestMethod]
	public void StrokePaintMapping()
	{
		WithBuffer( layer =>
		{
			using var image = Texture.Create( 2, 2 ).Finish();
			using var array = Texture.CreateArray( 2, 2, 2 ).Finish();
			PaintContext.InheritedOpacity = 0.25f;
			PaintContext.State.OverrideBlendMode = BlendMode.Multiply;
			var tint = Color.Red.WithAlpha( 0.8f );
			Fill[] fills = [tint, Fill.LinearGradient( Color.Red, Color.Blue ), Fill.RadialGradient( Color.Red, Color.Blue ), Fill.ConicGradient( Color.Red, Color.Blue ), Fill.Image( image, tint, width: Length.Percent( 50 ), height: Length.Percent( 200 ), offsetX: Length.Percent( -12.5f ), offsetY: Length.Percent( -100 ), repeat: BackgroundRepeat.RepeatX, filter: FilterMode.Point ), array];
			for ( int f = 0; f < fills.Length; f++ )
				foreach ( var phase in new[] { 0f, 7f } )
				{
					layer.Clear();
					PaintStroke = new Stroke( fills[f], 4 ) { Style = BorderStyle.Dashed, Offset = phase };
					Paint.Line( new Vector2( 10, 20 ), new Vector2( 1010, 20 ) );
					Assert.AreEqual( 1, layer.Instances.Count );
					Primitives( layer );
					foreach ( var tile in layer.Instances )
					{
						Assert.AreEqual( BlendMode.Multiply, layer.BlendMode );
						Assert.AreEqual( f == 0 ? tint.WithAlpha( 0.2f ) : Color.Transparent, tile.GPU.Color );
						if ( f == 0 ) continue;
						var background = tile.GPU.BackgroundRect;
						Assert.AreEqual( f == 4 ? -117.5f : 8f, tile.GPU.Rect.x + background.x, 0.001f );
						Assert.AreEqual( f == 4 ? 14f : 18f, tile.GPU.Rect.y + background.y, 0.001f );
						Assert.AreEqual( f == 4 ? 502f : 1004f, background.z, 0.001f );
						Assert.AreEqual( f == 4 ? 8f : 4f, background.w, 0.001f );
						Assert.AreEqual( f == 4 ? tint.WithAlpha( 0.2f ) : Color.White.WithAlpha( 0.25f ), tile.GPU.BackgroundTint );
						if ( f < 4 )
						{
							Assert.AreEqual( 2, tile.BackgroundGradient.Count );
							Assert.AreEqual( f - 1, (int)tile.BackgroundGradient.Type );
						}
						else
						{
							Assert.AreSame( f == 4 ? image : array, tile.BackgroundImage );
							Assert.AreEqual( (f == 4 ? image : array).Index, tile.GPU.TextureIndex );
							// The empty test renderer can return zero for bindless texture indices.
							Assert.AreEqual( (int)(f == 4 ? BackgroundRepeat.RepeatX : BackgroundRepeat.Clamp), tile.GPU.BackgroundRepeat );
							if ( f == 4 ) Assert.AreEqual( new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = FilterMode.Point }.Index, tile.GPU.SamplerIndex );
						}
					}
				}
		} );
	}

	/// <summary>Gradient stop arrays are copied and public angles map from clockwise degrees to shader radians.</summary>
	[TestMethod]
	public void GradientStopsAndAngles()
	{
		WithBuffer( layer =>
		{
			var stops = Enumerable.Range( 0, 8 ).Select( i => new Fill.GradientStop( i / 7f, Color.Red.WithAlpha( i / 7f ) ) ).ToArray();
			var expected = stops.ToArray();
			Fill[] fills = [Fill.LinearGradient( stops, 30 ), Fill.RadialGradient( stops ), Fill.ConicGradient( stops, 30 )];
			Array.Fill( stops, new Fill.GradientStop( 1, Color.Blue ) );
			foreach ( var fill in fills )
			{
				PaintStroke = Stroke.None;
				PaintFill = fill;
				Paint.Rect( new Rect( 10, 20, 80, 40 ) );
			}
			for ( int i = 0; i < fills.Length; i++ )
			{
				var gradient = layer.Instances[i].BackgroundGradient;
				Assert.AreEqual( 8, gradient.Count );
				Assert.AreEqual( new Vector2( 0.5f ), gradient.Center );
				Assert.AreEqual( 3, gradient.CenterUnits );
				Assert.AreEqual( 0, gradient.Circle );
				Assert.AreEqual( (int)GradientInfo.RadialSizeMode.FarthestSide, gradient.SizeMode );
				Assert.AreEqual( (int)(i == 0 ? GradientInfo.GradientTypes.Linear : i == 1 ? GradientInfo.GradientTypes.Radial : GradientInfo.GradientTypes.Conic), gradient.Type );
				if ( i != 1 ) Assert.AreEqual( (i == 0 ? 60 : 120) * MathF.PI / 180, gradient.Angle, 0.0001f );
				for ( int j = 0; j < 8; j++ )
				{
					Assert.AreEqual( expected[j].Offset, gradient.StopOffsets[j] );
					Assert.AreEqual( expected[j].Color, gradient.StopColors[j] );
				}
			}
		} );
	}

	/// <summary>Styled primitives submit boxes and analytic arcs use signed radians with capless full rings.</summary>
	[TestMethod]
	public void StyledPrimitiveCoverageAndArcAngles()
	{
		WithBuffer( layer =>
		{
			Fill fill = Color.Blue;
			var stroke = new Stroke( Color.Red, 4 );
			var rect = new Rect( 10, 20, 60, 40 );
			PaintFill = fill;
			Action[] draws = [
				() => Paint.Triangle( rect.TopLeft, rect.TopRight, rect.BottomLeft ),
				() => Paint.Triangle( new Triangle( new Vector3( 10, 20, 100 ), new Vector3( 70, 20, -100 ), new Vector3( 10, 60, 50 ) ) ),
				() => Paint.Quad( rect.TopLeft, rect.TopRight, rect.BottomRight, rect.BottomLeft ),
				() => Paint.Rect( rect, 8 ),
				() => Paint.Rect( rect, new Painter.CornerRadii( new( 4 ), new( 2 ), new( 1 ), new( 3 ) ) ),
				() => Paint.Circle( rect.Center, 20 ),
				() => Paint.Arc( rect.Center, 20, 0, 360 ),
				() => Paint.Circle( rect ),
				() => Paint.Pie( rect.Center, 20, 0, 90 ),
				() => Paint.Line( new Line( new Vector3( 10, 20, 100 ), new Vector3( 70, 20, -100 ) ) ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomRight ),
				() => Paint.Bezier( rect.TopLeft, rect.TopRight, rect.BottomLeft, rect.BottomRight )];
			PaintStroke = stroke;
			foreach ( var draw in draws )
			{
				layer.Clear();
				draw();
				Assert.IsTrue( Primitives( layer ).Length > 0 );
			}
			foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
				foreach ( var sweep in new[] { 90f, -90f, 360f, -360f, 720f } )
				{
					layer.Clear();
					PaintStroke = stroke with { Cap = cap };
					Paint.Arc( rect.Center, 20, 0, sweep );
					var arc = Primitives( layer ).Single();
					Assert.AreEqual( UICssBoxBatched.PathPrimitiveKind.Arc, arc.Kind );
					Assert.AreEqual( new Vector4( rect.Center.x, rect.Center.y, 20, 0 ), arc.A );
					Assert.AreEqual( Math.Clamp( sweep, -360, 360 ) * MathF.PI / 180, arc.B.x, 0.0001f );
					Assert.AreEqual( MathF.Abs( sweep ) >= 360 ? UICssBoxBatched.PathCap.Ring : (int)cap, arc.Count );
				}
		} );
	}

	/// <summary>Collinear vertices overlap at the seam; retracing Beziers retain their excursion.</summary>
	[TestMethod]
	public void CollinearPathsKeepCoverage()
	{
		WithBuffer( layer =>
		{
			var stroke = new Stroke( Color.White, 2 );
			PaintStroke = stroke;
			Paint.Line( [new( 0, 0 ), new( 20, 0 ), new( 40, 0 )] );
			Assert.IsTrue( Primitives( layer ).Any( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.RoundJoin && p.A == new Vector4( 20, 0, 0, 0 ) ), "The shared endpoint needs interior coverage." );
			layer.Clear();
			PaintStroke = stroke;
			Paint.Bezier( Vector2.Zero, new Vector2( 100, 0 ), new Vector2( -100, 0 ), Vector2.Zero );
			var segments = Primitives( layer ).Where( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment ).ToArray();
			Assert.IsTrue( segments.Any( p => MathF.Max( p.A.x, p.A.z ) > 28 ) );
			Assert.IsTrue( segments.Any( p => MathF.Min( p.A.x, p.A.z ) < -28 ) );
		} );
	}
}
