using Sandbox.UI;
using System.Collections.Generic;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class VirtualPanelTest
{
	/// <summary>
	/// The Items setter calls StateHasChanged, which asserts main-thread access, so mark the test
	/// thread as the main thread (the same thing SceneTest does for scene tests).
	/// </summary>
	[TestInitialize]
	public void MarkTestThreadAsMain()
	{
		ThreadSafe.MarkMainThread();
	}

	/// <summary>
	/// Creates a 1000x1000 root panel for virtual panel layout tests.
	/// </summary>
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// Boxed integers 0..count-1 as a List&lt;object&gt; - an IList, so the virtual panel keeps a
	/// reference to it for source change detection.
	/// </summary>
	static List<object> MakeItems( int count )
	{
		return Enumerable.Range( 0, count ).Select( x => (object)x ).ToList();
	}

	/// <summary>
	/// A VirtualList with a 400px viewport and 50px items creates only the visible window of
	/// cells (8 rows plus one buffer row), not one panel per item, and ItemHeight drives each
	/// cell's absolute position and height.
	/// </summary>
	[TestMethod]
	public void ListCreatesOnlyVisibleCells()
	{
		var root = CreateRoot();
		var list = new VirtualList { Parent = root };
		list.Style.Set( "width: 200px; height: 400px;" );
		list.ItemHeight = 50;

		var created = new List<int>();
		var cells = new Dictionary<int, Panel>();
		list.OnCreateCell = ( cell, data ) => { created.Add( (int)data ); cells[(int)data] = cell; };
		list.Items = MakeItems( 1000 );

		// First pass computes styles and the box; the second pass ticks with a valid layout,
		// which is what creates the visible cells.
		root.Layout();
		root.Layout();

		Assert.AreEqual( 1000, list.ItemCount );
		Assert.AreEqual( 9, list.ChildrenCount );
		Assert.IsTrue( created.SequenceEqual( Enumerable.Range( 0, 9 ) ) );

		// Cell 3 sits at 3 * ItemHeight and is ItemHeight tall
		Assert.AreEqual( Length.Pixels( 150 ), cells[3].Style.Top );
		Assert.AreEqual( Length.Pixels( 50 ), cells[3].Style.Height );
	}

	/// <summary>
	/// Scrolling a VirtualList moves the window of created cells: panels that scrolled out are
	/// deleted and the newly visible indices are created in their place.
	/// </summary>
	[TestMethod]
	public void ListScrollingMovesTheCellWindow()
	{
		var root = CreateRoot();
		var list = new VirtualList { Parent = root };
		list.Style.Set( "width: 200px; height: 400px;" );
		list.ItemHeight = 50;

		var created = new List<int>();
		var cells = new Dictionary<int, Panel>();
		list.OnCreateCell = ( cell, data ) => { created.Add( (int)data ); cells[(int)data] = cell; };
		list.Items = MakeItems( 1000 );

		root.Layout();
		root.Layout();
		created.Clear();

		// Scroll down 10 items - the visible window becomes [10, 19)
		list.ScrollOffset = new Vector2( 0, 500 );
		root.Layout();

		Assert.AreEqual( 9, list.ChildrenCount );
		Assert.IsTrue( created.SequenceEqual( Enumerable.Range( 10, 9 ) ) );

		// The first cell scrolled out of view and was deleted for real
		Assert.IsFalse( cells[0].IsValid );
	}

	/// <summary>
	/// ItemHeight is clamped to a minimum of 1 and otherwise round-trips through the layout.
	/// </summary>
	[TestMethod]
	public void ListItemHeightIsClamped()
	{
		var list = new VirtualList();

		list.ItemHeight = 0;
		Assert.AreEqual( 1f, list.ItemHeight );

		list.ItemHeight = 32;
		Assert.AreEqual( 32f, list.ItemHeight );
	}

	/// <summary>
	/// Assigning Items only marks the panel for rebuild when the sequence actually changed -
	/// re-assigning an equal sequence is detected and skipped.
	/// </summary>
	[TestMethod]
	public void ItemsSetterDetectsSequenceEquality()
	{
		var list = new VirtualList();

		list.Items = new List<object> { 1, 2, 3 };
		Assert.IsTrue( list.NeedsRebuild );
		Assert.AreEqual( 3, list.ItemCount );

		list.NeedsRebuild = false;
		list.Items = new List<object> { 1, 2, 3 };
		Assert.IsFalse( list.NeedsRebuild );

		list.Items = new List<object> { 1, 2, 4 };
		Assert.IsTrue( list.NeedsRebuild );
	}

	/// <summary>
	/// The item collection helpers (AddItem, RemoveItem, InsertItem, RemoveAt, Clear) keep
	/// ItemCount and HasData in sync and mark the panel for rebuild.
	/// </summary>
	[TestMethod]
	public void ItemCollectionEditsMarkForRebuild()
	{
		var list = new VirtualList();
		list.Items = new List<object> { "a", "b", "c" };
		list.NeedsRebuild = false;

		list.AddItem( "d" );
		Assert.IsTrue( list.NeedsRebuild );
		Assert.AreEqual( 4, list.ItemCount );

		list.NeedsRebuild = false;
		Assert.IsTrue( list.RemoveItem( "b" ) );
		Assert.IsTrue( list.NeedsRebuild );
		Assert.AreEqual( 3, list.ItemCount );
		Assert.IsFalse( list.RemoveItem( "zz" ) );

		list.InsertItem( 0, "front" );
		Assert.AreEqual( 4, list.ItemCount );

		list.RemoveAt( 0 );
		Assert.AreEqual( 3, list.ItemCount );

		Assert.IsTrue( list.HasData( 0 ) );
		Assert.IsTrue( list.HasData( 2 ) );
		Assert.IsFalse( list.HasData( 3 ) );
		Assert.IsFalse( list.HasData( -1 ) );

		list.Clear();
		Assert.AreEqual( 0, list.ItemCount );
	}

	/// <summary>
	/// When Items was assigned from an IList, mutating that source list (count change) is detected
	/// on the next tick and the cells are rebuilt to match - without re-assigning Items.
	/// </summary>
	[TestMethod]
	public void SourceListCountChangesAreDetected()
	{
		var root = CreateRoot();
		var list = new VirtualList { Parent = root };
		list.Style.Set( "width: 200px; height: 400px;" );
		list.ItemHeight = 50;

		var source = new List<object> { 0, 1, 2 };
		list.Items = source;

		root.Layout();
		root.Layout();
		Assert.AreEqual( 3, list.ChildrenCount );

		// Mutate the original list without touching the panel
		source.Add( 3 );
		root.Layout();

		Assert.AreEqual( 4, list.ItemCount );
		Assert.AreEqual( 4, list.ChildrenCount );
	}

	/// <summary>
	/// OnLastCell fires once when the final item's cell is created. AddItem does not re-arm it -
	/// only a fresh Items assignment does.
	/// </summary>
	[TestMethod]
	public void LastCellCallbackFiresOncePerItemsAssignment()
	{
		var root = CreateRoot();
		var list = new VirtualList { Parent = root };
		list.Style.Set( "width: 200px; height: 400px;" );
		list.ItemHeight = 50;

		int lastCellCalls = 0;
		list.OnLastCell = () => lastCellCalls++;
		list.Items = MakeItems( 3 );

		root.Layout();
		root.Layout();
		Assert.AreEqual( 1, lastCellCalls );

		// AddItem creates the new final cell but does not re-arm the callback
		list.AddItem( 99 );
		root.Layout();
		Assert.AreEqual( 4, list.ChildrenCount );
		Assert.AreEqual( 1, lastCellCalls );

		// A fresh Items assignment re-arms it
		list.Items = MakeItems( 5 );
		root.Layout();
		Assert.AreEqual( 2, lastCellCalls );
	}

	/// <summary>
	/// A VirtualGrid with a 200px wide viewport and 100px square items lays out two columns and
	/// creates only the visible rows plus one buffer row, positioning cells in a grid pattern.
	/// </summary>
	[TestMethod]
	public void GridCreatesOnlyVisibleCells()
	{
		var root = CreateRoot();
		var grid = new VirtualGrid { Parent = root };
		grid.Style.Set( "width: 200px; height: 400px;" );
		grid.ItemSize = new Vector2( 100, 100 );

		var created = new List<int>();
		var cells = new Dictionary<int, Panel>();
		grid.OnCreateCell = ( cell, data ) => { created.Add( (int)data ); cells[(int)data] = cell; };
		grid.Items = MakeItems( 500 );

		root.Layout();
		root.Layout();

		// 2 columns, 4 visible rows + 1 buffer row = 10 cells
		Assert.AreEqual( 10, grid.ChildrenCount );
		Assert.IsTrue( created.SequenceEqual( Enumerable.Range( 0, 10 ) ) );

		// Cell 3 = row 1, column 1 of the 100px grid
		Assert.AreEqual( Length.Pixels( 100 ), cells[3].Style.Left );
		Assert.AreEqual( Length.Pixels( 100 ), cells[3].Style.Top );
		Assert.AreEqual( Length.Pixels( 100 ), cells[3].Style.Width );
		Assert.AreEqual( Length.Pixels( 100 ), cells[3].Style.Height );
	}

	/// <summary>
	/// Scrolling a VirtualGrid moves the window of created cells by whole rows: cells scrolled out
	/// are deleted and the newly visible indices are created.
	/// </summary>
	[TestMethod]
	public void GridScrollingMovesTheCellWindow()
	{
		var root = CreateRoot();
		var grid = new VirtualGrid { Parent = root };
		grid.Style.Set( "width: 200px; height: 400px;" );
		grid.ItemSize = new Vector2( 100, 100 );

		var created = new List<int>();
		var cells = new Dictionary<int, Panel>();
		grid.OnCreateCell = ( cell, data ) => { created.Add( (int)data ); cells[(int)data] = cell; };
		grid.Items = MakeItems( 500 );

		root.Layout();
		root.Layout();
		created.Clear();

		// Scroll down two 100px rows - the visible window becomes [4, 14)
		grid.ScrollOffset = new Vector2( 0, 200 );
		root.Layout();

		Assert.AreEqual( 10, grid.ChildrenCount );
		Assert.IsTrue( created.SequenceEqual( Enumerable.Range( 10, 4 ) ) );

		// The first row scrolled out and was deleted for real
		Assert.IsFalse( cells[0].IsValid );
	}

	/// <summary>
	/// ItemSize round-trips through the grid layout and each axis is clamped to a minimum of 1.
	/// </summary>
	[TestMethod]
	public void GridItemSizeIsClampedAndRoundTrips()
	{
		var grid = new VirtualGrid();

		grid.ItemSize = new Vector2( 64, 32 );
		Assert.AreEqual( new Vector2( 64, 32 ), grid.ItemSize );

		grid.ItemSize = new Vector2( 0.25f, 0.25f );
		Assert.AreEqual( new Vector2( 1, 1 ), grid.ItemSize );
	}
}
