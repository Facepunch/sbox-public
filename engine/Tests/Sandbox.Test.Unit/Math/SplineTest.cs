namespace MathTests;

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

			// Loops are emulated with an extra point duplicating the first one;
			// that point must stay hidden from the public point count.
			Assert.AreEqual( 3, spline.PointCount );

			// And turning the loop off again should not leak it either
			spline.IsLoop = false;
			Assert.IsFalse( spline.IsLoop );
			Assert.AreEqual( 3, spline.PointCount );
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

	/// <summary>
	/// Sampling a spline every frame (roads, rails, paths) shouldn't allocate.
	/// </summary>
	[TestMethod]
	public void SamplingDoesNotAllocate()
	{
		var spline = new Spline();
		spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
		spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );
		spline.InsertPoint( 2, new Spline.Point { Position = new Vector3( 10, 10, 0 ) } );
		spline.SampleAtDistance( 1 );

		var before = System.GC.GetAllocatedBytesForCurrentThread();
		var total = 0f;
		for ( int i = 0; i < 100; i++ )
		{
			total += spline.SampleAtDistance( i * 0.2f ).Position.x;
			total += spline.PointCount + spline.SegmentCount + (spline.IsLoop ? 1 : 0);
		}

		Assert.AreEqual( 0L, System.GC.GetAllocatedBytesForCurrentThread() - before );
		Assert.IsTrue( total > 0 );
	}

	/// <summary>
	/// Deserialization replaces the point list; queries must read the new one, not a view of the old.
	/// </summary>
	[TestMethod]
	public void QueriesFollowAReplacedPointList()
	{
		var spline = new Spline();
		spline.InsertPoint( 0, new Spline.Point { Position = new Vector3( 0, 0, 0 ) } );
		spline.InsertPoint( 1, new Spline.Point { Position = new Vector3( 10, 0, 0 ) } );
		Assert.AreEqual( new Vector3( 5, 0, 0 ), spline.SampleAtDistance( 5 ).Position );

		var replaced = new System.Collections.Generic.List<Spline.Point>
		{
			new() { Position = new Vector3( 0, 0, 0 ) },
			new() { Position = new Vector3( 0, 20, 0 ) },
			new() { Position = new Vector3( 0, 40, 0 ) },
		};
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		typeof( Spline ).GetField( "_points", flags ).SetValue( spline, replaced );
		typeof( Spline ).GetField( "_areDistancesSampled", flags ).SetValue( spline, false );

		Assert.AreEqual( 3, spline.PointCount );
		Assert.AreEqual( 2, spline.SegmentCount );
		var sample = spline.SampleAtDistance( 25 ).Position;
		Assert.AreEqual( 0f, sample.x, 0.01f );
		Assert.AreEqual( 25f, sample.y, 0.1f );
	}
}
