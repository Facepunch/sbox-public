using Sandbox.UI;

namespace UITests;

[TestClass]
public class BorderRadiiTest
{
	[TestMethod]
	public void EqualityChecksEveryRadius()
	{
		float[] values = [ 0, System.BitConverter.Int32BitsToSingle( int.MinValue ), 1, -1, float.Epsilon,
			float.PositiveInfinity, float.NegativeInfinity, float.NaN ];
		for ( int component = 0; component < 8; component++ )
		{
			foreach ( var value in values )
			{
				var components = new float[8];
				components[component] = value;
				var radii = new BorderRadii
				{
					TopLeft = new Vector2( components[0], components[1] ),
					TopRight = new Vector2( components[2], components[3] ),
					BottomLeft = new Vector2( components[4], components[5] ),
					BottomRight = new Vector2( components[6], components[7] )
				};
				Assert.AreEqual( value == 0, radii.IsZero );
				Assert.AreEqual( value == 0, radii.Equals( BorderRadii.Zero ) );
				Assert.AreEqual( !float.IsNaN( value ), radii.Equals( radii ) );
			}
		}
	}

	[TestMethod]
	public void ClampingScalesEveryRadius()
	{
		var radii = new BorderRadii
		{
			TopLeft = new Vector2( 10, 40 ),
			TopRight = new Vector2( 20, 80 ),
			BottomLeft = new Vector2( 30, 60 ),
			BottomRight = new Vector2( 40, 40 )
		}.Clamped( 200, 60 );

		Assert.AreEqual( new Vector2( 5, 20 ), radii.TopLeft );
		Assert.AreEqual( new Vector2( 10, 40 ), radii.TopRight );
		Assert.AreEqual( new Vector2( 15, 30 ), radii.BottomLeft );
		Assert.AreEqual( new Vector2( 20, 20 ), radii.BottomRight );
	}

	static Styles Radius( string tl, string tr, string bl, string br )
	{
		return new Styles
		{
			BorderTopLeftRadius = Length.Parse( tl ),
			BorderTopRightRadius = Length.Parse( tr ),
			BorderBottomLeftRadius = Length.Parse( bl ),
			BorderBottomRightRadius = Length.Parse( br ),
		};
	}

	static Styles Radius( string all ) => Radius( all, all, all, all );

	[TestMethod]
	public void PixelsPassThrough()
	{
		var r = Radius( "8px" ).GetBorderRadii( new Rect( 0, 0, 100, 100 ) );

		Assert.AreEqual( new Vector2( 8, 8 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 8, 8 ), r.BottomRight );
	}

	/// <summary>
	/// Percentages resolve against width horizontally and height vertically, so 50% on a wide box
	/// is an ellipse like the web, not a circle.
	/// </summary>
	[TestMethod]
	public void PercentResolvesPerAxis()
	{
		var r = Radius( "50%" ).GetBorderRadii( new Rect( 0, 0, 200, 100 ) );

		Assert.AreEqual( new Vector2( 100, 50 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 100, 50 ), r.BottomRight );

		// Circle-only shaders get the smaller of the two, which is a pill here
		Assert.AreEqual( new Vector4( 50, 50, 50, 50 ), r.ToVector4() );
	}

	/// <summary>
	/// Radii too big for a side all scale by one factor, keeping their shape.
	/// </summary>
	[TestMethod]
	public void OverlapScalesUniformly()
	{
		var r = Radius( "40px" ).GetBorderRadii( new Rect( 0, 0, 100, 50 ) );

		// Left side sums to 80 in a 50 high box: 50/80
		Assert.AreEqual( 25f, r.TopLeft.x, 0.001f );
		Assert.AreEqual( 25f, r.TopLeft.y, 0.001f );
		Assert.AreEqual( 25f, r.BottomRight.x, 0.001f );
	}

	/// <summary>
	/// A single big corner is only limited by the sides it sits on, not by half the smaller
	/// dimension. 30px on the top of a 40px tall box stays 30px.
	/// </summary>
	[TestMethod]
	public void AsymmetricRadiiOnlyClampAgainstTheirSides()
	{
		var r = Radius( "30px", "30px", "0", "0" ).GetBorderRadii( new Rect( 0, 0, 100, 40 ) );

		Assert.AreEqual( new Vector2( 30, 30 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 30, 30 ), r.TopRight );
		Assert.AreEqual( Vector2.Zero, r.BottomLeft );

		var single = Radius( "50px", "0", "0", "0" ).GetBorderRadii( new Rect( 0, 0, 100, 50 ) );
		Assert.AreEqual( new Vector2( 50, 50 ), single.TopLeft );
	}

	[TestMethod]
	public void InnerSubtractsBorderPerSide()
	{
		var r = Radius( "20px" ).GetBorderRadii( new Rect( 0, 0, 100, 100 ) );
		var inner = r.Inner( new Vector4( 10, 2, 0, 0 ) ); // left 10, top 2

		Assert.AreEqual( new Vector2( 10, 18 ), inner.TopLeft );
		Assert.AreEqual( new Vector2( 20, 18 ), inner.TopRight );
		Assert.AreEqual( new Vector2( 10, 20 ), inner.BottomLeft );
		Assert.AreEqual( new Vector2( 20, 20 ), inner.BottomRight );
	}

