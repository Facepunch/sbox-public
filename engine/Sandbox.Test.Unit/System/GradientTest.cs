using System;

namespace SystemTest;

[TestClass]
public class GradientTest
{
	[TestMethod]
	public void WithOnePoint()
	{
		var c = new Gradient();
		c.AddColor( 0.0f, Color.Red );

		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.5f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 2.0f ) );
	}

	[TestMethod]
	public void WithTwoPoints()
	{
		var c = new Gradient();
		c.AddColor( 0.0f, Color.Red );
		c.AddColor( 1.0f, Color.Blue );

		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 1.0f ) );

		// Blow behind
		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100000.0f ) );

		// blow in front
		Assert.AreEqual( Color.Blue, c.Evaluate( 10.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100000.0f ) );

		// half way
		Assert.AreEqual( Color.Lerp( Color.Red, Color.Blue, 0.5f ), c.Evaluate( 0.5f ) );
	}

	[TestMethod]
	public void WithTwoPoints_Alpha()
	{
		Gradient c = Color.Red;
		c.AddAlpha( 0.0f, 0.0f );
		c.AddAlpha( 1.0f, 1.0f );

		Assert.AreEqual( Color.Red.WithAlpha( 0 ), c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Red.WithAlpha( 1 ), c.Evaluate( 1.0f ) );

		// Blow behind
		Assert.AreEqual( Color.Red.WithAlpha( 0 ), c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red.WithAlpha( 0 ), c.Evaluate( -100.0f ) );
		Assert.AreEqual( Color.Red.WithAlpha( 0 ), c.Evaluate( -100000.0f ) );

		// blow in front
		Assert.AreEqual( Color.Red.WithAlpha( 1 ), c.Evaluate( 10.0f ) );
		Assert.AreEqual( Color.Red.WithAlpha( 1 ), c.Evaluate( 100.0f ) );
		Assert.AreEqual( Color.Red.WithAlpha( 1 ), c.Evaluate( 100000.0f ) );

		// half way
		Assert.AreEqual( Color.Red.WithAlpha( 0.5f ), c.Evaluate( 0.5f ) );
	}

	[TestMethod]
	public void WithTwoPoints_WrongOrderInsert()
	{
		var c = new Gradient();
		c.AddColor( 1.0f, Color.Blue );
		c.AddColor( 0.0f, Color.Red );

		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 1.0f ) );

		// Blow behind
		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100000.0f ) );

		// blow in front
		Assert.AreEqual( Color.Blue, c.Evaluate( 10.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100000.0f ) );

		// half way
		Assert.AreEqual( Color.Lerp( Color.Red, Color.Blue, 0.5f ), c.Evaluate( 0.5f ) );
	}

	[TestMethod]
	public void WithTwoPoints_WrongOrderInsert_AddPoint2()
	{
		var c = new Gradient();
		c.AddColor( new Gradient.ColorFrame( 1.0f, Color.Blue ) );
		c.AddColor( new Gradient.ColorFrame( 0.0f, Color.Red ) );

		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 1.0f ) );

		// Blow behind
		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100000.0f ) );

		// blow in front
		Assert.AreEqual( Color.Blue, c.Evaluate( 10.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100000.0f ) );

		// half way
		Assert.AreEqual( Color.Lerp( Color.Red, Color.Blue, 0.5f ), c.Evaluate( 0.5f ) );
	}

	[TestMethod]
	public void WithTwoPoints_Stepped()
	{
		var c = new Gradient();
		c.AddColor( 0.0f, Color.Red );
		c.AddColor( 1.0f, Color.Blue );
		c.Blending = Gradient.BlendMode.Stepped;

		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 1.0f ) );

		// Blow behind
		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100.0f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( -100000.0f ) );

		// blow in front
		Assert.AreEqual( Color.Blue, c.Evaluate( 10.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100.0f ) );
		Assert.AreEqual( Color.Blue, c.Evaluate( 100000.0f ) );

		// half way
		Assert.AreEqual( Color.Red, c.Evaluate( 0.001f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.2f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.5f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.75f ) );
		Assert.AreEqual( Color.Red, c.Evaluate( 0.99f ) );
	}

	[TestMethod]
	public void WithThreePoints()
	{
		var c = new Gradient();
		c.AddColor( 0.0f, Color.Red );
		c.AddColor( 1.0f, Color.Green );
		c.AddColor( 2.0f, Color.Blue );

		Assert.AreEqual( Color.Red, c.Evaluate( -1.0f ) );

		Assert.AreEqual( Color.Red, c.Evaluate( 0.0f ) );
		Assert.AreEqual( Color.Green, c.Evaluate( 1.0f ) );
		Assert.AreEqual( Color.Lerp( Color.Red, Color.Green, 0.5f ), c.Evaluate( 0.5f ) );

		Assert.AreEqual( Color.Blue, c.Evaluate( 2.0f ) );
	}

	[TestMethod]
	public void JsonParse()
	{
		var c = new Gradient();
		c.Blending = Gradient.BlendMode.Stepped;
		c.AddColor( 0.0f, Color.Red );
		c.AddColor( 1.0f, Color.Green );
		c.AddColor( 2.0f, Color.Blue );

		var json = System.Text.Json.JsonSerializer.Serialize( c, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault } );
		Console.WriteLine( json );

		{
			var k = System.Text.Json.JsonSerializer.Deserialize<Gradient>( json );
			Assert.AreEqual( Gradient.BlendMode.Stepped, k.Blending );
			Assert.AreEqual( 3, k.Colors.Count );
			Assert.AreEqual( Color.Red, k.Colors[0].Value );
			Assert.AreEqual( Color.Green, k.Colors[1].Value );
			Assert.AreEqual( 1.0f, k.Colors[1].Time );
			Assert.AreEqual( Color.Blue, k.Colors[2].Value );
		}

	}

}
