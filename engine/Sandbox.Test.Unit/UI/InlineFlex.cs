using Sandbox.UI;

namespace Sandbox.Test.Unit.UI;

/// <summary>
/// Unit tests for display: inline-flex support.
/// Tests that inline-flex containers shrink to fit their content
/// while still using flexbox layout internally.
/// </summary>
[TestClass]
[DoNotParallelize] // RootPanel.AddToLists() modifies shared collections
public class InlineFlex
{
	/// <summary>
	/// Helper to run layout on a root panel
	/// </summary>
	private static void RunLayout( RootPanel root )
	{
		root.PreLayout();
		root.YogaNode?.CalculateLayout();
		root.FinalLayout( Vector2.Zero );
	}

	/// <summary>
	/// Helper to create a root panel with specific size
	/// </summary>
	private static RootPanel CreateRoot( float width = 500, float height = 500 )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, width, height );
		return root;
	}

	[TestMethod]
	[Description( "Verify inline-flex enum value exists and is parsed correctly" )]
	public void InlineFlex_EnumValueExists()
	{
		Assert.AreEqual( 3, (int)DisplayMode.InlineFlex );
	}

	[TestMethod]
	[Description( "Verify CSS parser recognizes 'inline-flex' string" )]
	public void InlineFlex_CSSParsingWorks()
	{
		var styles = new Styles();
		var result = styles.Set( "display", "inline-flex" );

		Assert.IsTrue( result, "Setting display: inline-flex should return true" );
		Assert.AreEqual( DisplayMode.InlineFlex, styles.Display, "Display should be InlineFlex" );
	}

	[TestMethod]
	[Description( "Verify inline-flex container shrinks to content when no width specified" )]
	public void InlineFlex_ShrinksToContent()
	{
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex;" );
		inlineFlex.Parent = root;

		var child = new Panel();
		child.Style.Width = 100;
		child.Style.Height = 50;
		child.Parent = inlineFlex;

		RunLayout( root );

		// Inline-flex should shrink to fit its child (100px wide)
		// rather than stretching to parent width (500px)
		Assert.IsTrue( inlineFlex.Box.Rect.Width <= 100 + 1,
			$"Inline-flex should shrink to content. Expected ~100, got {inlineFlex.Box.Rect.Width}" );
	}

	[TestMethod]
	[Description( "Verify explicit width overrides inline-flex shrink behavior" )]
	public void InlineFlex_ExplicitWidthOverridesShrink()
	{
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex; width: 200px;" );
		inlineFlex.Parent = root;

		var child = new Panel();
		child.Style.Width = 100;
		child.Style.Height = 50;
		child.Parent = inlineFlex;

		RunLayout( root );

		// Explicit width should override shrink-to-content
		Assert.AreEqual( 200, inlineFlex.Box.Rect.Width, 1,
			"Explicit width should override inline-flex shrink behavior" );
	}

	[TestMethod]
	[Description( "Verify inline-flex children still use flex layout rules" )]
	public void InlineFlex_ChildrenStillUseFlex()
	{
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex; width: 300px;" );
		inlineFlex.Parent = root;

		var child1 = new Panel();
		child1.Style.Set( "flex-grow: 1; height: 50px;" );
		child1.Parent = inlineFlex;

		var child2 = new Panel();
		child2.Style.Set( "flex-grow: 1; height: 50px;" );
		child2.Parent = inlineFlex;

		RunLayout( root );

		// Children should share space equally (flex-grow: 1 each)
		Assert.AreEqual( 150, child1.Box.Rect.Width, 1,
			"Children should use flex layout rules" );
		Assert.AreEqual( 150, child2.Box.Rect.Width, 1,
			"Children should use flex layout rules" );
	}

	[TestMethod]
	[Description( "Verify nested inline-flex containers work correctly" )]
	public void InlineFlex_NestedInlineFlex()
	{
		var root = CreateRoot();

		var outer = new Panel();
		outer.Style.Set( "display: inline-flex; gap: 10px;" );
		outer.Parent = root;

		var inner1 = new Panel();
		inner1.Style.Set( "display: inline-flex;" );
		inner1.Parent = outer;

		var innerChild1 = new Panel();
		innerChild1.Style.Width = 50;
		innerChild1.Style.Height = 50;
		innerChild1.Parent = inner1;

		var inner2 = new Panel();
		inner2.Style.Set( "display: inline-flex;" );
		inner2.Parent = outer;

		var innerChild2 = new Panel();
		innerChild2.Style.Width = 30;
		innerChild2.Style.Height = 50;
		innerChild2.Parent = inner2;

		RunLayout( root );

		// Inner containers should shrink to fit their content
		Assert.AreEqual( 50, inner1.Box.Rect.Width, 1 );
		Assert.AreEqual( 30, inner2.Box.Rect.Width, 1 );

		// Outer should be: 50 (inner1) + 10 (gap) + 30 (inner2) = 90
		Assert.AreEqual( 90, outer.Box.Rect.Width, 1 );
	}

	[TestMethod]
	[Description( "Verify empty inline-flex container has zero/minimal size" )]
	public void InlineFlex_EmptyContainer()
	{
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex;" );
		inlineFlex.Parent = root;

		// No children

		RunLayout( root );

		// Empty inline-flex should have zero width
		Assert.AreEqual( 0, inlineFlex.Box.Rect.Width, 1 );
	}

	[TestMethod]
	[Description( "Verify inline-flex with padding includes padding in size" )]
	public void InlineFlex_WithPadding()
	{
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex; padding: 10px;" );
		inlineFlex.Parent = root;

		var child = new Panel();
		child.Style.Width = 80;
		child.Style.Height = 40;
		child.Parent = inlineFlex;

		RunLayout( root );

		// Width should be: 10 (padding-left) + 80 (child) + 10 (padding-right) = 100
		Assert.AreEqual( 100, inlineFlex.Box.Rect.Width, 1 );
		Assert.AreEqual( 60, inlineFlex.Box.Rect.Height, 1 );
	}

	[TestMethod]
	[Description( "Verify inline-flex shrinks to content while regular flex uses default behavior" )]
	public void InlineFlex_ShrinksWhileFlexUsesDefault()
	{
		// Test inline-flex shrinks to content
		var root = CreateRoot();

		var inlineFlex = new Panel();
		inlineFlex.Style.Set( "display: inline-flex;" );
		inlineFlex.Parent = root;

		var inlineChild = new Panel();
		inlineChild.Style.Width = 100;
		inlineChild.Style.Height = 50;
		inlineChild.Parent = inlineFlex;

		RunLayout( root );

		// Inline-flex should shrink to content width (100px)
		Assert.AreEqual( 100, inlineFlex.Box.Rect.Width, 1,
			"Inline-flex should shrink to content width" );
	}

	[TestMethod]
	[Description( "Verify Panel.IsInlineFlex property works correctly" )]
	public void Panel_IsInlineFlexProperty()
	{
		var panel = new Panel();

		panel.Style.Set( "display: flex;" );
		Assert.IsFalse( panel.IsInlineFlex, "display: flex should not be inline-flex" );

		panel.Style.Set( "display: inline-flex;" );
		Assert.IsTrue( panel.IsInlineFlex, "display: inline-flex should be inline-flex" );

		panel.Style.Set( "display: none;" );
		Assert.IsFalse( panel.IsInlineFlex, "display: none should not be inline-flex" );
	}

	[TestMethod]
	[Description( "Verify Panel.UseFlexLayout property works for both flex and inline-flex" )]
	public void Panel_UseFlexLayoutProperty()
	{
		var panel = new Panel();

		panel.Style.Set( "display: flex;" );
		Assert.IsTrue( panel.UseFlexLayout, "display: flex should use flex layout" );

		panel.Style.Set( "display: inline-flex;" );
		Assert.IsTrue( panel.UseFlexLayout, "display: inline-flex should use flex layout" );

		panel.Style.Set( "display: none;" );
		Assert.IsFalse( panel.UseFlexLayout, "display: none should not use flex layout" );
	}
}
