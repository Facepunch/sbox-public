using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	static void AssertCapBounds( PainterTestOutput layer, UICssBoxBatched.PathPrimitive primitive, Vector2 endpoint, Vector2 outward, float width, Stroke.LineCap cap )
	{
		var transverse = outward.Perpendicular;
		float size = width * (cap == Stroke.LineCap.Arrow ? 1 : 0.5f);
		Vector2[] vertices = [endpoint + outward * size, endpoint + transverse * size, endpoint - transverse * size];
		foreach ( var point in vertices )
		{
			Assert.IsTrue( layer.Instances.Any( tile => tile.PathData.Primitives.Contains( primitive )
				&& point.x >= tile.GPU.Rect.x && point.x <= tile.GPU.Rect.x + tile.GPU.Rect.z
				&& point.y >= tile.GPU.Rect.y && point.y <= tile.GPU.Rect.y + tile.GPU.Rect.w ), $"Missing cap at {point}" );
		}
	}

	/// <summary>Both cap ends retain their dimensions, including short shafts and reverse directions.</summary>
	[TestMethod]
	public void PointedCapGeometryAndBounds()
	{
		WithBuffer( layer =>
		{
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
				foreach ( var width in new[] { 0.125f, 4f, 80f } )
					foreach ( var delta in new[] { new Vector2( 0.25f, 0 ), new Vector2( 0, 20 ), new Vector2( 300, 400 ) } )
						foreach ( var sign in new[] { -1f, 1f } )
							foreach ( var scale in new[] { Vector3.One, new Vector3( 8, 0.125f, 1 ) } )
							{
								layer.Clear();
								PaintContext.State.Transform = Matrix.CreateScale( scale );
								var start = new Vector2( 10, 20 );
								var end = start + delta * sign;
								var color = Color.Red.WithAlpha( 0.25f );
								PaintStroke = new Stroke( color, width ) { Cap = cap };
								Paint.Line( start, end );
								var segment = Primitives( layer ).Single();
								Assert.AreEqual( UICssBoxBatched.PathPrimitiveKind.Segment, segment.Kind );
								Assert.AreEqual( new Vector4( (int)cap, (int)cap, 0, 0 ), segment.B );
								AssertCapBounds( layer, segment, start, (start - end).Normal, width, cap );
								AssertCapBounds( layer, segment, end, (end - start).Normal, width, cap );
								Assert.IsTrue( layer.Instances.All( tile => tile.GPU.Color == color && tile.BorderShapeData.Circle.z == width ) );
								Assert.AreEqual( 1, layer.Instances.Count, "A stroke blends its union once." );
							}
		} );
	}

	/// <summary>Dash runs cap only their ends, preserving negative phase and joins through intermediate vertices.</summary>
	[TestMethod]
	public void PointedDashRunsAndNegativePhase()
	{
		WithBuffer( layer =>
		{
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
			{
				var stroke = new Stroke( Color.White.WithAlpha( 0.25f ), 20 ) { Cap = cap, Style = BorderStyle.Dashed, DashLength = 8, Gap = 4 };
				Vector2[] points = [new( 0, 0 ), new( 6, 0 ), new( 6, 14 )];
				foreach ( var phase in new[] { 0f, -12f } )
				{
					layer.Clear();
					PaintStroke = stroke with { Offset = phase };
					Paint.Line( points );
					var data = Primitives( layer );
					Assert.AreEqual( 1, data.Count( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.RoundJoin ) );
					var first = data.Single( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A == new Vector4( 0, 0, 6, 0 ) );
					var second = data.Single( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A == new Vector4( 6, 0, 6, 2 ) );
					var last = data.Single( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.A == new Vector4( 6, 6, 6, 14 ) );
					Assert.AreEqual( new Vector4( (int)cap, (int)Stroke.LineCap.Butt, 0, 0 ), first.B );
					Assert.AreEqual( new Vector4( (int)Stroke.LineCap.Butt, (int)cap, 0, 0 ), second.B );
					Assert.AreEqual( new Vector4( (int)cap, (int)cap, 0, 0 ), last.B );
				}
				UICssBoxBatched.PathPrimitive[] expected = null;
				foreach ( var phase in new[] { 2f, -10f, -22f } )
				{
					layer.Clear();
					PaintStroke = stroke with { Offset = phase };
					Paint.Line( points );
					var data = Primitives( layer );
					Assert.AreEqual( 2, data.Length );
					Assert.IsTrue( data.All( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Segment && p.B == new Vector4( (int)cap, (int)cap, 0, 0 ) ) );
					if ( expected is not null ) CollectionAssert.AreEquivalent( expected, data );
					expected = data;
				}
				layer.Clear();
				PaintStroke = stroke with { Style = BorderStyle.Dotted };
				Paint.Line( points );
				Assert.IsTrue( Primitives( layer ).All( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Disc && p.Count == 0 ) );
			}
		} );
	}

	/// <summary>Pointed caps use whole undashed paint bounds for images and gradients, independent of dash phase.</summary>
	[TestMethod]
	public void PointedCapPaintBounds()
	{
		WithBuffer( layer =>
		{
			using var image = Texture.Create( 2, 2 ).Finish();
			Fill[] fills = [Fill.LinearGradient( Color.Red, Color.Blue ), Fill.RadialGradient( Color.Red, Color.Blue ),
				Fill.ConicGradient( Color.Red, Color.Blue ), Fill.Image( image, width: Length.Percent( 50 ), height: Length.Percent( 200 ),
					offsetX: Length.Percent( -12.5f ), offsetY: Length.Percent( -100 ) )];
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
				foreach ( var fill in fills )
					foreach ( bool arc in new[] { false, true } )
					{
						Vector4 expected = default;
						foreach ( var phase in new[] { float.NaN, 0f, 7f, -5f, -17f } )
						{
							layer.Clear();
							var stroke = new Stroke( fill, 4 ) { Cap = cap, Style = float.IsNaN( phase ) ? BorderStyle.Solid : BorderStyle.Dashed, Offset = phase };
							PaintStroke = stroke;
							if ( arc ) Paint.Arc( new Vector2( 20, 30 ), 200, 30, -270 );
							else Paint.Line( new Vector2( 10, 20 ), new Vector2( 1010, 20 ) );
							Assert.AreEqual( 1, layer.Instances.Count );
							foreach ( var tile in layer.Instances )
							{
								var background = tile.GPU.BackgroundRect;
								var bounds = new Vector4( tile.GPU.Rect.x + background.x, tile.GPU.Rect.y + background.y, background.z, background.w );
								if ( expected == default ) expected = bounds;
								Assert.AreEqual( expected.x, bounds.x, 0.001f );
								Assert.AreEqual( expected.y, bounds.y, 0.001f );
								Assert.AreEqual( expected.z, bounds.z, 0.001f );
								Assert.AreEqual( expected.w, bounds.w, 0.001f );
							}
						}
					}
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
			{
				layer.Clear();
				PaintStroke = new Stroke( fills[0], 4 ) { Cap = cap };
				Paint.Line( new Vector2( 10, 20 ), new Vector2( 30, 20 ) );
				var tile = layer.Instances.Single();
				float extent = cap == Stroke.LineCap.Arrow ? 4 : 2;
				Assert.AreEqual( new Vector4( 10 - extent, 20 - extent, 20 + extent * 2, extent * 2 ),
					new Vector4( tile.GPU.Rect.x + tile.GPU.BackgroundRect.x, tile.GPU.Rect.y + tile.GPU.BackgroundRect.y, tile.GPU.BackgroundRect.z, tile.GPU.BackgroundRect.w ) );
			}
		} );
	}

	/// <summary>Signed arc tangents retain pointed caps; complete rings ignore every cap style.</summary>
	[TestMethod]
	public void PointedArcCapsAndFullRings()
	{
		WithBuffer( layer =>
		{
			var center = new Vector2( 20, 30 );
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
				foreach ( var width in new[] { 0.125f, 4f, 80f } )
					foreach ( var sweep in new[] { 0.5f, -0.5f, 90f, -90f, 270f, -270f } )
					{
						layer.Clear();
						PaintContext.State.Transform = Matrix.CreateScale( new Vector3( 8, 0.125f, 1 ) );
						PaintStroke = new Stroke( Color.White.WithAlpha( 0.25f ), width ) { Cap = cap };
						Paint.Arc( center, 20, 30, sweep );
						var arc = Primitives( layer ).Single();
						Assert.AreEqual( UICssBoxBatched.PathPrimitiveKind.Arc, arc.Kind );
						Assert.AreEqual( (int)cap, arc.Count );
						Assert.AreEqual( MathF.PI / 6, arc.A.w, 0.0001f );
						Assert.AreEqual( sweep * MathF.PI / 180, arc.B.x, 0.0001f );
						for ( int end = 0; end < 2; end++ )
						{
							float angle = (30 + (end == 0 ? 0 : sweep)) * MathF.PI / 180;
							var radial = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
							var outward = new Vector2( -radial.y, radial.x ) * MathF.Sign( sweep ) * (end == 0 ? -1 : 1);
							AssertCapBounds( layer, arc, center + radial * 20, outward, width, cap );
						}
					}
			foreach ( var cap in new[] { Stroke.LineCap.Triangle, Stroke.LineCap.Arrow } )
			{
				UICssBoxBatched.PathPrimitive[] expected = null;
				foreach ( var phase in new[] { 7f, -5f, -17f } )
				{
					layer.Clear();
					PaintStroke = new Stroke( Color.White, 4 ) { Cap = cap, Style = BorderStyle.Dashed, DashLength = 8, Gap = 4, Offset = phase };
					Paint.Arc( center, 20, 30, -270 );
					var arcs = Primitives( layer ).OrderByDescending( p => p.A.w ).ToArray();
					Assert.IsTrue( arcs.All( p => p.Kind == UICssBoxBatched.PathPrimitiveKind.Arc && p.Count == (int)cap && p.B.x < 0 ) );
					Assert.AreEqual( MathF.PI / 6, arcs[0].A.w, 0.0001f );
					Assert.AreEqual( -1f / 20, arcs[0].B.x, 0.0001f );
					Assert.AreEqual( MathF.PI / 6 - 5f / 20, arcs[1].A.w, 0.0001f );
					if ( expected is not null ) CollectionAssert.AreEqual( expected, arcs );
					expected = arcs;
				}
			}
			foreach ( var sweep in new[] { 360f, -360f } )
			{
				UICssBoxBatched.BoxInstance[] expected = null;
				foreach ( var cap in Enum.GetValues<Stroke.LineCap>() )
				{
					layer.Clear();
					PaintStroke = new Stroke( Fill.LinearGradient( Color.Red, Color.Blue ), 80 ) { Cap = cap };
					Paint.Arc( center, 20, 30, sweep );
					Assert.AreEqual( UICssBoxBatched.PathCap.Ring, Primitives( layer ).Single().Count );
					var tiles = layer.Instances.Select( tile => tile.GPU ).ToArray();
					if ( expected is not null ) CollectionAssert.AreEqual( expected, tiles );
					expected = tiles;
				}
			}
		} );
	}
}
