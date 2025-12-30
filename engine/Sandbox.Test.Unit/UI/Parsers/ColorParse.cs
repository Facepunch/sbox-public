using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class ColorParse
{
	[TestMethod]
	public void ParseHex3()
	{
		var c = Color.Parse( "#f0f" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( " #f0f " );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "#F0F" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void ParseHex4()
	{
		var c = Color.Parse( "#f0f0" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 0, c.Value.a );
	}

	[TestMethod]
	public void ParseHex6()
	{
		var c = Color.Parse( "#ff00ff" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void ParseHex8()
	{
		var c = Color.Parse( "#ff00FF00" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 0, c.Value.a );
	}

	[TestMethod]
	public void ParseName()
	{
		var c = Color.Parse( "red" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "black" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "white" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 1, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "transparent" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0, c.Value.a );
	}

	[TestMethod]
	public void ParseRGBA()
	{
		var c = Color.Parse( "rgba( 255, 0, 0, 1 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "rgba( 0, 255, 255, 0.5 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0, c.Value.r );
		Assert.AreEqual( 1, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 0.5, c.Value.a );

		c = Color.Parse( "rgba( 0, 255, 255, 0.5" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgba( )" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgba( 2, )" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgba(" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgba( red, 1 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "rgba( red, 0.5 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 0.5, c.Value.a );

		c = Color.Parse( "rgba( #f00, 1 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "rgba( #ff0000, 1 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void ParseRGB()
	{
		var c = Color.Parse( "rgb( 255, 0, 0 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1, c.Value.r );
		Assert.AreEqual( 0, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "rgb( 0, 255, 255 )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0, c.Value.r );
		Assert.AreEqual( 1, c.Value.g );
		Assert.AreEqual( 1, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "rgb(,,,,,,)" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgb( 255, 255, 0" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgb( 2 )" );
		Assert.IsFalse( c.HasValue );

		c = Color.Parse( "rgb(" );
		Assert.IsFalse( c.HasValue );
	}

	[TestMethod]
	public void ParseFGD()
	{
		var c = Color.Parse( "128 64 255 32" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.5f, c.Value.r, 0.02f );
		Assert.AreEqual( 0.25f, c.Value.g, 0.02f );
		Assert.AreEqual( 1f, c.Value.b, 0.02f );
		Assert.AreEqual( 0.125f, c.Value.a, 0.02f );
	}

	[TestMethod]
	public void Darken()
	{
		var c = Color.Parse( "darken( #fff, 50% )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.5f, c.Value.r );
		Assert.AreEqual( 0.5f, c.Value.g );
		Assert.AreEqual( 0.5f, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void Lighten()
	{
		var c = Color.Parse( "lighten( #aaa, 50% )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1.0f, c.Value.r );
		Assert.AreEqual( 1.0f, c.Value.g );
		Assert.AreEqual( 1.0f, c.Value.b );
		Assert.AreEqual( 1.0f, c.Value.a );
	}

	[TestMethod]
	public void Invert()
	{
		var c = Color.Parse( "invert( #f0f )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.0f, c.Value.r );
		Assert.AreEqual( 1.0f, c.Value.g );
		Assert.AreEqual( 0.0f, c.Value.b );
		Assert.AreEqual( 1.0f, c.Value.a );
	}

	[TestMethod]
	public void Mix()
	{
		var c = Color.Parse( "mix( #000, #fff, 50% )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.5f, c.Value.r );
		Assert.AreEqual( 0.5f, c.Value.g );
		Assert.AreEqual( 0.5f, c.Value.b );
		Assert.AreEqual( 1.0f, c.Value.a );
	}

	[TestMethod]
	public void DarkenNoSpace()
	{
		var c = Color.Parse( "darken(#fff, 50%)" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.5f, c.Value.r );
		Assert.AreEqual( 0.5f, c.Value.g );
		Assert.AreEqual( 0.5f, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void MixThenDarken()
	{
		var c = Color.Parse( "darken( mix( #000, #fff, 50% ), 50% )" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 0.25f, c.Value.r );
		Assert.AreEqual( 0.25f, c.Value.g );
		Assert.AreEqual( 0.25f, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );
	}

	[TestMethod]
	public void ParseRaw()
	{
		var c = Color.Parse( "1.0, 2.0, 0, 1.0" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 1.0f, c.Value.r );
		Assert.AreEqual( 2.0f, c.Value.g );
		Assert.AreEqual( 0, c.Value.b );
		Assert.AreEqual( 1, c.Value.a );

		c = Color.Parse( "10.0f, 20.0, 2000, 0.4f" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 10, c.Value.r );
		Assert.AreEqual( 20, c.Value.g );
		Assert.AreEqual( 2000, c.Value.b );
		Assert.AreEqual( 0.4f, c.Value.a );

		c = Color.Parse( "10f, 20f, 2000f, 0.4f" );

		Assert.IsTrue( c.HasValue );
		Assert.AreEqual( 10, c.Value.r );
		Assert.AreEqual( 20, c.Value.g );
		Assert.AreEqual( 2000, c.Value.b );
		Assert.AreEqual( 0.4f, c.Value.a );
	}

}
