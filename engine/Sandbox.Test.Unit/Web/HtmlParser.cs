using Sandbox.Html;

namespace Web;

[TestClass]
public class HtmlParser
{
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
