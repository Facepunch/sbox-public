using Sandbox.Html;

namespace SystemTests;

/// <summary>
/// The same cases as sbox.web's BBCodeTest - the html differs where the menu's HtmlPanel wants it to
/// </summary>
[TestClass]
public class BBCodeParserTest
{
	[TestMethod]
	public void RendersWorkshopDescription()
	{
		// A real s&box workshop description
		var html = BBCodeParser.ToHtml( """
			[h1]Big City[/h1]
			The original gm_bigcity, ported to s&box.

			[h2]Credits[/h2]
			[list]
			  [*]Based on the original gm_bigcity by Bigwig
			  [*]Assets by Valve Corporation
			[/list]

			[h2]Links[/h2]
			💬 [url=https://discord.gg/rbCJdZjewf]Discord[/url]
			🐙 [url=https://github.com/Softsplit/softsplit.big_city]GitHub[/url]
			""" );

		StringAssert.Contains( html, "<h1>Big City</h1>The original gm_bigcity, ported to s&amp;box.<br>" );
		StringAssert.Contains( html, "<ul><li>Based on the original gm_bigcity by Bigwig</li><li>Assets by Valve Corporation</li></ul>" );
		StringAssert.Contains( html, "<a href=\"https://discord.gg/rbCJdZjewf\">Discord</a>" );
		Assert.IsFalse( html.Contains( '[' ), html );
	}

	[TestMethod]
	public void EncodesTextAndRejectsUnsafeLinks()
	{
		var html = BBCodeParser.ToHtml( "<script>alert(1)</script> [url=javascript:alert(1)]click[/url] [img]javascript:x[/img]" );

		Assert.IsFalse( html.Contains( "<script>" ), html );
		Assert.IsFalse( html.Contains( "javascript:alert" ) && html.Contains( "href" ), html );
		StringAssert.Contains( html, "&lt;script&gt;" );
		StringAssert.Contains( html, "click" );
	}

	[TestMethod]
	public void QuotesAttributesCantBreakOut()
	{
		var html = BBCodeParser.ToHtml( "[url=https://example.com/\"onmouseover=\"x]a[/url]" );

		Assert.IsFalse( html.Contains( "\"onmouseover" ), html );
	}

	[TestMethod]
	public void BareUrlUsesItselfAsLabel()
	{
		var html = BBCodeParser.ToHtml( "[url]https://sbox.game/[/url]" );

		Assert.AreEqual( "<a href=\"https://sbox.game/\">https://sbox.game/</a>", html );
	}

	[TestMethod]
	public void UnknownAndUnbalancedTagsStayLiteral()
	{
		Assert.AreEqual( "[color=red]hi[/color]", BBCodeParser.ToHtml( "[color=red]hi[/color]" ) );
		Assert.AreEqual( "a[/b]", BBCodeParser.ToHtml( "a[/b]" ) );
	}

	[TestMethod]
	public void UnclosedTagsAreClosed()
	{
		Assert.AreEqual( "<strong><em>x</em></strong>", BBCodeParser.ToHtml( "[b][i]x" ) );
	}

	[TestMethod]
	public void NoParseAndCodeKeepTheirContents()
	{
		Assert.AreEqual( "[b]not bold[/b]", BBCodeParser.ToHtml( "[noparse][b]not bold[/b][/noparse]" ) );
		Assert.AreEqual( "<pre>var x = a &lt; b;</pre>", BBCodeParser.ToHtml( "[code]\nvar x = a < b;\n[/code]" ) );
	}

	[TestMethod]
	public void QuoteWithAuthor()
	{
		var html = BBCodeParser.ToHtml( "[quote=Garry]hello[/quote]" );

		Assert.AreEqual( "<blockquote><div class=\"bb-quote-author\">Garry</div>hello</blockquote>", html );
	}

	[TestMethod]
	public void ListItemOutsideListIsLiteral()
	{
		Assert.AreEqual( "[*]stray", BBCodeParser.ToHtml( "[*]stray" ) );
	}

	[TestMethod]
	public void ItsHtmlParses()
	{
		// What the menu does with it - the html has to come back as the elements it said
		var node = INode.Parse( BBCodeParser.ToHtml( "[h1]Title[/h1][list][*]one[*]two[/list][quote]q[/quote]" ) );

		Assert.IsNotNull( node );
		Assert.AreEqual( 2, node.Children.Single( x => x.Name == "ul" ).Children.Count( x => x.Name == "li" ) );
		Assert.IsTrue( node.Children.Any( x => x.Name == "h1" ) );
		Assert.IsTrue( node.Children.Any( x => x.Name == "blockquote" ) );
	}
}
