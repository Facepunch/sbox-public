using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class StripComments
{
	[TestMethod]
	public void SingleLine()
	{
		var str = StyleParser.StripComments( "One\nTwo// hello\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );
	}

	[TestMethod]
	public void CommentsInsideComments()
	{
		var str = StyleParser.StripComments( "One\nTwo// hello // more // bullshit /*\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );

		str = StyleParser.StripComments( "One\nTwo/* hello /* more // bullshit */\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );
	}

	[TestMethod]
	public void Multiline()
	{
		var str = StyleParser.StripComments( "One\nTwo /* hello\n   Hello*/Three" );
		Assert.AreEqual( "One\nTwo Three", str );
	}

	[TestMethod]
	public void DontThinkUrlsAreComments()
	{
		var str = "bloop\nbackground-image: url( https://hello );\nfloop";

		var stripped = StyleParser.StripComments( str );
		Assert.AreEqual( str, stripped );
	}
}
