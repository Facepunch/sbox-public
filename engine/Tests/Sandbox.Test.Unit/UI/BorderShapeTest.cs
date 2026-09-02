using Sandbox.UI;

namespace UITests;

/// <summary>
/// A border shape generalises border-radius, so it follows the same rules for the root panel
/// scale: pixel lengths scale, percentages stay put and resolve against the box later.
/// </summary>
[TestClass]
public class BorderShapeTest
{
	[TestMethod]
	public void ScaleAppliesToPolygonPixels()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon( 10px 20px, 30px 20px, 20px 40px )" ) );

		style.ApplyScale( 2.0f );

		var points = style.BorderShape.Points;
		Assert.AreEqual( 20, points[0].X.GetPixels( 0 ) );
		Assert.AreEqual( 40, points[0].Y.GetPixels( 0 ) );
		Assert.AreEqual( 60, points[1].X.GetPixels( 0 ) );
		Assert.AreEqual( 80, points[2].Y.GetPixels( 0 ) );
	}

	[TestMethod]
	public void ScaleLeavesPolygonPercentagesAlone()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "polygon( 50% 0%, 100% 100%, 0% 100% )" ) );

		style.ApplyScale( 2.0f );

		var points = style.BorderShape.Points;
		Assert.AreEqual( 50, points[0].X.GetPixels( 100 ) );
		Assert.AreEqual( 100, points[1].X.GetPixels( 100 ) );
	}

	[TestMethod]
	public void ScaleAppliesToCircleRadiusAndCentre()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "circle( 40px at 20px 30px )" ) );

		style.ApplyScale( 1.5f );

		var shape = style.BorderShape;
		Assert.AreEqual( 60, shape.CircleRadius.Value.GetPixels( 0 ) );
		Assert.AreEqual( 30, shape.CircleCenterX.GetPixels( 0 ) );
		Assert.AreEqual( 45, shape.CircleCenterY.GetPixels( 0 ) );
	}

	[TestMethod]
	public void ScaleLeavesNoneAlone()
	{
		var style = new Styles();
		Assert.IsTrue( style.Set( "border-shape", "none" ) );

		style.ApplyScale( 2.0f );

		Assert.IsTrue( style.BorderShape.IsNone );
	}
}
