namespace SystemTest;

[TestClass]
public class TriangleTest
{
	[TestMethod]
	public void TestTrianglePerimeter()
	{
		Triangle triangle = new Triangle( new Vector3( 0, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 1, 0, 0 ) );

		Assert.AreEqual( 3.4142137f, triangle.Perimeter );
	}

	[TestMethod]
	public void TestTriangleArea()
	{
		Triangle triangle = new Triangle( new Vector3( 0, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 1, 0, 0 ) );

		Assert.AreEqual( triangle.Area, 0.5f );
	}

	[TestMethod]
	public void TestTriangleIsRight()
	{
		Triangle triangle = new Triangle( new Vector3( 0, 0, 0 ), new Vector3( 0, 3, 0 ), new Vector3( 4, 0, 0 ) );

		Assert.IsTrue( triangle.IsRight );
	}


	[TestMethod]
	public void TestClosestPointWithPointOutsideTriangle()
	{
		Triangle triangle = new Triangle( new Vector3( -1, 0, 0 ), new Vector3( 0, 2, 0 ), new Vector3( 1, 0, 0 ) );
		Vector3 closestPoint = triangle.ClosestPoint( new Vector3( -1000, 0, 0 ) );

		Assert.AreEqual( closestPoint, new Vector3( -1, 0, 0 ) );
	}

	[TestMethod]
	public void TestClosestPointWithPointInsideTriangle()
	{
		Triangle triangle = new Triangle( new Vector3( -100, 0, 0 ), new Vector3( 0, 100, 0 ), new Vector3( 100, 0, 0 ) );
		Vector3 closestPoint = triangle.ClosestPoint( new Vector3( 10, 15, 0 ) );

		Assert.IsTrue( closestPoint == new Vector3( 10, 15, 0 ) );
	}

}
