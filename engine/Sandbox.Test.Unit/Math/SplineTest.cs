namespace MathTest;

[TestClass]
public class SplineTest
{
	[TestMethod]
	public void AddAndRetrievePoints()
	{
		{
			// Create a new spline
			Spline spline = new Spline();

			// Add points to the spline
			spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
			spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );
			spline.InsertPoint( 2, new Spline.Point { Position = new Vector3( 20, 0, 0 ) } );

			// Check the number of points
			Assert.AreEqual( 3, spline.PointCount );

			// Retrieve and check the positions
			Assert.AreEqual( new Vector3( 0, 0, 0 ), spline.GetPoint( 0 ).Position );
			Assert.AreEqual( new Vector3( 10, 0, 0 ), spline.GetPoint( 1 ).Position );
			Assert.AreEqual( new Vector3( 20, 0, 0 ), spline.GetPoint( 2 ).Position );
		}
	}

	[TestMethod]
	public void GetPositionAtDistance()
	{
		{
			// Create a spline with two points
			Spline spline = new Spline();

			spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
			spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );

			// Get positions at various distances
			Vector3 start = spline.SampleAtDistance( 0 ).Position;
			Vector3 middle = spline.SampleAtDistance( 5 ).Position;
			Vector3 end = spline.SampleAtDistance( 10 ).Position;

			Assert.AreEqual( new Vector3( 0, 0, 0 ), start );
			Assert.AreEqual( new Vector3( 5, 0, 0 ), middle );
			Assert.AreEqual( new Vector3( 10, 0, 0 ), end );
		}
	}

	[TestMethod]
	public void GetLength()
	{
		{
			// Create a spline
			Spline spline = new Spline();

			spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
			spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );

			float length = spline.Length;

			Assert.AreEqual( 10.0f, length, 0.001f );
		}
	}

	[TestMethod]
	public void IsLoop()
	{
		{
			// Create a spline and set it to loop
			Spline spline = new Spline();

			spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
			spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );
			spline.InsertPoint( 2, new Spline.Point { Position = new Vector3( 10, 10, 0 ) } );

			spline.IsLoop = true;

			// Check that the spline is looped
			Assert.IsTrue( spline.IsLoop );
		}
	}

	[TestMethod]
	public void FindDistanceClosestToPosition()
	{
		{
			// Create a spline
			Spline spline = new Spline();

			spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
			spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );

			// Find the distance closest to a point
			var sample = spline.SampleAtClosestPosition( new Vector3( 5, 5, 0 ) );

			// The closest distance should be approximately 5.0
			Assert.AreEqual( 5.0f, sample.Distance, 0.001f );
			Assert.AreEqual( new Vector3( 5, 0, 0 ), sample.Position );
		}
	}
}
