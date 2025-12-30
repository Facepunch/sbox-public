namespace MathTest;

[TestClass]
public partial class BBoxTest
{
	[TestMethod]
	public void ContainsPoint()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		// Test points inside
		Assert.IsTrue( bbox.Contains( new Vector3( 0, 0, 0 ) ), "Center point should be inside" );
		Assert.IsTrue( bbox.Contains( new Vector3( 9.9f, 9.9f, 9.9f ) ), "Point near max corner should be inside" );
		Assert.IsTrue( bbox.Contains( new Vector3( -9.9f, -9.9f, -9.9f ) ), "Point near min corner should be inside" );

		// Test points outside
		Assert.IsFalse( bbox.Contains( new Vector3( 11, 0, 0 ) ), "Point outside x max should not be inside" );
		Assert.IsFalse( bbox.Contains( new Vector3( -11, 0, 0 ) ), "Point outside x min should not be inside" );
		Assert.IsFalse( bbox.Contains( new Vector3( 10.1f, 10.1f, 10.1f ) ), "Point just beyond max corner should not be inside" );
	}

	[TestMethod]
	public void ContainsBBox()
	{
		var outerBox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );
		var innerBox = new BBox( new Vector3( -5, -5, -5 ), new Vector3( 5, 5, 5 ) );
		var overlappingBox = new BBox( new Vector3( -15, -15, -15 ), new Vector3( 0, 0, 0 ) );
		var separateBox = new BBox( new Vector3( 20, 20, 20 ), new Vector3( 30, 30, 30 ) );

		Assert.IsTrue( outerBox.Contains( innerBox ), "Outer box should contain inner box" );
		Assert.IsFalse( outerBox.Contains( overlappingBox ), "Outer box should not contain overlapping box" );
		Assert.IsFalse( outerBox.Contains( separateBox ), "Outer box should not contain separate box" );
		Assert.IsFalse( innerBox.Contains( outerBox ), "Inner box should not contain outer box" );
	}

	[TestMethod]
	public void OverlapsBBox()
	{
		var boxA = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );
		var boxB = new BBox( new Vector3( 5, 5, 5 ), new Vector3( 15, 15, 15 ) );
		var boxC = new BBox( new Vector3( 20, 20, 20 ), new Vector3( 30, 30, 30 ) );

		Assert.IsTrue( boxA.Overlaps( boxB ), "Boxes A and B should overlap" );
		Assert.IsFalse( boxA.Overlaps( boxC ), "Boxes A and C should not overlap" );
		Assert.IsTrue( boxB.Overlaps( boxA ), "Overlap should be commutative" );
	}

	[TestMethod]
	public void AddPointTest()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		// Add a point outside the box
		var expandedBox = bbox.AddPoint( new Vector3( 20, 5, 15 ) );

		// Check that the box expanded in the right dimensions
		Assert.AreEqual( new Vector3( -10, -10, -10 ), expandedBox.Mins );
		Assert.AreEqual( new Vector3( 20, 10, 15 ), expandedBox.Maxs );
	}

	[TestMethod]
	public void GrowTest()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		var grownBox = bbox.Grow( 5 );

		// Check that the box grew in all dimensions
		Assert.AreEqual( new Vector3( -15, -15, -15 ), grownBox.Mins );
		Assert.AreEqual( new Vector3( 15, 15, 15 ), grownBox.Maxs );
	}

	[TestMethod]
	public void ClosestPointTest()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		// Test points inside the box
		var insidePoint = new Vector3( 5, 5, 5 );
		var closestToInside = bbox.ClosestPoint( insidePoint );
		Assert.IsTrue( closestToInside.AlmostEqual( insidePoint ), "Closest point to an inside point should be the point itself" );

		// Test points outside the box
		var outsidePoint = new Vector3( 20, 5, 5 );
		var closestToOutside = bbox.ClosestPoint( outsidePoint );
		Assert.IsTrue( closestToOutside.AlmostEqual( new Vector3( 10, 5, 5 ) ), "Closest point should be on the box surface" );

		// Test point outside on multiple axes
		var cornerPoint = new Vector3( 20, 20, 20 );
		var closestToCorner = bbox.ClosestPoint( cornerPoint );
		Assert.IsTrue( closestToCorner.AlmostEqual( new Vector3( 10, 10, 10 ) ), "Closest point should be the box corner" );
	}

	[TestMethod]
	public void FromPositionAndSizeTest()
	{
		var bbox1 = BBox.FromPositionAndSize( new Vector3( 10, 20, 30 ), 10.0f );
		Assert.AreEqual( new Vector3( 5, 15, 25 ), bbox1.Mins );
		Assert.AreEqual( new Vector3( 15, 25, 35 ), bbox1.Maxs );

		var bbox2 = BBox.FromPositionAndSize( new Vector3( 10, 20, 30 ), new Vector3( 2, 4, 6 ) );
		Assert.AreEqual( new Vector3( 9, 18, 27 ), bbox2.Mins );
		Assert.AreEqual( new Vector3( 11, 22, 33 ), bbox2.Maxs );
	}

	[TestMethod]
	public void GetEdgeDistanceTest()
	{
		var bbox = new BBox( new Vector3( -10, -10, -10 ), new Vector3( 10, 10, 10 ) );

		// Test center point (should be 10 units from any edge)
		float centerDist = bbox.GetEdgeDistance( new Vector3( 0, 0, 0 ) );
		Assert.AreEqual( 10.0f, centerDist );

		// Test point near edge
		float nearEdgeDist = bbox.GetEdgeDistance( new Vector3( 9, 0, 0 ) );
		Assert.AreEqual( 1.0f, nearEdgeDist );

		// Test point outside bbox (should return distance to nearest edge, which is 0)
		float outsideDist = bbox.GetEdgeDistance( new Vector3( 20, 0, 0 ) );
		Assert.AreEqual( 10.0f, outsideDist );
	}
}
