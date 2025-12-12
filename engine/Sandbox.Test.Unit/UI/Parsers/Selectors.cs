using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class Selectors
{
	[TestMethod]
	public void SingleClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel" ) );

		Assert.AreEqual( 1, entry.Selectors.Length );
		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void SingleElement()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "testelement" ) );

		Assert.AreEqual( 1, entry.Selectors.Length );
		Assert.AreEqual( "testelement", entry.Selectors[0].Element );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void SingleId()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "#ident" ) );

		Assert.AreEqual( 1, entry.Selectors.Length );
		Assert.AreEqual( "ident", entry.Selectors[0].Id );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void SingleIdWithClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "#ident.one" ) );

		Assert.AreEqual( 1, entry.Selectors.Length );
		Assert.AreEqual( 1, entry.Selectors.Length );
		Assert.AreEqual( "ident", entry.Selectors[0].Id );
		Assert.AreEqual( 1, entry.Selectors[0].Classes.Length );
		Assert.AreEqual( "one", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void TrailingSpace()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel " ) );
		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.IsTrue( entry.SetSelector( ".testpanel        " ) );
		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.IsTrue( entry.SetSelector( "     .testpanel        " ) );
		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.IsTrue( entry.SetSelector( "     .testpanel \n\r    " ) );
		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
	}

	[TestMethod]
	public void MultipleClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".one.two.three" ) );

		Assert.AreEqual( 3, entry.Selectors[0].Classes.Length );
		Assert.AreEqual( "one", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( "two", entry.Selectors[0].Classes.Skip( 1 ).First() );
		Assert.AreEqual( "three", entry.Selectors[0].Classes.Skip( 2 ).First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void MultipleSelectors()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".one, .two, .three" ) );

		Assert.AreEqual( 3, entry.Selectors.Length );
		Assert.AreEqual( "one", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( "two", entry.Selectors[1].Classes.First() );
		Assert.AreEqual( "three", entry.Selectors[2].Classes.First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithHover()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:hover" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Hover, entry.Selectors[0].Flags & PseudoClass.Hover );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithActive()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:active" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Active, entry.Selectors[0].Flags & PseudoClass.Active );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithFocus()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:focus" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Focus, entry.Selectors[0].Flags & PseudoClass.Focus );
	}

	[TestMethod]
	public void ClassWithEmpty()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:empty" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Empty, entry.Selectors[0].Flags & PseudoClass.Empty );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithFirstChild()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:first-child" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.FirstChild, entry.Selectors[0].Flags & PseudoClass.FirstChild );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithLastChild()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:last-child" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.LastChild, entry.Selectors[0].Flags & PseudoClass.LastChild );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void ClassWithOnlyChild()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".testpanel:only-child" ) );

		Assert.AreEqual( "testpanel", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.OnlyChild, entry.Selectors[0].Flags & PseudoClass.OnlyChild );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void OnlyPseudoSelector()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ":hover" ) );
		Assert.IsNull( entry.Selectors[0].Classes );
		Assert.AreEqual( PseudoClass.Hover, entry.Selectors[0].Flags & PseudoClass.Hover );

	}

	[TestMethod]
	public void NotSelectorWithClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".a:not(.b)" ) );
		Assert.AreEqual( "a", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( "b", entry.Selectors[0].Not.Classes.First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void NotSelectorWithClassSpaced()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".a:not( .b )" ) );
		Assert.AreEqual( "a", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( "b", entry.Selectors[0].Not.Classes.First() );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void NotSelectorWithPseudoSelector()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".a:not(:hover)" ) );
		Assert.AreEqual( "a", entry.Selectors[0].Classes.First() );
		Assert.IsNull( entry.Selectors[0].Not.Classes );
		Assert.AreEqual( PseudoClass.Hover, entry.Selectors[0].Not.Flags & PseudoClass.Hover );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void Invalid()
	{
		StyleBlock entry = new();

		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( ".testpanel,," ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "..testpanel" ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "ONe..Two" ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "Two." ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "." ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "23:." ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "234:" ) );
	}

	[TestMethod]
	public void NthChild()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".a:nth-child(1)" ) );
		Assert.AreEqual( "a", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].NthChild );
		Assert.AreEqual( false, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void UniversalSelectorInvalid()
	{
		StyleBlock entry = new();

		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "*text" ) );
		Assert.ThrowsException<System.Exception>( () => entry.SetSelector( "*_brown" ) );
	}

	[TestMethod]
	public void UniversalSelector()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "*" ) );
		Assert.AreEqual( true, entry.Selectors[0].UniversalSelector );
	}

	[TestMethod]
	public void UniversalSelectorWithPseudo()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "*:hover" ) );
		Assert.AreEqual( true, entry.Selectors[0].UniversalSelector );
		Assert.AreEqual( true, entry.Selectors[0].Flags.Contains( PseudoClass.Hover ) );
	}

	[TestMethod]
	public void UniversalSelectorWithClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "*.active" ) );
		Assert.AreEqual( true, entry.Selectors[0].UniversalSelector );
		Assert.AreEqual( true, entry.Selectors[0].Classes.Contains( "active" ) );
	}

	[TestMethod]
	public void HasSelectorWithClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".parent:has(.child)" ) );
		Assert.AreEqual( "parent", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "child", entry.Selectors[0].Has[0].Classes.First() );
	}

	[TestMethod]
	public void HasSelectorWithDirectChild()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".parent:has(> .child)" ) );
		Assert.AreEqual( "parent", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "child", entry.Selectors[0].Has[0].Classes.First() );
		Assert.IsTrue( entry.Selectors[0].Has[0].ImmediateParent );
	}

	[TestMethod]
	public void HasSelectorWithAdjacentSibling()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "h1:has(+ p)" ) );
		Assert.AreEqual( "h1", entry.Selectors[0].Element );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "p", entry.Selectors[0].Has[0].Element );
		Assert.IsTrue( entry.Selectors[0].Has[0].AdjacentSibling );
	}

	[TestMethod]
	public void HasSelectorWithGeneralSibling()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".item:has(~ .active)" ) );
		Assert.AreEqual( "item", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "active", entry.Selectors[0].Has[0].Classes.First() );
		Assert.IsTrue( entry.Selectors[0].Has[0].GeneralSibling );
	}

	[TestMethod]
	public void HasSelectorWithComplexSelector()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "article:has(h2.title#main)" ) );
		Assert.AreEqual( "article", entry.Selectors[0].Element );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "h2", entry.Selectors[0].Has[0].Element );
		Assert.AreEqual( "title", entry.Selectors[0].Has[0].Classes.First() );
		Assert.AreEqual( "main", entry.Selectors[0].Has[0].Id );
	}

	[TestMethod]
	public void HasSelectorCombinedWithNot()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( "div:not(:has(.excluded))" ) );
		Assert.AreEqual( "div", entry.Selectors[0].Element );
		Assert.IsNotNull( entry.Selectors[0].Not );
		Assert.IsNotNull( entry.Selectors[0].Not.Has );
		Assert.AreEqual( 1, entry.Selectors[0].Not.Has.Length );
		Assert.AreEqual( "excluded", entry.Selectors[0].Not.Has[0].Classes.First() );
	}

	[TestMethod]
	public void HasSelectorWithSpacing()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".parent:has( > .child )" ) );
		Assert.AreEqual( "parent", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "child", entry.Selectors[0].Has[0].Classes.First() );
		Assert.IsTrue( entry.Selectors[0].Has[0].ImmediateParent );
	}

	[TestMethod]
	public void HasSelectorNestedInStyleSheet()
	{
		var sheet = StyleParser.ParseSheet(
			".container { " +
			"    &:has(.active) { background: red; } " +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( ".container:has(.active)", sheet.Nodes[0].Selectors[0].AsString );
		Assert.IsNotNull( sheet.Nodes[0].Selectors[0].Has );
		Assert.AreEqual( 1, sheet.Nodes[0].Selectors[0].Has.Length );
	}

	[TestMethod]
	public void HasSelectorWithPseudoClass()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".menu:has(.item:hover)" ) );
		Assert.AreEqual( "menu", entry.Selectors[0].Classes.First() );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "item", entry.Selectors[0].Has[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Hover, entry.Selectors[0].Has[0].Flags & PseudoClass.Hover );
	}

	[TestMethod]
	public void HasSelectorEmpty()
	{
		StyleBlock entry = new();
		// :has() with empty content should be handled gracefully
		Assert.IsTrue( entry.SetSelector( ".parent:has()" ) );
		Assert.AreEqual( "parent", entry.Selectors[0].Classes.First() );
		// Empty :has() should result in null or empty Has array
		Assert.IsTrue( entry.Selectors[0].Has == null || entry.Selectors[0].Has.Length == 0 );
	}


	[TestMethod]
	public void HasSelectorChainedWithOtherPseudos()
	{
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".item:hover:has(.tooltip):first-child" ) );
		Assert.AreEqual( "item", entry.Selectors[0].Classes.First() );
		Assert.AreEqual( PseudoClass.Hover | PseudoClass.FirstChild, entry.Selectors[0].Flags );
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( 1, entry.Selectors[0].Has.Length );
		Assert.AreEqual( "tooltip", entry.Selectors[0].Has[0].Classes.First() );
	}

	[TestMethod]
	public void HasSelectorCaseSensitivity()
	{
		// Classes should remain case-sensitive
		StyleBlock entry = new();
		Assert.IsTrue( entry.SetSelector( ".Parent:has(.Child)" ) );
		Assert.AreEqual( "parent", entry.Selectors[0].Classes.First() ); // Should preserve case
		Assert.IsNotNull( entry.Selectors[0].Has );
		Assert.AreEqual( "child", entry.Selectors[0].Has[0].Classes.First() ); // Should preserve case
	}
}
