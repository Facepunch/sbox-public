using Sandbox.UI;
using System.Collections.Generic;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class TreeViewTests
{
	class Node
	{
		public string Name;
		public List<Node> Children = new();

		public Node( string name ) { Name = name; }

		public Node Add( string name )
		{
			var child = new Node( name );
			Children.Add( child );
			return child;
		}
	}

	bool previousRenderText;

	/// <summary>
	/// Rows have labels, and a visible label builds a text texture in FinalLayout, which needs the
	/// render system this tier never boots. Text measurement still runs on the CPU.
	/// </summary>
	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		return root;
	}

	/// <summary>
	/// Roots with children, each with grandchildren. 10 roots, 100 children, 1000 grandchildren.
	/// </summary>
	static List<Node> MakeTree( int roots = 10, int children = 10, int grandchildren = 10 )
	{
		var list = new List<Node>();
		for ( int r = 0; r < roots; r++ )
		{
			var root = new Node( $"r{r}" );
			list.Add( root );

			for ( int c = 0; c < children; c++ )
			{
				var child = root.Add( $"r{r}c{c}" );
				for ( int g = 0; g < grandchildren; g++ ) child.Add( $"r{r}c{c}g{g}" );
			}
		}
		return list;
	}

	static TreeView<Node> CreateTree( RootPanel root, List<Node> data, float height = 400 )
	{
		var tree = new TreeView<Node> { Parent = root };
		tree.Style.Set( $"width: 300px; height: {height}px;" );
		tree.RowHeight = 40;
		tree.Roots = data;
		tree.GetChildren = n => n.Children;
		tree.OnRow = ( row, n ) => row.Text = n.Name;
		return tree;
	}

	/// <summary>
	/// Closed roots only: ten rows, and a 400px viewport of 40px rows binds the 10 that fit plus
	/// nothing more - the pool never exceeds what's visible.
	/// </summary>
	[TestMethod]
	public void OnlyVisibleRowsGetPanels()
	{
		var root = CreateRoot();
		var tree = CreateTree( root, MakeTree( 100 ) );

		root.Layout();
		root.Layout();

		Assert.AreEqual( 100, tree.RowCount );

		// 10 fit, plus one partial and one overscan
		Assert.IsTrue( tree.ActiveRowCount >= 10 && tree.ActiveRowCount <= 12, $"bound {tree.ActiveRowCount}" );
		Assert.AreEqual( tree.ActiveRowCount, tree.PooledRowCount );

		var row3 = tree.GetRowPanel( 3 );
		Assert.IsNotNull( row3 );
		Assert.AreEqual( "r3", row3.Text );
		Assert.AreEqual( Length.Pixels( 120 ), row3.Style.Top );
		Assert.AreEqual( Length.Pixels( 40 ), row3.Style.Height );
	}

	/// <summary>
	/// Opening a node splices its children in below it; closing takes them out again. Rows
	/// carry their depth.
	/// </summary>
	[TestMethod]
	public void OpenAndCloseChangeTheRowList()
	{
		var root = CreateRoot();
		var data = MakeTree( 3, 4, 2 );
		var tree = CreateTree( root, data );

		root.Layout();
		root.Layout();
		Assert.AreEqual( 3, tree.RowCount );

		tree.Open( data[1] );
		root.Layout();

		Assert.AreEqual( 7, tree.RowCount );
		Assert.AreEqual( 0, tree.GetRowDepth( 1 ) );
		Assert.AreEqual( 1, tree.GetRowDepth( 2 ) );
		Assert.AreEqual( 0, tree.GetRowDepth( 6 ) );
		Assert.AreEqual( "r1c0", tree.GetRowItem( 2 ).Name );
		Assert.AreEqual( "r2", tree.GetRowItem( 6 ).Name );
		Assert.AreEqual( 1, tree.GetParentRow( 3 ) );

		tree.Open( data[1].Children[0] );
		root.Layout();
		Assert.AreEqual( 9, tree.RowCount );
		Assert.AreEqual( 2, tree.GetRowDepth( 3 ) );

		tree.Close( data[1], recursive: true );
		root.Layout();
		Assert.AreEqual( 3, tree.RowCount );
		Assert.IsFalse( tree.IsOpen( data[1].Children[0] ) );
	}

	/// <summary>
	/// Scrolling rebinds the panels that were already there instead of making new ones. After
	/// warming up, a scroll through the whole list never grows the pool.
	/// </summary>
	[TestMethod]
	public void ScrollingReusesPanels()
	{
		var root = CreateRoot();
		var tree = CreateTree( root, MakeTree( 100 ) );

		root.Layout();
		root.Layout();

		var first = tree.GetRowPanel( 0 );

		tree.ScrollOffset = new Vector2( 0, 400 );
		root.Layout();

		// Ten rows fit, two more can be partially visible, plus one overscan row at each end
		var maxPooled = 10 + 2 + tree.OverscanRows * 2;
		Assert.IsTrue( tree.PooledRowCount <= maxPooled, $"pool grew to {tree.PooledRowCount}" );
		Assert.IsTrue( first.IsValid, "the panel that scrolled out was pooled, not deleted" );
		Assert.IsNull( tree.GetRowPanel( 0 ) );
		Assert.AreEqual( "r10", tree.GetRowPanel( 10 ).Text );

		// Ten pixel steps down the whole list
		for ( int y = 400; y < 100 * 40 - 400; y += 10 )
		{
			tree.ScrollOffset = new Vector2( 0, y );
			root.Layout();
			Assert.IsTrue( tree.PooledRowCount <= maxPooled, $"pool grew to {tree.PooledRowCount} at {y}" );
		}
	}

	/// <summary>
	/// The tree's own work allocates nothing: not an idle frame, and not a frame that scrolls new
	/// rows in and rebinds pooled panels. A full layout still costs a little per re-styled panel
	/// in the style pipeline, so that path gets a small budget rather than zero. Rows here keep a
	/// fixed label so the measurement is the tree, not the text layout - changing a label's text
	/// re-lays it out, and that costs on the order of a kilobyte per row in RichTextKit.
	/// </summary>
	[TestMethod]
	public void ScrollingAllocatesNothing()
	{
		var root = CreateRoot();
		var data = MakeTree( 20, 10, 10 );
		var tree = CreateTree( root, data );
		tree.OnRow = ( row, n ) => { row.Text = "row"; row.IconName = "folder"; };
		foreach ( var n in data ) tree.Open( n );

		root.Layout();
		root.Layout();

		// Warm everything: scroll the whole range once so every code path has run
		for ( int y = 0; y < 220 * 40 - 400; y += 37 )
		{
			tree.ScrollOffset = new Vector2( 0, y );
			root.Layout();
		}

		tree.ScrollOffset = new Vector2( 0, 0 );
		root.Layout();
		root.Layout();

		var before = System.GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ ) root.Layout();
		var idleBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( 0, idleBytes, $"idle frames allocated {idleBytes} bytes over 100 frames" );

		// The tree's tick binds rows and queues style rebuilds; draining that queue is the root's job
		before = System.GC.GetAllocatedBytesForCurrentThread();
		int frames = 0;
		for ( int y = 0; y < 220 * 40 - 400; y += 37 )
		{
			tree.ScrollOffset = new Vector2( 0, y );
			tree.Tick();
			root.BuildStyleRules();
			frames++;
		}

		var tickBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.AreEqual( 0, tickBytes, $"tree tick allocated {tickBytes} bytes over {frames} scrolling frames" );

		tree.ScrollOffset = new Vector2( 0, 0 );
		root.Layout();
		root.Layout();

		before = System.GC.GetAllocatedBytesForCurrentThread();
		frames = 0;
		for ( int y = 0; y < 220 * 40 - 400; y += 37 )
		{
			tree.ScrollOffset = new Vector2( 0, y );
			root.Layout();
			frames++;
		}

		var frameBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsTrue( frameBytes <= frames * 128, $"full layout allocated {frameBytes} bytes over {frames} scrolling frames ({frameBytes / frames} per frame)" );
	}

	/// <summary>
	/// Adding to or removing from a list the tree was given is noticed next tick without a Refresh.
	/// </summary>
	[TestMethod]
	public void ListCountChangesAreDetected()
	{
		var root = CreateRoot();
		var data = MakeTree( 3, 2, 0 );
		var tree = CreateTree( root, data );
		tree.Open( data[0] );

		root.Layout();
		root.Layout();
		Assert.AreEqual( 5, tree.RowCount );

		data[0].Children.Add( new Node( "new" ) );
		root.Layout();
		Assert.AreEqual( 6, tree.RowCount );
		Assert.AreEqual( "new", tree.GetRowItem( 3 ).Name );

		data.Add( new Node( "root" ) );
		root.Layout();
		Assert.AreEqual( 7, tree.RowCount );

		data.RemoveAt( 0 );
		root.Layout();
		Assert.AreEqual( 3, tree.RowCount );
	}

	/// <summary>
	/// Per item heights stack into the row positions and the scrollable extent.
	/// </summary>
	[TestMethod]
	public void PerItemHeightsPositionRows()
	{
		var root = CreateRoot();
		var data = MakeTree( 5, 0, 0 );
		var tree = CreateTree( root, data );
		tree.GetHeight = n => n.Name == "r1" ? 100 : 20;

		root.Layout();
		root.Layout();

		Assert.AreEqual( Length.Pixels( 0 ), tree.GetRowPanel( 0 ).Style.Top );
		Assert.AreEqual( Length.Pixels( 20 ), tree.GetRowPanel( 1 ).Style.Top );
		Assert.AreEqual( Length.Pixels( 100 ), tree.GetRowPanel( 1 ).Style.Height );
		Assert.AreEqual( Length.Pixels( 120 ), tree.GetRowPanel( 2 ).Style.Top );
	}

	/// <summary>
	/// Plain select replaces, ctrl toggles, shift ranges from the anchor, and the panels carry
	/// the selected class.
	/// </summary>
	[TestMethod]
	public void SelectionModifiers()
	{
		var root = CreateRoot();
		var data = MakeTree( 10, 0, 0 );
		var tree = CreateTree( root, data );

		int changes = 0;
		tree.OnSelectionChanged = () => changes++;

		root.Layout();
		root.Layout();

		tree.SelectRow( 2 );
		Assert.AreEqual( 1, tree.Selection.Count );
		Assert.IsTrue( tree.IsSelected( data[2] ) );
		Assert.IsTrue( tree.GetRowPanel( 2 ).HasClass( "selected" ) );

		tree.SelectRow( 4, toggle: true );
		Assert.AreEqual( 2, tree.Selection.Count );

		tree.SelectRow( 4, toggle: true );
		Assert.AreEqual( 1, tree.Selection.Count );
		Assert.IsFalse( tree.GetRowPanel( 4 ).HasClass( "selected" ) );

		tree.SelectRow( 7, range: true );
		Assert.AreEqual( 4, tree.Selection.Count );
		Assert.IsTrue( tree.IsSelected( data[4] ) && tree.IsSelected( data[7] ) );

		tree.SelectRow( 0 );
		Assert.AreEqual( 1, tree.Selection.Count );
		Assert.AreEqual( 5, changes );

		tree.ClearSelection();
		Assert.AreEqual( 0, tree.Selection.Count );
	}

	/// <summary>
	/// Keyboard: down moves the cursor, right opens, left closes then climbs, enter activates.
	/// </summary>
	[TestMethod]
	public void KeyboardNavigation()
	{
		var root = CreateRoot();
		var data = MakeTree( 3, 2, 0 );
		var tree = CreateTree( root, data );

		Node activated = null;
		tree.OnActivate = n => activated = n;

		root.Layout();
		root.Layout();

		Press( tree, "down" );
		Assert.AreEqual( 0, tree.CursorRow );
		Assert.IsTrue( tree.IsSelected( data[0] ) );

		Press( tree, "down" );
		Assert.AreEqual( 1, tree.CursorRow );

		Press( tree, "right" );
		root.Layout();
		Assert.IsTrue( tree.IsOpen( data[1] ) );
		Assert.AreEqual( 5, tree.RowCount );

		Press( tree, "right" );
		Assert.AreEqual( 2, tree.CursorRow );
		Assert.AreSame( data[1].Children[0], tree.CursorItem );

		Press( tree, "left" );
		Assert.AreEqual( 1, tree.CursorRow );

		Press( tree, "left" );
		root.Layout();
		Assert.IsFalse( tree.IsOpen( data[1] ) );
		Assert.AreEqual( 3, tree.RowCount );

		Press( tree, "enter" );
		Assert.AreSame( data[1], activated );

		Press( tree, "end" );
		Assert.AreEqual( 2, tree.CursorRow );

		Press( tree, "home" );
		Assert.AreEqual( 0, tree.CursorRow );

		Press( tree, "down", shift: true );
		Press( tree, "down", shift: true );
		Assert.AreEqual( 3, tree.Selection.Count );
	}

	/// <summary>
	/// Select opens the path to a buried item and scrolls it into view.
	/// </summary>
	[TestMethod]
	public void SelectExpandsPathAndScrolls()
	{
		var root = CreateRoot();
		var data = MakeTree( 50, 5, 5 );
		var tree = CreateTree( root, data );

		root.Layout();
		root.Layout();

		var target = data[40].Children[2].Children[3];
		tree.Select( target );
		root.Layout();

		Assert.IsTrue( tree.IsOpen( data[40] ) );
		Assert.IsTrue( tree.IsOpen( data[40].Children[2] ) );
		Assert.IsTrue( tree.IsSelected( target ) );

		var row = tree.GetItemRow( target );
		Assert.IsTrue( row > 40 );
		Assert.IsNotNull( tree.GetRowPanel( row ), "the selected row is bound, so it's in the viewport" );
		Assert.IsTrue( tree.ScrollOffset.y > 0 );

		Assert.IsFalse( tree.ExpandPathTo( new Node( "stranger" ) ) );
	}

	/// <summary>
	/// A rename swaps in a text entry; submitting it reports the new text.
	/// </summary>
	[TestMethod]
	public void RenameRoundTrips()
	{
		var root = CreateRoot();
		var data = MakeTree( 3, 0, 0 );
		var tree = CreateTree( root, data );

		string renamed = null;
		tree.OnRename = ( n, text ) => { n.Name = text; renamed = text; };

		root.Layout();
		root.Layout();

		tree.SelectRow( 1 );
		Press( tree, "f2" );

		var panel = tree.GetRowPanel( 1 );
		Assert.IsTrue( panel.IsRenaming );

		var entry = panel.Children.OfType<TextEntry>().Single();
		entry.Text = "Renamed";
		entry.CreateEvent( "onsubmit", "Renamed" );
		root.Layout();

		Assert.AreEqual( "Renamed", renamed );
		Assert.AreEqual( "Renamed", data[1].Name );
		Assert.IsFalse( panel.IsRenaming );
		Assert.AreEqual( "Renamed", tree.GetRowPanel( 1 ).Text );
	}

	/// <summary>
	/// Without OnRename, F2 does nothing.
	/// </summary>
	[TestMethod]
	public void RenameNeedsAHandler()
	{
		var root = CreateRoot();
		var tree = CreateTree( root, MakeTree( 3, 0, 0 ) );

		root.Layout();
		root.Layout();

		tree.SelectRow( 1 );
		Press( tree, "f2" );

		Assert.IsFalse( tree.GetRowPanel( 1 ).IsRenaming );
	}

	/// <summary>
	/// Refresh rebinds visible rows so label changes show up, and the sealed non-generic
	/// TreeView works over plain objects.
	/// </summary>
	[TestMethod]
	public void RefreshRebindsAndObjectTreeWorks()
	{
		var root = CreateRoot();
		var tree = new TreeView { Parent = root };
		tree.Style.Set( "width: 300px; height: 400px;" );
		tree.Roots = new List<object> { "one", "two" };

		root.Layout();
		root.Layout();

		Assert.AreEqual( 2, tree.RowCount );
		Assert.AreEqual( "two", tree.GetRowPanel( 1 ).Text );

		var binds = tree.BindCount;
		root.Layout();
		Assert.AreEqual( binds, tree.BindCount, "idle frames don't rebind" );

		tree.Refresh();
		root.Layout();
		Assert.AreEqual( binds + 2, tree.BindCount );

		// A rebuild pools every row and hands them back out; they must come back in place
		Assert.AreEqual( Length.Pixels( 0 ), tree.GetRowPanel( 0 ).Style.Top );
		Assert.AreEqual( Length.Pixels( 24 ), tree.GetRowPanel( 1 ).Style.Top );
	}

	/// <summary>
	/// Highlight opens the path, scrolls there, and puts the highlight class on that row only.
	/// </summary>
	[TestMethod]
	public void HighlightFlashesARow()
	{
		var root = CreateRoot();
		var data = MakeTree( 50, 5, 5 );
		var tree = CreateTree( root, data );
		tree.HighlightTime = 10;

		root.Layout();
		root.Layout();

		var target = data[30].Children[1].Children[2];
		tree.Highlight( target );

		// The scroll lands on the next layout, which is when the row gets a panel
		root.Layout();

		var row = tree.GetItemRow( target );
		Assert.IsTrue( row > 30 );
		Assert.IsTrue( tree.IsOpen( data[30] ) );
		Assert.IsNotNull( tree.GetRowPanel( row ) );
		Assert.IsTrue( tree.GetRowPanel( row ).HasClass( "highlight" ) );
		Assert.IsFalse( tree.GetRowPanel( row ).HasClass( "highlight-fade" ) );
		Assert.IsFalse( tree.GetRowPanel( row - 1 ).HasClass( "highlight" ) );
	}

	/// <summary>
	/// A recursive close never asks a closed node for its children - lazy trees would otherwise
	/// build their whole subtree just to close it.
	/// </summary>
	[TestMethod]
	public void RecursiveCloseSkipsClosedNodes()
	{
		var root = CreateRoot();
		var data = MakeTree( 2, 3, 3 );
		var tree = CreateTree( root, data );

		int asked = 0;
		tree.GetChildren = n => { asked++; return n.Children; };
		tree.Open( data[0] );
		tree.Open( data[0].Children[1] );

		root.Layout();
		root.Layout();
		asked = 0;

		tree.Close( data[0], recursive: true );

		// Root 0 and its open child were asked; the two closed children and root 1 were not
		Assert.AreEqual( 2, asked );
		Assert.IsFalse( tree.IsOpen( data[0].Children[1] ) );
	}

	/// <summary>
	/// With GetParent the path to an item is walked upwards, never searched for - a lazy tree is
	/// not asked for any children it hasn't shown.
	/// </summary>
	[TestMethod]
	public void GetParentAvoidsSearchingForThePath()
	{
		var root = CreateRoot();
		var data = MakeTree( 20, 5, 5 );
		var parents = new Dictionary<Node, Node>();
		foreach ( var r in data )
			foreach ( var c in r.Children )
			{
				parents[c] = r;
				foreach ( var g in c.Children ) parents[g] = c;
			}

		var tree = CreateTree( root, data );
		tree.GetParent = n => parents.GetValueOrDefault( n );

		int asked = 0;
		tree.GetChildren = n => { asked++; return n.Children; };

		root.Layout();
		root.Layout();
		asked = 0;

		var target = data[15].Children[3].Children[1];
		Assert.IsTrue( tree.ExpandPathTo( target ) );
		tree.Rebuild();

		Assert.IsTrue( tree.IsOpen( data[15] ) && tree.IsOpen( data[15].Children[3] ) );
		Assert.IsTrue( tree.GetItemRow( target ) > 0 );

		// Only the rebuild asked: the roots that can expand, plus the two open nodes' children
		Assert.IsTrue( asked <= 20 + 5 + 5, $"asked {asked} times" );
	}

	/// <summary>
	/// Binding straight to the file system: DirectoryInfo children, Parent for the path, and a
	/// comparer on the full path because FileSystemInfo has no value equality and Parent hands
	/// out fresh objects. Highlighting a file deep in a temp folder opens every folder above it.
	/// </summary>
	[TestMethod]
	public void BindsToTheFileSystemWithAComparer()
	{
		var temp = System.IO.Directory.CreateTempSubdirectory( "sbox-treeview-" );
		try
		{
			var deep = temp.CreateSubdirectory( "a" ).CreateSubdirectory( "b" );
			var file = new System.IO.FileInfo( System.IO.Path.Combine( deep.FullName, "leaf.txt" ) );
			System.IO.File.WriteAllText( file.FullName, "x" );
			temp.CreateSubdirectory( "c" );

			var root = CreateRoot();
			var tree = new TreeView<System.IO.FileSystemInfo> { Parent = root };
			tree.Style.Set( "width: 300px; height: 400px;" );
			tree.Comparer = new PathComparer();
			tree.Roots = temp.GetFileSystemInfos();
			tree.CanExpand = f => f is System.IO.DirectoryInfo;
			tree.GetChildren = f => f is System.IO.DirectoryInfo d ? d.GetFileSystemInfos() : null;
			tree.GetParent = f => f is System.IO.DirectoryInfo d ? d.Parent : ((System.IO.FileInfo)f).Directory;
			tree.OnRow = ( r, f ) => r.Text = f.Name;

			root.Layout();
			root.Layout();
			Assert.AreEqual( 2, tree.RowCount );

			// A different FileInfo object for the same file
			tree.Highlight( new System.IO.FileInfo( file.FullName ) );
			root.Layout();

			Assert.AreEqual( 4, tree.RowCount );
			Assert.AreEqual( "leaf.txt", tree.GetRowItem( 2 ).Name );
			Assert.AreEqual( 2, tree.GetRowDepth( 2 ) );
			Assert.IsTrue( tree.GetRowPanel( 2 ).HasClass( "highlight" ) );
		}
		finally
		{
			temp.Delete( true );
		}
	}

	/// <summary>
	/// The highlight fade is a background-color transition on a row that had no background before.
	/// Run it through hold, fade and clear with real styles to make sure that path holds up.
	/// </summary>
	[TestMethod]
	public void HighlightFadeTransitionRuns()
	{
		var root = CreateRoot();
		root.StyleSheet.Parse( ".tree-row.highlight { background-color: rgba( white, 0.35 ); } .tree-row.highlight-fade { background-color: rgba( white, 0 ); transition: background-color 1s ease-out; } .tree-row.selected { background-color: rgba( white, 0.12 ); }" );

		var data = MakeTree( 5, 2, 0 );
		var tree = CreateTree( root, data );
		tree.HighlightTime = 0;
		tree.HighlightFadeTime = 0.05f;

		root.Layout();
		root.Layout();

		tree.Highlight( data[2] );
		root.Layout();

		var panel = tree.GetRowPanel( 2 );
		Assert.IsTrue( panel.HasClass( "highlight" ) || panel.HasClass( "highlight-fade" ) );

		// Keep ticking well past the fade so the transition starts, runs and expires
		var end = RealTime.Now + 0.3f;
		while ( RealTime.Now < end )
		{
			root.Layout();
			System.Threading.Thread.Sleep( 5 );
		}

		Assert.IsFalse( panel.HasClass( "highlight" ) );
		Assert.IsFalse( panel.HasClass( "highlight-fade" ) );

		// Highlighting again while selected, and scrolling it out and back, must be fine too
		tree.SelectRow( 2 );
		tree.Highlight( data[2] );
		root.Layout();
		tree.ScrollOffset = new Vector2( 0, 2000 );
		root.Layout();
		tree.ScrollOffset = new Vector2( 0, 0 );
		root.Layout();
		Assert.IsNotNull( tree.GetRowPanel( 2 ) );
	}

	class PathComparer : IEqualityComparer<System.IO.FileSystemInfo>
	{
		public bool Equals( System.IO.FileSystemInfo a, System.IO.FileSystemInfo b ) => string.Equals( a?.FullName, b?.FullName, System.StringComparison.OrdinalIgnoreCase );
		public int GetHashCode( System.IO.FileSystemInfo f ) => System.StringComparer.OrdinalIgnoreCase.GetHashCode( f.FullName );
	}

	static void Press( BaseTreeView tree, string button, bool shift = false )
	{
		var modifiers = shift ? KeyboardModifiers.Shift : KeyboardModifiers.None;
		tree.OnButtonEvent( new ButtonEvent( button, true, 0, modifiers ) );
	}
}
