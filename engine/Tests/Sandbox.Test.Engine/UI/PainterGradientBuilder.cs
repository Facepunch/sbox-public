using System;
using Sandbox.Rendering;
using Sandbox.UI;

namespace UITests;

[TestClass]
public class PainterGradientBuilderTest
{
	static GradientInfo Describe( Fill fill )
	{
		fill.CreateDescriptor( new Rect( 10, 20, 200, 100 ), 1, BlendMode.Normal, Vector4.Zero, out var descriptor );
		return descriptor.BackgroundGradient;
	}

	[TestMethod]
	public void FluentGradientsMatchSpanFactories()
	{
		Fill.GradientStop[] stops = [new( 0, Color.Red ), new( 0.3f, Color.Orange ), new( 0.3f, Color.Yellow ), new( 1, Color.Green )];
		var builders = new[] { Fill.LinearGradient(), Fill.RadialGradient(), Fill.ConicGradient() };
		Fill[] expected = [Fill.LinearGradient( stops ), Fill.RadialGradient( stops ), Fill.ConicGradient( stops )];
		for ( int i = 0; i < builders.Length; i++ )
		{
			var builder = builders[i];
			foreach ( var stop in stops ) builder = builder.WithStop( stop.Offset, stop.Color );
			var actual = Describe( builder );
			Assert.IsTrue( actual.Equals( Describe( expected[i] ) ) );
			Assert.AreEqual( actual.GetHashCode(), Describe( expected[i] ).GetHashCode() );
			var gpu = UICssBoxBatched.GradientInstance.From( actual );
			Assert.AreEqual( 4, gpu.Count );
			for ( int j = 0; j < stops.Length; j++ )
			{
				Assert.AreEqual( stops[j].Color, gpu.StopColors[j] );
				Assert.AreEqual( stops[j].Offset, gpu.StopOffsets[j] );
			}
		}
	}

	[TestMethod]
	public void BranchesOwnTheirStopsAndEnforceCapacity()
	{
		var shared = Fill.LinearGradient().WithStop( 0, Color.Red );
		Fill blue = shared.WithStop( 1, Color.Blue );
		Fill green = shared.WithStop( 1, Color.Green );
		Assert.AreEqual( Color.Blue, Describe( blue ).ColorOffsets[1].color );
		Assert.AreEqual( Color.Green, Describe( green ).ColorOffsets[1].color );
		Assert.ThrowsException<InvalidOperationException>( () => { Fill incomplete = shared; } );

		var full = Fill.ConicGradient();
		for ( int i = 0; i < GradientInfo.MaxStops; i++ ) full = full.WithStop( i / 7f, Color.White );
		Assert.AreEqual( 8, Describe( full ).ColorOffsets.Length );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => full.WithStop( 1, Color.Black ) );
		Assert.AreEqual( 8, Describe( full ).ColorOffsets.Length );
	}

	[TestMethod]
	public void InvalidStopsAndIncompleteGradientsAreRejected()
	{
		Assert.ThrowsException<InvalidOperationException>( () => { Fill empty = Fill.LinearGradient(); } );
		foreach ( float offset in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity } )
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient().WithStop( offset, Color.Red ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient().WithStop( 0.5f, Color.Red ).WithStop( 0.4f, Color.Blue ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient().WithStop( 0, new Color( float.NaN, 0, 0 ) ) );
	}

	[TestMethod]
	public void AnglesReplaceRatherThanAccumulateAndPreserveStops()
	{
		foreach ( float angle in new[] { -450f, -90f, 0f, 45f, 90f, 450f } )
		{
			var linear = Fill.LinearGradient().WithStop( 0, Color.Red ).WithStop( 1, Color.Blue );
			var conic = Fill.ConicGradient().WithStop( 0, Color.Red ).WithStop( 1, Color.Blue );
			Assert.IsTrue( Describe( linear.WithAngle( 12 ).WithAngle( angle ) ).Equals( Describe( Fill.LinearGradient( Color.Red, Color.Blue, angle ) ) ) );
			Assert.IsTrue( Describe( conic.WithAngle( angle ) ).Equals( Describe( Fill.ConicGradient( Color.Red, Color.Blue, angle ) ) ) );
			Assert.IsTrue( Describe( ((Fill)linear).WithAngle( angle ) ).Equals( Describe( linear.WithAngle( angle ) ) ) );
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Fill.LinearGradient().WithAngle( float.NaN ) );
		Assert.ThrowsException<InvalidOperationException>( () => Fill.RadialGradient().WithAngle( 30 ) );
		Assert.ThrowsException<InvalidOperationException>( () => Fill.Solid( Color.Red ).WithAngle( 30 ) );
		Assert.ThrowsException<InvalidOperationException>( () => Fill.LinearGradient( Vector2.Zero, new Vector2( 100, 0 ), Color.Red, Color.Blue ).WithAngle( 30 ) );
	}

	[TestMethod]
	public void TextGradientsRetainMoreThanEightStops()
	{
		var sheet = StyleParser.ParseSheet( ".test { color: linear-gradient(red, orange, yellow, green, cyan, blue, purple, white, black); }" );
		var stops = sheet.Nodes[0].Styles.TextGradient.ColorOffsets;
		Assert.IsTrue( stops.Length > GradientInfo.MaxStops );
		Assert.AreEqual( Color.Black, stops[^1].color );
	}
}
