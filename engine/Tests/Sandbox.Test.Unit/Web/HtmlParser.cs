using Sandbox.Html;
using Sandbox.UI;

namespace WebTests;

[TestClass]
public class HtmlParserTest
{
	[TestMethod]
	[DataRow( "disabled", true )]
	[DataRow( "checked", true )]
	[DataRow( "other", true )]
	[DataRow( "disable", false )]
	[DataRow( "missing", false )]
	[DataRow( "DISABLED", false )]
	[DataRow( "", false )]
	[DataRow( null, false )]
	public void HasClassMatchesWholeTokens( string classname, bool expected )
	{
		IStyleTarget target = Node.Parse( "<div class=' \tdisabled\r\nchecked\fother  '></div>" ).ChildNodes.Single();
		Assert.AreEqual( expected, target.HasClass( classname ) );
		IStyleTarget empty = Node.Parse( "<div></div>" ).ChildNodes.Single();
		Assert.IsFalse( empty.HasClass( classname ) );
	}

	[TestMethod]
	public void HasClassesRequiresEveryClass()
	{
		IStyleTarget target = Node.Parse( "<div class='disabled checked other'></div>" ).ChildNodes.Single();
		Assert.IsTrue( target.HasClasses( ["disabled", "checked"] ) );
		Assert.IsTrue( target.HasClasses( ["checked", "disabled"] ) );
		Assert.IsFalse( target.HasClasses( ["disabled", "missing"] ) );
		Assert.IsFalse( target.HasClasses( ["missing", "checked"] ) );
		Assert.IsFalse( target.HasClasses( ["disable", "checked"] ) );
	}

	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ClassLookupsDoNotAllocate()
	{
		IStyleTarget target = Node.Parse( "<div class='disabled checked'></div>" ).ChildNodes.Single();
		string[] required = ["disabled", "checked"];
		string[] missing = ["disabled", "missing"];
		bool matched = false;
		Allocations.AssertSettles( "Checking HTML classes", () =>
		{
			matched = target.HasClass( "disabled" ) && target.HasClass( "checked" ) && !target.HasClass( "missing" )
				&& target.HasClasses( required ) && !target.HasClasses( missing );
		} );
		Assert.IsTrue( matched );
	}

	[TestMethod]
	public void ParseBasic()
	{
		var node = Node.Parse( "<poop></poop>" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 1, node.ChildNodes.Count );
	}

	[TestMethod]
	public void ParseMultipleRoot()
	{
		var node = Node.Parse( "<poop></poop><poop></poop><poop></poop>" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 3, node.ChildNodes.Count );
	}

	[TestMethod]
	public void ParseMultipleDifferentRoot()
	{
		var node = Node.Parse( "<a></a><b></b><c></c>" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 3, node.ChildNodes.Count );
		Assert.AreEqual( "a", node.ChildNodes[0].Name );
		Assert.AreEqual( "b", node.ChildNodes[1].Name );
		Assert.AreEqual( "c", node.ChildNodes[2].Name );
	}

	[TestMethod]
	public void ParseSameNameUnclosed()
	{
		var node = Node.Parse( "<poop></poop><poop><poop></poop>" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 3, node.ChildNodes.Count );
		Assert.AreEqual( "poop", node.ChildNodes[0].Name );
		Assert.AreEqual( "poop", node.ChildNodes[1].Name );
		Assert.AreEqual( "poop", node.ChildNodes[2].Name );
	}

	[TestMethod]
	public void ParseEmpty()
	{
		var node = Node.Parse( "" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 0, node.ChildNodes.Count );
	}

	[TestMethod]
	public void UnclosedChildren()
	{
		var node = Node.Parse( "<surround><one><two><three></surround>" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 1, node.ChildNodes.Count );
		Assert.AreEqual( "surround", node.ChildNodes[0].Name );
		Assert.AreEqual( 3, node.ChildNodes[0].ChildNodes.Count );
		Assert.AreEqual( "one", node.ChildNodes[0].ChildNodes[0].Name );
		Assert.AreEqual( "two", node.ChildNodes[0].ChildNodes[1].Name );
		Assert.AreEqual( "three", node.ChildNodes[0].ChildNodes[2].Name );
	}

	[TestMethod]
	public void EntsText()
	{
		var node = Node.Parse( "hello &gt; hello" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 1, node.ChildNodes.Count );
		Assert.IsTrue( node.ChildNodes[0].IsText );
		Assert.AreEqual( "hello > hello", node.ChildNodes[0].InnerHtml );
	}

	[TestMethod]
	public void EntsAttribute()
	{
		var node = Node.Parse( "<div test=\"hello &gt; hello\">" );

		Assert.IsNotNull( node );
		Assert.AreEqual( 1, node.ChildNodes.Count );
		Assert.IsTrue( node.ChildNodes[0].IsElement );
		Assert.IsTrue( node.ChildNodes[0].HasAttributes );
		Assert.AreEqual( 1, node.ChildNodes[0].Attributes.Count );
		Assert.AreEqual( "test", node.ChildNodes[0].Attributes[0].Name );
		Assert.AreEqual( "hello > hello", node.ChildNodes[0].Attributes[0].Value );
	}
}
