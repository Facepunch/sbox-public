using Sandbox.UI;
using System;

namespace EngineTests;

public partial class PanelDrawTest : PainterTestBase
{
	[TestMethod]
	public void LengthGradientCoordinatesResolveForEachShape()
	{
		WithBuffer( layer =>
		{
			Fill[] fills = [
				Fill.LinearGradient( Length.Percent( 25 ), Length.Percent( 50 ), Length.Percent( 75 ), Length.Percent( 50 ), Color.Red, Color.Blue ),
				Fill.RadialGradient( Length.Percent( 25 ), Length.Percent( 50 ), Length.Percent( 75 ), Length.Percent( 50 ), Color.Red, Color.Blue ),
				Fill.ConicGradient( Length.Percent( 25 ), Length.Percent( 50 ), Length.Percent( 75 ), Length.Percent( 50 ), Color.Red, Color.Blue )];
			foreach ( var rect in new[] { new Rect( 100, 200, 200, 100 ), new Rect( 350, 500, 400, 200 ) } )
			{
				layer.Clear();
				for ( int i = 0; i < fills.Length; i++ )
				{
					PaintFill = fills[i];
					Paint.Rect( rect );
				}
				var linear = layer.Instances[0];
				Assert.AreEqual( new Vector4( rect.Width * 0.25f, 0, rect.Width * 0.5f, rect.Width * 0.5f ), linear.GPU.BackgroundRect );
				Assert.AreEqual( MathF.PI * 0.5f, linear.BackgroundGradient.Angle, 0.0001f );
				var radial = layer.Instances[1];
				Assert.AreEqual( new Vector4( -rect.Width * 0.25f, -rect.Width * 0.25f, rect.Width, rect.Width ), radial.GPU.BackgroundRect );
				Assert.AreEqual( 1, radial.BackgroundGradient.Circle );
				var conic = layer.Instances[2];
				Assert.AreEqual( new Vector4( 0, 0, rect.Width, rect.Height ), conic.GPU.BackgroundRect );
				var gradient = conic.BackgroundGradient;
				Assert.AreEqual( new Vector2( rect.Width * 0.25f, rect.Height * 0.5f ), gradient.Center );
				Assert.AreEqual( 0, gradient.CenterUnits );
				Assert.AreEqual( MathF.PI * 0.5f, gradient.Angle, 0.0001f );
			}
		} );
	}

