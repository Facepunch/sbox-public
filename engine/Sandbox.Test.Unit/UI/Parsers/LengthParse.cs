using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class LengthParse
{
	[TestMethod]
	public void LengthParsePixels()
	{
		var l = Length.Parse( "123px" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 123, l.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, l.Value.Unit );

		l = Length.Parse( "123.00px" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 123, l.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, l.Value.Unit );

		l = Length.Parse( "123.00 px" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 123, l.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, l.Value.Unit );

		l = Length.Parse( " 123.00px" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 123, l.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, l.Value.Unit );

		l = Length.Parse( " 123.00  px  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 123, l.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, l.Value.Unit );
	}

	[TestMethod]
	public void LengthParsePercent()
	{
		var l = Length.Parse( "74%" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 74, l.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, l.Value.Unit );

		l = Length.Parse( " 74.0%" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 74, l.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, l.Value.Unit );

		l = Length.Parse( " 74%  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 74, l.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, l.Value.Unit );

		l = Length.Parse( " 74 %  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 74, l.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, l.Value.Unit );
	}

	[TestMethod]
	public void LengthParseRem()
	{
		var l = Length.Parse( "82rem" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 82, l.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, l.Value.Unit );

		l = Length.Parse( " 82.0rem" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 82, l.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, l.Value.Unit );

		l = Length.Parse( " 82rem  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 82, l.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, l.Value.Unit );

		l = Length.Parse( " 82 rem  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 82, l.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, l.Value.Unit );
	}

	[TestMethod]
	public void LengthParseEm()
	{
		var l = Length.Parse( "24em" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 24, l.Value.Value );
		Assert.AreEqual( LengthUnit.Em, l.Value.Unit );

		l = Length.Parse( " 24.0em" );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 24, l.Value.Value );
		Assert.AreEqual( LengthUnit.Em, l.Value.Unit );

		l = Length.Parse( " 24em  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 24, l.Value.Value );
		Assert.AreEqual( LengthUnit.Em, l.Value.Unit );

		l = Length.Parse( " 24 em  " );

		Assert.IsTrue( l.HasValue );
		Assert.AreEqual( 24, l.Value.Value );
		Assert.AreEqual( LengthUnit.Em, l.Value.Unit );
	}
}