	/// <summary>
	/// A border wider than the radius makes the padding corner square on both axes.
	/// </summary>
	[TestMethod]
	public void InnerGoesSquareWhenBorderExceedsRadius()
	{
		var r = Radius( "5px" ).GetBorderRadii( new Rect( 0, 0, 100, 100 ) );
		var inner = r.Inner( new Vector4( 10, 0, 0, 0 ) );

		Assert.AreEqual( Vector2.Zero, inner.TopLeft );
		Assert.AreEqual( Vector2.Zero, inner.BottomLeft );
		Assert.AreEqual( new Vector2( 5, 5 ), inner.TopRight );
	}

	[TestMethod]
	public void GrowFollowsCssSpreadRule()
	{
		var r = Radius( "10px", "0", "2px", "10px" ).GetBorderRadii( new Rect( 0, 0, 100, 100 ) );
		var grown = r.Grow( 5 );

		Assert.AreEqual( 15f, grown.TopLeft.x, 0.001f );
		Assert.AreEqual( 0f, grown.TopRight.x, 0.001f );
		Assert.AreEqual( 5.92f, grown.BottomLeft.x, 0.001f );

		var shrunk = r.Grow( -15 );
		Assert.AreEqual( 0f, shrunk.TopLeft.x, 0.001f );
	}

	[TestMethod]
	public void PackingOrders()
	{
		var r = Radius( "1px", "2px", "3px", "4px" ).GetBorderRadii( new Rect( 0, 0, 100, 100 ) );

		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), r.ToVector4() );
		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), r.Horizontal );
		Assert.AreEqual( new Vector4( 1, 2, 3, 4 ), r.Vertical );
		Assert.AreEqual( new Vector4( 4, 2, 3, 1 ), r.ToPublic() );

		var back = BorderRadii.FromPublic( r.ToPublic() );
		Assert.AreEqual( r.TopLeft, back.TopLeft );
		Assert.AreEqual( r.BottomRight, back.BottomRight );
	}

	/// <summary>
	/// The elliptical case: horizontal and vertical come out separately.
	/// </summary>
	[TestMethod]
	public void HorizontalAndVerticalSplit()
	{
		var r = Radius( "20%" ).GetBorderRadii( new Rect( 0, 0, 200, 100 ) );

		Assert.AreEqual( new Vector4( 40, 40, 40, 40 ), r.Horizontal );
		Assert.AreEqual( new Vector4( 20, 20, 20, 20 ), r.Vertical );
	}

	/// <summary>
	/// The CSS slash syntax: horizontal radii, then vertical ones, each side repeating like margin.
	/// </summary>
	[TestMethod]
	public void SlashSyntaxSetsVerticalRadii()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-radius", "10px 20px / 5px" ) );

		var r = style.GetBorderRadii( new Rect( 0, 0, 200, 200 ) );
		Assert.AreEqual( new Vector2( 10, 5 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 20, 5 ), r.TopRight );
		Assert.AreEqual( new Vector2( 10, 5 ), r.BottomRight );
		Assert.AreEqual( new Vector2( 20, 5 ), r.BottomLeft );

		// Setting the shorthand again without a slash makes the corners circular again
		Assert.IsTrue( style.Set( "border-radius", "8px" ) );
		r = style.GetBorderRadii( new Rect( 0, 0, 200, 200 ) );
		Assert.AreEqual( new Vector2( 8, 8 ), r.TopLeft );
	}

	[TestMethod]
	public void LonghandTakesTwoValues()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-top-left-radius", "40px 20px" ) );
		Assert.IsTrue( style.Set( "border-bottom-right-radius", "6px" ) );

		var r = style.GetBorderRadii( new Rect( 0, 0, 200, 200 ) );
		Assert.AreEqual( new Vector2( 40, 20 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 6, 6 ), r.BottomRight );

		Assert.IsFalse( style.Set( "border-top-left-radius", "40px 20px 5px" ) );
	}

	[TestMethod]
	public void SlashPercentagesResolvePerAxis()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-radius", "50% / 10%" ) );

		var r = style.GetBorderRadii( new Rect( 0, 0, 200, 100 ) );
		Assert.AreEqual( new Vector2( 100, 10 ), r.TopLeft );
	}

	[TestMethod]
	public void ScaleAppliesToBothAxes()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-radius", "8px" ) );

		style.ApplyScale( 2.0f );

		var r = style.GetBorderRadii( new Rect( 0, 0, 200, 200 ) );

		Assert.AreEqual( new Vector2( 16, 16 ), r.TopLeft );
		Assert.AreEqual( new Vector2( 16, 16 ), r.TopRight );
		Assert.AreEqual( new Vector2( 16, 16 ), r.BottomLeft );
		Assert.AreEqual( new Vector2( 16, 16 ), r.BottomRight );
	}

	[TestMethod]
	public void ScaleKeepsEllipseAspect()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-radius", "20px / 10px" ) );

		style.ApplyScale( 1.5f );

		var r = style.GetBorderRadii( new Rect( 0, 0, 400, 400 ) );
		Assert.AreEqual( new Vector2( 30, 15 ), r.TopLeft );
	}

	[TestMethod]
	public void ScaleLeavesPercentagesAlone()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-radius", "50%" ) );

		style.ApplyScale( 2.0f );

		var r = style.GetBorderRadii( new Rect( 0, 0, 100, 100 ) );
		Assert.AreEqual( new Vector2( 50, 50 ), r.TopLeft );
	}
}
