namespace SystemTest;

[TestClass]
public class ParseTest
{
	[TestMethod]
	public void ReadUntilOrEndRespectParens()
	{
		var p = new Parse( "cubic-bezier(0.1, 0.2, 0.3, 0.4), opacity" );
		Assert.AreEqual( "cubic-bezier(0.1, 0.2, 0.3, 0.4)", p.ReadUntilOrEnd( ",", true, false ) );

		p = new Parse( "outer(inner(a, b), c), next" );
		Assert.AreEqual( "outer(inner(a, b), c)", p.ReadUntilOrEnd( ",", true, false ) );

		p = new Parse( "[a, b, c], next" );
		Assert.AreEqual( "[a, b, c]", p.ReadUntilOrEnd( ",", true, false ) );

		p = new Parse( "{a, b, c}, next" );
		Assert.AreEqual( "{a, b, c}", p.ReadUntilOrEnd( ",", true, false ) );

		p = new Parse( ",rest" );
		Assert.AreEqual( string.Empty, p.ReadUntilOrEnd( ",", true, true ) );

		p = new Parse( "no commas here at all" );
		Assert.AreEqual( "no commas here at all", p.ReadUntilOrEnd( ",", true, false ) );

		p = new Parse( "cubic-bezier(0.1, 0.2)" );
		Assert.AreEqual( "cubic-bezier(0.1", p.ReadUntilOrEnd( ",", false ) );
	}

	[TestMethod]
	public void ReadWordRespectParens()
	{
		var p = new Parse( "cubic-bezier( 0.1, 0.2, 0.3, 0.4 ) trailing" );
		Assert.AreEqual( "cubic-bezier( 0.1, 0.2, 0.3, 0.4 )", p.ReadWord( null, true, true ) );

		p = new Parse( "steps(4, end) ease" );
		Assert.AreEqual( "steps(4, end)", p.ReadWord( null, true, true ) );

		p = new Parse( "ease-out next" );
		Assert.AreEqual( "ease-out", p.ReadWord( null, true, true ) );

		p = new Parse( "ease-out next" );
		Assert.AreEqual( "ease-out", p.ReadWord() );
	}
}
