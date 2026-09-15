using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	/// <summary>Closed borders keep a fixed dot count and uniform spacing, including across the closing vertex.</summary>
	[TestMethod]
	public void ClosedDotsFitThePerimeter()
	{
		WithBuffer( layer =>
		{
			Vector2[] points = [new( 10, 20 ), new( 47, 20 ), new( 47, 42 ), new( 10, 42 )];
			const float perimeter = 118;
			const int count = 17;
			for ( int step = -80; step <= 80; step++ )
			{
				layer.Clear();
				PaintStroke = new Stroke( Color.White, 2 )
				{
					Style = BorderStyle.Dotted,
					Gap = 5,
					Offset = step * 0.113f,
				};
				Paint.Polygon( points );
				var dots = Primitives( layer );
				Assert.AreEqual( count, dots.Length );
				var distances = dots.Select( dot =>
				{
					float x = dot.A.x, y = dot.A.y;
					if ( MathF.Abs( y - 20 ) < 0.0001f ) return x - 10;
					if ( MathF.Abs( x - 47 ) < 0.0001f ) return 37 + y - 20;
					if ( MathF.Abs( y - 42 ) < 0.0001f ) return 59 + 47 - x;
					Assert.AreEqual( 10f, x, 0.0001f );
					return 96 + 42 - y;
				} ).Order().ToArray();
				for ( int i = 0; i < count; i++ )
				{
					float next = i + 1 == count ? distances[0] + perimeter : distances[i + 1];
					Assert.AreEqual( perimeter / count, next - distances[i], 0.001f );
				}
			}
		} );
	}

	/// <summary>Dots move through phase zero rather than disappearing and respawning at the loop's start.</summary>
	[TestMethod]
	public void ClosedDotsCrossTheSeamContinuously()
	{
		WithBuffer( layer =>
		{
			Vector2[] previous = null;
			foreach ( var phase in new[] { -0.001f, 0f, float.Epsilon, 0.001f } )
			{
				layer.Clear();
				PaintStroke = new Stroke( Color.White, 2 ) { Style = BorderStyle.Dotted, Gap = 5, Offset = phase };
				Paint.Polygon( [new( 0, 0 ), new( 37, 0 ), new( 37, 22 ), new( 0, 22 )] );
				var current = Primitives( layer ).Select( dot => new Vector2( dot.A.x, dot.A.y ) ).ToArray();
				Assert.AreEqual( 17, current.Length );
				if ( previous is not null )
					foreach ( var point in current )
						Assert.IsTrue( previous.Min( p => (p - point).Length ) < 0.0011f, $"Dot jumped at phase {phase}" );
				previous = current;
			}
		} );
	}

	/// <summary>Positive and negative full rings fit whole dot periods; a short closed loop still has one dot.</summary>
	[TestMethod]
	public void DottedRingsAndShortLoops()
	{
		WithBuffer( layer =>
		{
			var center = new Vector2( 50, 60 );
			const float radius = 13;
			foreach ( var sweep in new[] { 360f, -360f } )
				foreach ( var phase in new[] { -14f, -0.001f, 0f, 0.001f, 2f, 7f, 100f } )
				{
					layer.Clear();
					PaintStroke = new Stroke( Color.White, 2 ) { Style = BorderStyle.Dotted, Gap = 5, Offset = phase };
					Paint.Arc( center, radius, 17, sweep );
					var dots = Primitives( layer );
					Assert.AreEqual( 12, dots.Length );
					var angles = dots.Select( dot => MathF.Atan2( dot.A.y - center.y, dot.A.x - center.x ) ).Order().ToArray();
					for ( int i = 0; i < angles.Length; i++ )
					{
						float next = i + 1 == angles.Length ? angles[0] + MathF.Tau : angles[i + 1];
						Assert.AreEqual( MathF.Tau / angles.Length, next - angles[i], 0.0001f );
					}
				}
			foreach ( var phase in new[] { -100f, 0f, 0.1f, 0.5f, 2f, 100f } )
			{
				layer.Clear();
				PaintStroke = new Stroke( Color.White, 6 ) { Style = BorderStyle.Dotted, Gap = 4, Offset = phase };
				Paint.Polygon( [new( 0, 0 ), new( 1, 0 ), new( 1, 0.5f ), new( 0, 0.5f )] );
				var dot = Primitives( layer ).Single();
				Assert.IsTrue( float.IsFinite( dot.A.x ) && float.IsFinite( dot.A.y ) );
			}
		} );
	}
}
