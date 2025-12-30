namespace MathTest;

[TestClass]
public class FloatSpanTest
{
	[TestMethod]
	public void Max()
	{
		var data = new float[] { 1.0f, 5.0f, 3.0f, 9.0f, 2.0f };
		var span = new FloatSpan( data );

		Assert.AreEqual( 9.0f, span.Max() );
	}

	[TestMethod]
	public void Max_Empty()
	{
		var data = new float[0];
		var span = new FloatSpan( data );

		Assert.AreEqual( 0.0f, span.Max() );
	}

	[TestMethod]
	public void Min()
	{
		var data = new float[] { 1.0f, 5.0f, 3.0f, 9.0f, 2.0f };
		var span = new FloatSpan( data );

		Assert.AreEqual( 1.0f, span.Min() );
	}

	[TestMethod]
	public void Min_Empty()
	{
		var data = new float[0];
		var span = new FloatSpan( data );

		Assert.AreEqual( 0.0f, span.Min() );
	}

	[TestMethod]
	public void Average()
	{
		var data = new float[] { 2.0f, 4.0f, 6.0f, 8.0f };
		var span = new FloatSpan( data );

		Assert.AreEqual( 5.0f, span.Average() );
	}

	[TestMethod]
	public void Average_Empty()
	{
		var data = new float[0];
		var span = new FloatSpan( data );

		Assert.AreEqual( 0.0f, span.Average() );
	}

	[TestMethod]
	public void Sum()
	{
		var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
		var span = new FloatSpan( data );

		Assert.AreEqual( 10.0f, span.Sum() );
	}

	[TestMethod]
	public void Sum_Empty()
	{
		var data = new float[0];
		var span = new FloatSpan( data );

		Assert.AreEqual( 0.0f, span.Sum() );
	}

	[TestMethod]
	public void Set_Value()
	{
		var data = new float[5];
		var span = new FloatSpan( data );

		span.Set( 7.5f );

		foreach ( var value in data )
		{
			Assert.AreEqual( 7.5f, value );
		}
	}

	[TestMethod]
	public void Set_Span()
	{
		var data = new float[4];
		var span = new FloatSpan( data );
		var values = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

		span.Set( values );

		CollectionAssert.AreEqual( values, data );
	}

	[TestMethod]
	public void CopyScaled()
	{
		var data = new float[4];
		var span = new FloatSpan( data );
		var values = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };

		span.CopyScaled( values, 2.0f );

		Assert.AreEqual( 2.0f, data[0] );
		Assert.AreEqual( 4.0f, data[1] );
		Assert.AreEqual( 6.0f, data[2] );
		Assert.AreEqual( 8.0f, data[3] );
	}

	[TestMethod]
	public void Add()
	{
		var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
		var span = new FloatSpan( data );
		var values = new float[] { 10.0f, 20.0f, 30.0f, 40.0f };

		span.Add( values );

		Assert.AreEqual( 11.0f, data[0] );
		Assert.AreEqual( 22.0f, data[1] );
		Assert.AreEqual( 33.0f, data[2] );
		Assert.AreEqual( 44.0f, data[3] );
	}

	[TestMethod]
	public void AddScaled()
	{
		var data = new float[] { 1.0f, 2.0f, 3.0f, 4.0f };
		var span = new FloatSpan( data );
		var values = new float[] { 10.0f, 20.0f, 30.0f, 40.0f };

		span.AddScaled( values, 0.5f );

		Assert.AreEqual( 6.0f, data[0] );
		Assert.AreEqual( 12.0f, data[1] );
		Assert.AreEqual( 18.0f, data[2] );
		Assert.AreEqual( 24.0f, data[3] );
	}

	[TestMethod]
	public void Scale()
	{
		var data = new float[] { 2.0f, 4.0f, 6.0f, 8.0f };
		var span = new FloatSpan( data );

		span.Scale( 0.5f );

		Assert.AreEqual( 1.0f, data[0] );
		Assert.AreEqual( 2.0f, data[1] );
		Assert.AreEqual( 3.0f, data[2] );
		Assert.AreEqual( 4.0f, data[3] );
	}
}