	[TestMethod]
	public void GradientCoordinatesMixPercentagesPixelsAndCalc()
	{
		WithBuffer( layer =>
		{
			Fill.GradientStop[] stops = [new( 0, Color.Red ), new( 1, Color.Blue )];
			var x = Length.Percent( 50 );
			var endX = Length.Calc( "calc(50% + 50px)" );
			Fill[] fills = [Fill.LinearGradient( x, 225, endX, 225, stops ), Fill.RadialGradient( x, 225, endX, 225, stops ), Fill.ConicGradient( x, 225, endX, 225, stops )];
			foreach ( var fill in fills )
			{
				PaintFill = fill;
				Paint.Rect( new Rect( 100, 200, 200, 100 ) );
			}
			Assert.AreEqual( new Vector4( 100, 0, 50, 50 ), layer.Instances[0].GPU.BackgroundRect );
			Assert.AreEqual( new Vector4( 50, -25, 100, 100 ), layer.Instances[1].GPU.BackgroundRect );
			Assert.AreEqual( new Vector2( 100, 25 ), layer.Instances[2].BackgroundGradient.Center );
			Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			{
				PaintFill = Fill.RadialGradient( x, Length.Percent( 50 ), 200, 250, stops );
				Paint.Rect( new Rect( 100, 200, 200, 100 ) );
			}, "Mixed coordinates that coincide after resolving the shape bounds must be rejected." );
			Assert.AreEqual( 3, layer.Instances.Count );
		} );
	}

	[TestMethod]
	public void LinearAndConicPointDirections()
	{
		WithBuffer( layer =>
		{
			var center = new Vector2( 110, 80 );
			foreach ( var (delta, linearAngle, conicAngle) in new[] {
				(new Vector2( 100, 0 ), 90f, 90f), (new Vector2( 0, 100 ), 0f, 180f),
				(new Vector2( -100, 0 ), -90f, -90f), (new Vector2( 0, -100 ), 180f, 0f) } )
			{
				layer.Clear();
				PaintFill = Fill.LinearGradient( center, center + delta, Color.Red, Color.Blue );
				Paint.Rect( new Rect( 20, 30, 300, 200 ) );
				Assert.AreEqual( linearAngle * MathF.PI / 180, layer.Instances[0].BackgroundGradient.Angle, 0.0001f );
				Assert.AreEqual( 100f, layer.Instances[0].GPU.BackgroundRect.z );
				PaintFill = Fill.ConicGradient( center, center + delta, Color.Red, Color.Blue );
				Paint.Rect( new Rect( 20, 30, 300, 200 ) );
				var conic = layer.Instances[1].BackgroundGradient;
				Assert.AreEqual( conicAngle * MathF.PI / 180, conic.Angle, 0.0001f );
				Assert.AreEqual( new Vector2( 90, 50 ), conic.Center );
			}
			PaintFill = Fill.LinearGradient( center, center + new Vector2( 30, 40 ), Color.Red, Color.Blue );
			Paint.Rect( new Rect( 0, 0, 300, 200 ) );
			var diagonal = layer.Instances[^1];
			Assert.AreEqual( 35.714286f, diagonal.GPU.BackgroundRect.z, 0.0001f );
			Assert.AreEqual( 107.14286f, diagonal.GPU.BackgroundRect.x, 0.0001f );
			Assert.AreEqual( 82.14286f, diagonal.GPU.BackgroundRect.y, 0.0001f );
			Assert.AreEqual( 36.869898f * MathF.PI / 180, diagonal.BackgroundGradient.Angle, 0.0001f );
		} );
	}

	[TestMethod]
	public void RadialGradientCoordinatesAreShared()
	{
		WithBuffer( layer =>
		{
			Fill.GradientStop[] stops = [new( 0, Color.White ), new( 0.4f, Color.Green ), new( 1, Color.Blue )];
			// A diagonal edge also measures a 50px radius; neither axis alone defines it.
			var fill = Fill.RadialGradient( new Vector2( 110, 80 ), new Vector2( 140, 120 ), stops );
			Array.Fill( stops, new Fill.GradientStop( 1, Color.Red ) );
			foreach ( bool transformed in new[] { false, true } )
			{
				layer.Clear();
				using var scope = Paint.Scope();
				if ( transformed )
				{
					Paint.Translate( 20, 30 );
					Paint.Rotate( 25 );
					Paint.Scale( -2, 3 );
				}
				PaintFill = fill;
				foreach ( var rect in new[] { new Rect( 0, 0, 300, 100 ), new Rect( 90, 60, 40, 160 ), new Rect( 500, 300, 20, 10 ) } )
				{
					Paint.Rect( rect );
					Paint.Circle( rect );
					Paint.Triangle( rect.TopLeft, rect.TopRight, rect.BottomLeft );
				}
				PaintStroke = new Stroke( fill, 8 ) { Cap = Stroke.LineCap.Arrow };
				Paint.Line( new Vector2( 50, 60 ), new Vector2( 170, 100 ) );
				Assert.AreEqual( 10, layer.Instances.Count );
				foreach ( var instance in layer.Instances )
				{
					var gpu = instance.GPU;
					Assert.AreEqual( 60f, gpu.Rect.x + gpu.BackgroundRect.x, 0.001f );
					Assert.AreEqual( 30f, gpu.Rect.y + gpu.BackgroundRect.y, 0.001f );
					Assert.AreEqual( 100f, gpu.BackgroundRect.z );
					Assert.AreEqual( 100f, gpu.BackgroundRect.w );
					Assert.AreEqual( (int)BackgroundRepeat.Clamp, gpu.BackgroundRepeat, "The edge color extends beyond the circle." );
					Assert.AreEqual( PaintTransform, instance.Transform );
					var gradient = instance.BackgroundGradient;
					Assert.AreEqual( (int)GradientInfo.GradientTypes.Radial, gradient.Type );
					Assert.AreEqual( 1, gradient.Circle );
					Assert.AreEqual( new Vector2( 0.5f ), gradient.Center );
					Assert.AreEqual( 3, gradient.CenterUnits );
					Assert.AreEqual( 3, gradient.Count );
					Assert.AreEqual( 0.4f, gradient.StopOffsets[1] );
					Assert.AreEqual( Color.White, gradient.StopColors[0] );
					Assert.AreEqual( Color.Green, gradient.StopColors[1] );
					Assert.AreEqual( Color.Blue, gradient.StopColors[2] );
				}
			}
		} );
	}
}
