using System;

namespace SystemTest;

[TestClass]
public class Curve2DTest
{
	[TestMethod]
	public void WithOnePoint()
	{
		var c = new Curve();
		c.AddPoint( 0.0f, 50.0f );

		Assert.AreEqual( 50.0f, c.Evaluate( -1.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 0.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 1.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 0.5f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 2.0f ) );
	}

	[TestMethod]
	public void WithTwoPoints()
	{
		var c = new Curve();
		c.AddPoint( 0.0f, 100.0f );
		c.AddPoint( 1.0f, 0.0f );

		Assert.AreEqual( 100.0f, c.Evaluate( -1.0f ) );

		Assert.AreEqual( 100.0f, c.Evaluate( 0.0f ) );
		Assert.AreEqual( 0.0f, c.Evaluate( 1.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 0.5f ) );

		Assert.AreEqual( 0.0f, c.Evaluate( 2.0f ) );
	}

	[TestMethod]
	public void WithThreePoints()
	{
		var c = new Curve();
		c.AddPoint( 0.0f, 100.0f );
		c.AddPoint( 1.0f, 0.0f );
		c.AddPoint( 2.0f, 100.0f );

		Assert.AreEqual( 100.0f, c.Evaluate( -1.0f ) );

		Assert.AreEqual( 100.0f, c.Evaluate( 0.0f ) );
		Assert.AreEqual( 0.0f, c.Evaluate( 1.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 0.5f ) );

		Assert.AreEqual( 100.0f, c.Evaluate( 2.0f ) );
	}

	[TestMethod]
	public void WithFourPoints()
	{
		var c = new Curve();
		c.AddPoint( 0.0f, 100.0f );
		c.AddPoint( 1.0f, 0.0f );
		c.AddPoint( 2.0f, 100.0f );
		c.AddPoint( 3.0f, 0.0f );

		Assert.AreEqual( 100.0f, c.Evaluate( -1.0f ) );

		Assert.AreEqual( 100.0f, c.Evaluate( 0.0f ) );
		Assert.AreEqual( 0.0f, c.Evaluate( 1.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 0.5f ) );

		Assert.AreEqual( 100.0f, c.Evaluate( 2.0f ) );
		Assert.AreEqual( 50.0f, c.Evaluate( 2.5f ) );
		Assert.AreEqual( 0.0f, c.Evaluate( 3.0f ) );
	}

	[TestMethod]
	public void JsonParseArray()
	{
		var c = new Curve();
		c.AddPoint( 0.0f, 1.0f );
		c.AddPoint( 1.0f, 0.5f );
		c.AddPoint( 2.0f, 1.0f );

		var json = System.Text.Json.JsonSerializer.Serialize( c, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault } );
		Console.WriteLine( json );

		{
			var k = System.Text.Json.JsonSerializer.Deserialize<Curve>( json );
			Assert.AreEqual( 0f, k.TimeRange.x );
			Assert.AreEqual( 1f, k.TimeRange.y );
			Assert.AreEqual( 0f, k.ValueRange.x );
			Assert.AreEqual( 1f, k.ValueRange.y );
			Assert.AreEqual( 3, k.Frames.Length );
		}

	}

	[TestMethod]
	public void JsonParseFull()
	{
		var c = new Curve();
		c.TimeRange = new Vector2( -10, 10 );
		c.ValueRange = new Vector2( -1, 100 );
		c.AddPoint( 0.0f, 1.0f );
		c.AddPoint( 1.0f, 0.5f );
		c.AddPoint( 2.0f, 1.0f );

		var json = System.Text.Json.JsonSerializer.Serialize( c, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault } );
		Console.WriteLine( json );

		{
			var k = System.Text.Json.JsonSerializer.Deserialize<Curve>( json );
			Assert.AreEqual( -10.0f, k.TimeRange.x );
			Assert.AreEqual( 10.0f, k.TimeRange.y );
			Assert.AreEqual( -1.0f, k.ValueRange.x );
			Assert.AreEqual( 100f, k.ValueRange.y );
			Assert.AreEqual( 3, k.Frames.Length );
		}

	}

}
