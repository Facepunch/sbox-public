using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class StructuralPseudoClassesTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	[TestMethod]
	public void RootMatchesTopOfStylingTree()
	{
		var selector = StyleParser.ParseSelector( ":root" );
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		var otherRoot = new RootPanel();
		var panel = new Panel();
		Assert.IsTrue( selector.Test( root ) );
		Assert.IsTrue( selector.Test( otherRoot ) );
		Assert.IsTrue( selector.Test( panel ) );

		root.StyleSheet.Parse( ".child { width: 10px; height: 10px; } :root > .child { width: 100px; }" );
		panel.AddClass( "child" );
		panel.Parent = root;
		panel.Switch( PseudoClass.Root, true );
		root.Layout();
		Assert.IsFalse( selector.Test( panel ) );
		Assert.AreEqual( 100, panel.Box.Rect.Width );

		var leaf = new Panel( panel, "child" );
		root.Layout();
		Assert.IsFalse( selector.Test( leaf ) );
		Assert.AreEqual( 10, leaf.Box.Rect.Width );

		panel.Parent = null;
		Assert.IsTrue( selector.Test( panel ) );
		Assert.IsFalse( selector.Test( leaf ) );
	}

	[TestMethod]
	public void FocusWithinTracksFocusAndBlur()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( """
			.holder { width: 100px; height: 100px; }
			.holder:focus-within { width: 200px; }
			""" );
		var left = new Panel( root, "holder" );
		var right = new Panel( root, "holder" );
		var a = new Panel( left ) { AcceptsFocus = true };
		var b = new Panel( right ) { AcceptsFocus = true };
		var selector = StyleParser.ParseSelector( ":focus-within" );
		left.Switch( PseudoClass.FocusWithin, true );
		root.Layout();
		Assert.IsFalse( selector.Test( root ) );
		Assert.IsFalse( selector.Test( left ) );

		a.Focus();
		InputFocus.Tick();
		root.Layout();
		Assert.IsTrue( selector.Test( root ) );
		Assert.IsTrue( selector.Test( left ) );
		Assert.IsTrue( selector.Test( a ) );
		Assert.IsFalse( selector.Test( right ) );
		Assert.AreEqual( 200, left.Box.Rect.Width );
		Assert.AreEqual( 100, right.Box.Rect.Width );

		b.Focus();
		InputFocus.Tick();
		root.Layout();
		Assert.IsFalse( selector.Test( left ) );
		Assert.IsTrue( selector.Test( right ) );
		Assert.AreEqual( 100, left.Box.Rect.Width );
		Assert.AreEqual( 200, right.Box.Rect.Width );

		b.Blur();
		InputFocus.Tick();
		root.Layout();
		Assert.IsFalse( selector.Test( root ) );
		Assert.IsFalse( selector.Test( right ) );
		Assert.AreEqual( 100, right.Box.Rect.Width );
	}

	[TestMethod]
	[DataRow( ":ROOT", 10 )]
	[DataRow( ":FOCUS-WITHIN", 10 )]
	[DataRow( ":focus:focus-within", 20 )]
	[DataRow( ":root:not(:focus-within)", 20 )]
	[DataRow( ":nth-child(2n+1)", 10 )]
	public void Specificity( string text, int score )
	{
		var block = new StyleBlock();
		Assert.IsTrue( block.SetSelector( text ) );
		Assert.AreEqual( score, block.Selectors.Single().SelfScore );
	}

	[TestMethod]
	[DataRow( "odd", "1,3,5,7" )]
	[DataRow( "EVEN", "2,4,6,8" )]
	[DataRow( "+3", "3" )]
	[DataRow( "0", "" )]
	[DataRow( "-1", "" )]
	[DataRow( "n", "1,2,3,4,5,6,7,8" )]
	[DataRow( "+n", "1,2,3,4,5,6,7,8" )]
	[DataRow( "-n", "" )]
	[DataRow( "2n", "2,4,6,8" )]
	[DataRow( "2n+1", "1,3,5,7" )]
	[DataRow( " 2N + 1 ", "1,3,5,7" )]
	[DataRow( "2n - 1", "1,3,5,7" )]
	[DataRow( "3n-2", "1,4,7" )]
	[DataRow( "+3n - 2", "1,4,7" )]
	[DataRow( "n-2", "1,2,3,4,5,6,7,8" )]
	[DataRow( "-n+3", "1,2,3" )]
	[DataRow( "-2n + 7", "1,3,5,7" )]
	[DataRow( "0n+3", "3" )]
	[DataRow( "0n-1", "" )]
	[DataRow( "n-2147483648", "1,2,3,4,5,6,7,8" )]
	[DataRow( "2147483647n-2147483646", "1" )]
	[DataRow( "-2147483648n+1", "1" )]
	public void NthChildExpressions( string expression, string expected )
	{
		var parent = new Panel();
		var children = Enumerable.Range( 0, 8 ).Select( _ => new Panel( parent ) ).ToArray();
		var selector = StyleParser.ParseSelector( $":nth-child({expression})" );
		var matched = children.Where( p => selector.Test( p ) ).Select( p => p.SiblingIndex + 1 );
		Assert.AreEqual( expected, string.Join( ",", matched ) );
	}

	[TestMethod]
	[DataRow( "" )]
	[DataRow( " " )]
	[DataRow( "2 n+1" )]
	[DataRow( "+ 2n" )]
	[DataRow( "+ 2" )]
	[DataRow( "2n++1" )]
	[DataRow( "2n+-1" )]
	[DataRow( "2n + -1" )]
	[DataRow( "2n1" )]
	[DataRow( "2n +" )]
	[DataRow( "1.5n" )]
	[DataRow( "n of .item" )]
	[DataRow( "2147483648n" )]
	[DataRow( "n+2147483648" )]
	public void RejectMalformedNthChild( string expression )
	{
		Assert.ThrowsException<System.Exception>( () => StyleParser.ParseSelector( $":nth-child({expression})" ) );
	}

	[TestMethod]
	public void NthChildUpdatesAfterRemovalAndReordering()
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( """
			.holder { height: 10px; }
			.holder:has(> .item:nth-child(3n+1).selected) { height: 20px; }
			.item { width: 10px; height: 10px; }
			.item:nth-child(3n+1) { width: 40px; }
			.leaf { width: 2px; height: 2px; }
			.item:nth-child(3n+1) > .leaf { width: 4px; }
			""" );
		var holder = new Panel( root, "holder" );
		var children = Enumerable.Range( 0, 6 ).Select( _ => new Panel( holder, "item" ) ).ToArray();
		foreach ( var child in children ) new Panel( child, "leaf" );
		children[3].AddClass( "selected" );

		void CheckStyles( int holderHeight )
		{
			root.Layout();
			Assert.AreEqual( holderHeight, holder.Box.Rect.Height );
			var ordered = holder.Children.ToArray();
			for ( int i = 0; i < ordered.Length; i++ )
			{
				Assert.AreEqual( i % 3 == 0 ? 40 : 10, ordered[i].Box.Rect.Width, $"Child {i + 1}" );
				Assert.AreEqual( i % 3 == 0 ? 4 : 2, ordered[i].Children.Single().Box.Rect.Width, $"Leaf {i + 1}" );
			}
		}

		CheckStyles( 20 );
		children[1].Parent = null;
		CheckStyles( 10 );
		holder.SetChildIndex( children[5], 0 );
		CheckStyles( 20 );
	}
}
