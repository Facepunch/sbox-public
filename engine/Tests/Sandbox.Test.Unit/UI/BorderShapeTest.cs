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
	public void InlinePointsOwnTheirDataAndExposeOnlyActiveSlots()
	{
		BorderShapePoint[] source = [new( 10, 20 ), new( 30, 20 ), new( 20, 40 )];
		var shape = new BorderShape( source );
		var copy = new BorderShape( source );
		source[0] = new( 999, 999 );
		Assert.AreEqual( new BorderShapePoint( 10, 20 ), shape.Points[0] );
		Assert.IsTrue( shape.Equals( copy ) );
		Assert.AreEqual( shape.GetHashCode(), copy.GetHashCode() );
		Assert.AreEqual( 3, shape.Points.Count );
		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => { _ = shape.Points[-1]; } );
		Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => { _ = shape.Points[3]; } );
		Assert.AreEqual( 0, BorderShape.None.Points.Count );
		Assert.AreEqual( 0, new BorderShape( null, 0, 0 ).Points.Count );
		Assert.AreEqual( 0, default( BorderShape.PointCollection ).Count );
		System.Collections.Generic.IReadOnlyList<BorderShapePoint> view = shape.Points;
		CollectionAssert.AreEqual( copy.Points.ToArray(), view.ToArray() );
	}

	[TestMethod]
	public void PolygonCapacityAndScalingPreserveTheOriginal()
	{
		var source = Enumerable.Range( 0, BorderShape.MaxPoints ).Select( i => new BorderShapePoint( i + 1, Length.Percent( i * 10 ).Value ) ).ToArray();
		var shape = new BorderShape( source );
		var scaled = shape.Scale( 2 );
		Assert.AreEqual( 8, scaled.Points.Count );
		for ( int i = 0; i < source.Length; i++ )
		{
			Assert.AreEqual( source[i], shape.Points[i] );
			Assert.AreEqual( source[i].X.Value * 2, scaled.Points[i].X.Value );
			Assert.AreEqual( source[i].Y, scaled.Points[i].Y );
		}
		Assert.AreSame( shape, shape.Scale( 1 ) );
		foreach ( int count in new[] { 0, 1, 2, 9 } )
			Assert.ThrowsException<System.ArgumentOutOfRangeException>( () => new BorderShape( new BorderShapePoint[count] ) );
	}

	[TestMethod]
	public void PointEnumerationVisitsActiveSlots()
	{
		var shape = new BorderShape( [new( 10, 20 ), new( 30, 20 ), new( 20, 40 )] );
		int index = 0;
		foreach ( var point in shape.Points )
		{
			Assert.AreEqual( shape.Points[index], point );
			index++;
		}
		Assert.AreEqual( shape.Points.Count, index );
	}

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
