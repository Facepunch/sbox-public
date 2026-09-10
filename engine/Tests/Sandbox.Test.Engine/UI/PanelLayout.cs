using System;
using Sandbox.Engine;
using Sandbox.UI;

namespace UITests;

[TestClass]
[DoNotParallelize]
public partial class PanelLayoutTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	[TestMethod]
	public void LayoutMinimal()
	{
		var root = new PanelLayout( null );
		root.FlexDirection = FlexDirection.Column;
		root.AlignItems = Align.Stretch;
		root.Width = 100.0f;
		root.Height = 100.0f;

		var child = new PanelLayout( null );
		child.FlexGrow = 1;
		root.AddChild( child );

		root.CalculateLayout();

		Assert.AreEqual( 100.0f, child.LayoutWidth );
		Assert.AreEqual( 100.0f, child.LayoutHeight );
		Assert.AreEqual( 0f, child.LayoutX );
		Assert.AreEqual( 0f, child.LayoutY );
	}

	[TestMethod]
	public void LayoutSanity()
	{
		var root = new PanelLayout( null );
		root.Width = 1920;
		root.Height = 1080;

		var child = new PanelLayout( null );
		child.PositionType = PositionMode.Absolute;
		child.Top = 64;
		child.Left = 32;
		child.Width = 100;
		child.Height = 100;

		root.AddChild( child );

		var child2 = new PanelLayout( null );
		child2.PositionType = PositionMode.Absolute;
		child2.Right = 64;
		child2.Bottom = 32;
		child2.Width = 100;
		child2.Height = 100;

		root.AddChild( child2 );

		root.CalculateLayout();

		Assert.AreEqual( 1920f, root.LayoutWidth );
		Assert.AreEqual( 1080f, root.LayoutHeight );
		Assert.AreEqual( 0f, root.LayoutX );
		Assert.AreEqual( 0f, root.LayoutY );

		Assert.AreEqual( 32f, child.LayoutX );
		Assert.AreEqual( 64f, child.LayoutY );

		Assert.AreEqual( 1756f, child2.LayoutX );
		Assert.AreEqual( 948f, child2.LayoutY );
	}

	[TestMethod]
	public void LayoutGrid()
	{
		var root = new PanelLayout( null );
		root.Display = DisplayMode.Grid;
		root.Width = 300;
		root.Height = 200;
		root.GridTemplateColumns = "100px 1fr";
		root.GridTemplateRows = "repeat( 2, 1fr )";
		root.ColumnGap = 10;

		var a = new PanelLayout( null );
		var b = new PanelLayout( null );
		var c = new PanelLayout( null );
		c.GridColumnStart = "1";
		c.GridColumnEnd = "span 2";
		root.AddChild( a );
		root.AddChild( b );
		root.AddChild( c );

		root.CalculateLayout();

		Assert.AreEqual( 100f, a.LayoutWidth );
		Assert.AreEqual( 100f, a.LayoutHeight );
		Assert.AreEqual( 110f, b.LayoutX );
		Assert.AreEqual( 190f, b.LayoutWidth );
		Assert.AreEqual( 100f, c.LayoutY );
		Assert.AreEqual( 300f, c.LayoutWidth );
	}

	[TestMethod]
	public void LayoutBlock()
	{
		var root = new PanelLayout( null );
		root.Display = DisplayMode.Block;
		root.Width = 200;

		var a = new PanelLayout( null );
		a.Height = 30;
		a.MarginBottom = 20;
		var b = new PanelLayout( null );
		b.Height = 30;
		b.MarginTop = 10;
		root.AddChild( a );
		root.AddChild( b );

		root.CalculateLayout();

		Assert.AreEqual( 200f, a.LayoutWidth );
		Assert.AreEqual( 50f, b.LayoutY, "adjoining margins collapse to the larger one" );
		Assert.AreEqual( 80f, root.LayoutHeight );
	}

	[TestMethod]
	public void StyleMutationRecalculatesLayout()
	{
		var root = new PanelLayout( null ) { Width = 100, Height = 100 };
		var child = new PanelLayout( null ) { Width = Length.Percent( 50 ), Height = 20 };
		root.AddChild( child );

		root.CalculateLayout();
		Assert.AreEqual( 50f, child.LayoutWidth );

		root.Width = 240;
		root.CalculateLayout();

		Assert.AreEqual( 120f, child.LayoutWidth );
	}

	[TestMethod]
	public void IndexedInsertionClampsLogicalIndexes()
	{
		var root = new PanelLayout( null );
		var child = new PanelLayout( null );
		var first = new PanelLayout( null );

		root.AddChild( 10, child );
		root.AddChild( -1, first );
		Assert.AreSame( first.Node, root.Node.GetChild( 0 ) );
		Assert.AreSame( child.Node, root.Node.GetChild( 1 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => root.Node.InsertChild( new(), 3 ) );
	}

	[TestMethod]
	public void FlexBasisUsesParentDirection()
	{
		var root = CreateRoot( 200, 100 );
		root.Style.Set( "flex-direction: row;" );
		root.Layout();

		var child = root.AddChild<Panel>();
		child.Style.Set( "flex-direction: column; flex-basis: calc( 50% - 10px );" );
		root.Layout();

		Assert.AreEqual( 90f, child.Box.Rect.Width );

		root.Style.FlexDirection = FlexDirection.Column;
		root.Layout();
		Assert.AreEqual( 40f, child.Box.Rect.Height );
	}

	[TestMethod]
	public void GridCanRelayoutAsBlock()
	{
		var root = CreateRoot( 300, 200 );
		var container = root.AddChild<Panel>();
		container.Style.Set( "position: absolute; width: 300px; display: grid; grid-template-columns: 100px 1fr;" );

		var first = container.AddChild<Panel>();
		first.Style.Set( "height: 20px;" );
		var second = container.AddChild<Panel>();
		second.Style.Set( "height: 30px;" );

		root.Layout();
		Assert.AreEqual( 100f, second.Box.Rect.Left );
		Assert.AreEqual( 0f, second.Box.Rect.Top );

		container.Style.Set( "position: absolute; width: 300px; display: block;" );
		root.Layout();

		Assert.AreEqual( 0f, second.Box.Rect.Left );
		Assert.AreEqual( 20f, second.Box.Rect.Top );
		Assert.AreEqual( 50f, container.Box.Rect.Height );
	}

	[TestMethod]
	public void ChildReorderUpdatesLayoutOrder()
	{
		var root = CreateRoot( 300, 100 );
		root.Style.Set( "flex-direction: row;" );
		var first = AddFixedChild( root );
		var second = AddFixedChild( root );
		var third = AddFixedChild( root );

		root.Layout();
		Assert.AreEqual( 0f, first.Box.Rect.Left );
		Assert.AreEqual( 100f, third.Box.Rect.Left );

		root.SetChildIndex( third, 0 );
		root.Layout();

		Assert.AreEqual( 0f, third.Box.Rect.Left );
		Assert.AreEqual( 50f, first.Box.Rect.Left );
		Assert.AreEqual( 100f, second.Box.Rect.Left );
	}

	[TestMethod]
	public void RelativeUnitsRelayoutWhenContextChanges()
	{
		var root = CreateRoot( 1000, 500 );
		root.Style.Set( "font-size: 20px;" );
		var child = root.AddChild<Panel>();
		child.Style.Set( "position: absolute; width: 10vw; height: 2rem; font-size: 15px; padding-left: 2em;" );

		root.Layout();

		Assert.AreEqual( 100f, child.Box.Rect.Width );
		Assert.AreEqual( 40f, child.Box.Rect.Height );
		Assert.AreEqual( 30f, child.Box.Padding.Left );

		root.PanelBounds = new Rect( 0, 0, 800, 500 );
		root.Style.Set( "font-size: 24px;" );
		root.Layout();

		Assert.AreEqual( 80f, child.Box.Rect.Width );
		Assert.AreEqual( 48f, child.Box.Rect.Height );
		Assert.AreEqual( 30f, child.Box.Padding.Left );
	}

	[TestMethod]
	public void UnrelatedChangesDoNotRepushDescendantLayoutStyles()
	{
		var root = CreateRoot( 1000, 500 );
		root.Style.FontSize = 20;
		var container = AddFixedChild( root );
		var fixedChild = AddFixedChild( container );
		var relative = container.AddChild<Panel>();
		relative.Style.Set( "position: absolute; width: 10vw; height: 2rem; font-size: 15px;" );
		root.Layout();
		root.Layout();
		var fixedUpdates = fixedChild.LayoutTree.StyleUpdateCount;
		var relativeUpdates = relative.LayoutTree.StyleUpdateCount;

		root.Style.BackgroundColor = Color.Red;
		root.Layout();
		Assert.AreEqual( fixedUpdates, fixedChild.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( relativeUpdates, relative.LayoutTree.StyleUpdateCount );

		root.PanelBounds = new Rect( 0, 0, 800, 500 );
		root.Layout();
		Assert.AreEqual( fixedUpdates, fixedChild.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( relativeUpdates + 1, relative.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( 80f, relative.Box.Rect.Width );

		root.Style.FontSize = 24;
		root.Layout();
		Assert.AreEqual( fixedUpdates, fixedChild.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( relativeUpdates + 2, relative.LayoutTree.StyleUpdateCount );
		Assert.AreEqual( 48f, relative.Box.Rect.Height );
	}

	[TestMethod]
	public void InheritedLayoutValuesStillRepush()
	{
		var root = CreateRoot( 300, 200 );
		var parent = AddFixedChild( root );
		var child = parent.AddChild<Panel>();
		child.Style.Set( "width: inherit; height: 2em;" );
		parent.Style.FontSize = 10;
		root.Layout();
		Assert.AreEqual( 50f, child.Box.Rect.Width );
		Assert.AreEqual( 20f, child.Box.Rect.Height );

		parent.Style.Width = 80;
		root.Layout();
		Assert.AreEqual( 80f, child.Box.Rect.Width );
		Assert.AreEqual( 20f, child.Box.Rect.Height );

		parent.Style.FontSize = 15;
		root.Layout();
		Assert.AreEqual( 30f, child.Box.Rect.Height );

		child.Style.Set( "width: 40px; height: 20px;" );
		root.Layout();
		var updates = child.LayoutTree.StyleUpdateCount;
		parent.Style.Width = 100;
		root.Layout();
		Assert.AreEqual( 40f, child.Box.Rect.Width );
		Assert.AreEqual( updates, child.LayoutTree.StyleUpdateCount, "Replacing inherit must clear the inheritance dependency." );
	}

	[TestMethod]
	public void InitialLayoutKeywordsDoNotTrackParentChanges()
	{
		var root = CreateRoot( 300, 200 );
		var child = root.AddChild<Panel>();
		child.Style.Set( "width: initial; padding: unset; margin: revert; height: 20px;" );
		root.Layout();
		root.Layout();
		var updates = child.LayoutTree.StyleUpdateCount;
		root.Style.BackgroundColor = Color.Red;
		root.Layout();
		Assert.AreEqual( updates, child.LayoutTree.StyleUpdateCount );
	}

	[TestMethod]
	public void SelectorCanStartInheritingAnUnchangedLayoutValue()
	{
		var root = CreateRoot( 300, 200 );
		root.StyleSheet.Parse( ".item { width: 50px; height: 20px; } .item.inherited { width: inherit; }" );
		var parent = AddFixedChild( root );
		var child = parent.AddChild<Panel>( "item" );
		root.Layout();
		root.Layout();
		child.AddClass( "inherited" );
		root.Layout();
		Assert.AreEqual( 50f, child.Box.Rect.Width );

		parent.Style.Width = 80;
		root.Layout();
		Assert.AreEqual( 80f, child.Box.Rect.Width );

		child.RemoveClass( "inherited" );
		root.Layout();
		Assert.AreEqual( 50f, child.Box.Rect.Width );
		var updates = child.LayoutTree.StyleUpdateCount;
		parent.Style.Width = 100;
		root.Layout();
		Assert.AreEqual( updates, child.LayoutTree.StyleUpdateCount );
	}

	[TestMethod]
	public void InheritedScrollbarWidthRefreshesGutter()
	{
		var root = CreateRoot( 300, 200 );
		root.Style.ScrollbarWidth = 8;
		var child = root.AddChild<Panel>();
		child.Style.Set( "width: 100px; height: 100px; overflow: scroll; scrollbar-gutter: stable;" );
		root.Layout();
		Assert.AreEqual( 8f, child.LayoutTree.Gutter.Right );
		root.Style.ScrollbarWidth = 12;
		root.Layout();
		Assert.AreEqual( 12f, child.LayoutTree.Gutter.Right );
	}

	[TestMethod]
	public void RemovedRelativeUnitsStopTrackingContext()
	{
		var root = CreateRoot( 300, 200 );
		var child = AddFixedChild( root );
		child.Style.Width = Length.ViewWidth( 10 );
		root.Layout();
		child.Style.Width = 50;
		root.Layout();
		var updates = child.LayoutTree.StyleUpdateCount;
		root.PanelBounds = new Rect( 0, 0, 500, 200 );
		root.Layout();
		Assert.AreEqual( updates, child.LayoutTree.StyleUpdateCount );
	}

	[TestMethod]
	public void CalcTracksReferenceAxisAndFontContext()
	{
		var root = CreateRoot( 200, 100 );
		root.Style.FontSize = 10;
		root.Layout();
		var child = root.AddChild<Panel>();
		child.Style.Set( "position: absolute; width: calc( 50% - 1rem ); height: 20px;" );
		root.Layout();
		Assert.AreEqual( 90f, child.Box.Rect.Width );
		var updates = child.LayoutTree.StyleUpdateCount;

		root.PanelBounds = new Rect( 0, 0, 200, 150 );
		root.Layout();
		Assert.AreEqual( updates, child.LayoutTree.StyleUpdateCount );

		root.Style.FontSize = 20;
		root.Layout();
		Assert.AreEqual( 80f, child.Box.Rect.Width );

		root.PanelBounds = new Rect( 0, 0, 300, 150 );
		root.Layout();
		child.SetNeedsPreLayout();
		root.Layout();
		Assert.AreEqual( 130f, child.Box.Rect.Width );
	}

	[TestMethod]
	public void GridTrackCachesReuseStringsAndRefreshDependentUnits()
	{
		var root = CreateRoot( 1000, 500 );
		root.Style.FontSize = 20;
		var grid = root.AddChild<Panel>();
		grid.Style.Set( "display: grid; position: absolute; grid-template-columns: minmax(2rem, 2rem) 10vw; grid-template-rows: 20px; grid-auto-columns: 3em; grid-auto-rows: 2rem; font-size: 10px;" );
		var first = grid.AddChild<Panel>();
		var second = grid.AddChild<Panel>();
		root.Layout();
		Assert.AreEqual( 40f, first.Box.Rect.Width );
		Assert.AreEqual( 100f, second.Box.Rect.Width );

		var style = grid.LayoutTree.Node.Style;
		var columns = style.GridTemplateColumns;
		var rows = style.GridTemplateRows;
		var autoColumns = style.GridAutoColumns;
		var autoRows = style.GridAutoRows;
		var parses = grid.LayoutTree.GridParseCount;
		grid.Style.Left = 10;
		root.Layout();
		Assert.AreEqual( parses, grid.LayoutTree.GridParseCount );
		Assert.AreSame( columns, style.GridTemplateColumns );
		Assert.AreSame( rows, style.GridTemplateRows );
		CollectionAssert.AreEqual( autoColumns, style.GridAutoColumns );
		CollectionAssert.AreEqual( autoRows, style.GridAutoRows );

		root.Style.FontSize = 30;
		root.PanelBounds = new Rect( 0, 0, 800, 500 );
		root.Layout();
		Assert.AreEqual( 60f, first.Box.Rect.Width );
		Assert.AreEqual( 80f, second.Box.Rect.Width );
		Assert.AreEqual( parses + 2, grid.LayoutTree.GridParseCount );
		Assert.AreNotSame( columns, style.GridTemplateColumns );
		Assert.AreSame( rows, style.GridTemplateRows );
		CollectionAssert.AreEqual( autoColumns, style.GridAutoColumns );
		CollectionAssert.AreNotEqual( autoRows, style.GridAutoRows );

		grid.Style.FontSize = 15;
		root.Layout();
		Assert.AreEqual( parses + 3, grid.LayoutTree.GridParseCount );
		CollectionAssert.AreNotEqual( autoColumns, style.GridAutoColumns );
		Assert.AreEqual( 60f, first.Box.Rect.Width, "An item font change must not alter rem tracks." );
		Assert.AreEqual( 80f, second.Box.Rect.Width );
	}

	[TestMethod]
	public void GapAndBorderDependenciesRefreshWithoutStyleChanges()
	{
		var root = CreateRoot( 200, 100 );
		root.Style.FontSize = 10;
		var child = root.AddChild<Panel>();
		child.Style.Set( "position: absolute; width: 100px; height: 50px; column-gap: 2rem; border-left-width: 10%;" );
		root.Layout();
		child.SetNeedsPreLayout();
		root.PreLayout();
		Assert.AreEqual( 20f, child.LayoutTree.Node.Style.GetGap( Sandbox.Layout.Gutter.Column ).Value );
		Assert.AreEqual( 20f, child.LayoutTree.Node.Style.GetBorder( Sandbox.Layout.Edge.Left ).Value );

		root.Style.FontSize = 15;
		root.PanelBounds = new Rect( 0, 0, 300, 100 );
		root.Layout();
		child.SetNeedsPreLayout();
		root.PreLayout();
		Assert.AreEqual( 30f, child.LayoutTree.Node.Style.GetGap( Sandbox.Layout.Gutter.Column ).Value );
		Assert.AreEqual( 30f, child.LayoutTree.Node.Style.GetBorder( Sandbox.Layout.Edge.Left ).Value );
	}

	private static RootPanel CreateRoot( float width, float height )
	{
		return new RootPanel { PanelBounds = new Rect( 0, 0, width, height ) };
	}

	private static Panel AddFixedChild( Panel parent )
	{
		var child = parent.AddChild<Panel>();
		child.Style.Set( "width: 50px; height: 20px; flex-shrink: 0;" );
		return child;
	}
}
