namespace Sandbox.PanelGallery;

/// <summary>
/// The virtualized TreeView. A big lazy tree that should scroll without a hitch, with per-item
/// heights, rename, drag &amp; drop, context menus and tooltips all wired up to a real data model.
/// </summary>
public class TreeViewPage : GalleryPage
{
	readonly Sandbox.UI.Label output;
	readonly Sandbox.UI.Label stats;
	readonly Sandbox.UI.TreeView<Node> tree;

	public TreeViewPage() : base( "Tree View", "A virtualized tree over your own objects. Only the rows in view exist as panels, and they're reused as you scroll. Arrow keys move, left/right open and close, F2 renames, drag rows onto each other to reparent." )
	{
		var row = Case( "One million lazy nodes" );

		tree = new Sandbox.UI.TreeView<Node>();
		tree.AddClass( "demo-tree" );
		tree.Roots = Node.MakeRoots( 100 );
		tree.GetChildren = n => n.Children;
		tree.GetParent = n => n.Parent;
		tree.CanExpand = n => n.CanHaveChildren;
		tree.GetHeight = n => n.IsSection ? 34 : 24;
		tree.GetTooltip = n => n.Tooltip;
		tree.OnRow = BindRow;
		tree.OnSelect = n => Say( $"selected {n.Name}" );
		tree.OnActivate = n => Say( $"activated {n.Name}" );
		tree.OnRename = ( n, text ) => { n.Name = text; Say( $"renamed to {text}" ); };
		tree.CanDrag = n => !n.IsSection;
		tree.OnItemDropped = Drop;
		tree.OnContextMenu = OpenMenu;
		row.AddChild( tree );

		var buttons = Case( "Drive it from code" );
		Button( buttons, "Select deep node", () => tree.Select( tree.Roots.ElementAt( 40 ).Children[7].Children[3] ) );
		Button( buttons, "Scroll to random", () => tree.ScrollTo( RandomNode() ) );
		Button( buttons, "Highlight random", () => tree.Highlight( RandomNode() ) );
		Button( buttons, "Open all roots", () => { foreach ( var n in tree.Roots ) tree.Open( n ); } );
		Button( buttons, "Close everything", () => { foreach ( var n in tree.Roots ) tree.Close( n, true ); } );
		Button( buttons, "Add a root", () => ((List<Node>)tree.Roots).Add( new Node( "Added root", 0 ) ) );
		Button( buttons, "Rename cursor row", () => tree.BeginRename( tree.CursorRow ) );

		var statsCase = Case( "Live" );
		stats = statsCase.Add.Label( "-", "output" );

		AddDriveBrowser();

		output = Output();
	}

	public override void Tick()
	{
		base.Tick();

		var text = $"rows {tree.RowCount:n0}   bound {tree.ActiveRowCount}   pooled {tree.PooledRowCount}   binds {tree.BindCount:n0}   cursor {tree.CursorRow}";
		if ( stats.Text != text ) stats.Text = text;
	}

	void BindRow( Sandbox.UI.TreeRow row, Node node )
	{
		row.Text = node.Name;
		row.IconName = node.IsSection ? null : node.CanHaveChildren ? "folder" : "description";
		row.SetClass( "section", node.IsSection );
		row.SetClass( "hidden", node.Hidden );

		// Controls live in Content. Made once per pooled row, then just updated - the click
		// looks up whatever node the row is showing at the time.
		if ( row.Content.ChildrenCount == 0 )
		{
			var eye = row.Content.Add.Icon( "visibility", "eye" );
			eye.AddEventListener( "onclick", () =>
			{
				var current = tree.GetRowItem( row.RowIndex );
				current.Hidden = !current.Hidden;
				BindRow( row, current );
				Say( $"{(current.Hidden ? "hid" : "showed")} {current.Name}" );
			} );
		}

		var icon = (Sandbox.UI.IconPanel)row.Content.GetChild( 0 );
		icon.Text = node.Hidden ? "visibility_off" : "visibility";
	}

	void Drop( Node dragged, Node target )
	{
		if ( target is null )
		{
			Say( $"dropped {dragged.Name} on nothing" );
			return;
		}

		if ( dragged == target || target.IsDescendantOf( dragged ) )
		{
			Say( "can't drop a node into itself" );
			return;
		}

		dragged.Parent?.Children.Remove( dragged );
		((List<Node>)tree.Roots).Remove( dragged );

		if ( target.CanHaveChildren )
		{
			target.Children.Insert( 0, dragged );
			dragged.Parent = target;
			tree.Open( target );
		}
		else
		{
			var siblings = target.Parent?.Children ?? (List<Node>)tree.Roots;
			siblings.Insert( siblings.IndexOf( target ) + 1, dragged );
			dragged.Parent = target.Parent;
		}

		tree.Refresh();
		Say( $"dropped {dragged.Name} on {target.Name}" );
	}

	void OpenMenu( Node node )
	{
		var menu = new Sandbox.UI.Menu();
		menu.AddOption( "Rename", "edit", () => tree.BeginRename( tree.GetItemRow( node ) ) ).Shortcut = "F2";
		menu.AddOption( "Open recursively", "unfold_more", () => tree.Open( node, true ) );
		menu.AddSeparator();
		menu.AddOption( "Add child", "add", () => AddChild( node ) );
		menu.AddOption( "Delete", "delete", () => Delete( node ) ).Shortcut = "Del";

		menu.Open( this, Sandbox.UI.Popup.PositionMode.UnderMouse );
	}

	void AddChild( Node node )
	{
		var child = new Node( $"New node {node.Children.Count}", node.Depth + 1 ) { Parent = node };
		node.Children.Add( child );
		tree.Open( node );
		tree.Select( child );
	}

	void Delete( Node node )
	{
		if ( node.Parent is not null ) node.Parent.Children.Remove( node );
		else ((List<Node>)tree.Roots).Remove( node );

		Say( $"deleted {node.Name}" );
	}

	void Say( string text ) => output.Text = text;

	/// <summary>
	/// The same control over the file system, to show there's nothing to convert: the drive's
	/// directories are the roots, a directory's entries are its children, and that's the binding.
	/// </summary>
	void AddDriveBrowser()
	{
		var driveRoot = System.IO.Path.GetPathRoot( Environment.CurrentDirectory );
		var row = Case( $"Files on {driveRoot}" );

		var files = new Sandbox.UI.TreeView<System.IO.FileSystemInfo>();
		files.AddClass( "demo-tree" );
		files.Comparer = new FullNameComparer();
		files.Roots = Entries( new System.IO.DirectoryInfo( driveRoot ) );
		files.CanExpand = f => f is System.IO.DirectoryInfo;
		files.GetChildren = f => f is System.IO.DirectoryInfo d ? Entries( d ) : null;
		files.GetParent = f => f is System.IO.DirectoryInfo d ? d.Parent : ((System.IO.FileInfo)f).Directory;
		files.GetTooltip = f => f.FullName;
		files.OnRow = ( r, f ) =>
		{
			r.Text = f.Name;
			r.IconName = f is System.IO.DirectoryInfo ? "folder" : "description";
		};
		files.OnActivate = f => Say( $"activated {f.FullName}" );
		row.AddChild( files );

		var buttons = Case( "Drive it from code" );
		buttons.AddChild( new Sandbox.UI.Button( "Highlight this exe", null, "flatbutton", () =>
		{
			var timer = System.Diagnostics.Stopwatch.StartNew();
			files.Highlight( new System.IO.FileInfo( Environment.ProcessPath ) );
			Say( $"Highlight this exe: {timer.Elapsed.TotalMilliseconds:0.00}ms, {files.RowCount:n0} rows" );
		} ) );
	}

	readonly Dictionary<string, List<System.IO.FileSystemInfo>> entries = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A directory's entries, read once. Folders you can't read just have nothing in them.
	/// </summary>
	List<System.IO.FileSystemInfo> Entries( System.IO.DirectoryInfo dir )
	{
		if ( entries.TryGetValue( dir.FullName, out var list ) ) return list;

		list = new List<System.IO.FileSystemInfo>();
		entries[dir.FullName] = list;

		try
		{
			foreach ( var d in dir.EnumerateDirectories() ) list.Add( d );
			foreach ( var f in dir.EnumerateFiles() ) list.Add( f );
		}
		catch ( Exception )
		{
			// Access denied, gone, whatever - it's empty to us
		}

		return list;
	}

	/// <summary>
	/// FileSystemInfo has no value equality, and Parent hands out fresh objects, so key on the path.
	/// </summary>
	class FullNameComparer : IEqualityComparer<System.IO.FileSystemInfo>
	{
		public bool Equals( System.IO.FileSystemInfo a, System.IO.FileSystemInfo b ) => string.Equals( a?.FullName, b?.FullName, StringComparison.OrdinalIgnoreCase );
		public int GetHashCode( System.IO.FileSystemInfo f ) => StringComparer.OrdinalIgnoreCase.GetHashCode( f.FullName );
	}

	/// <summary>
	/// A button that reports how long its action took, including the rebuild it caused.
	/// </summary>
	void Button( Panel parent, string title, Action action )
	{
		parent.AddChild( new Sandbox.UI.Button( title, null, "flatbutton", () =>
		{
			var timer = System.Diagnostics.Stopwatch.StartNew();
			action();
			if ( tree.NeedsRebuild ) tree.Rebuild();
			Say( $"{title}: {timer.Elapsed.TotalMilliseconds:0.00}ms, {tree.RowCount:n0} rows" );
		} ) );
	}

	/// <summary>
	/// Some node a few levels down, so scrolling and highlighting have somewhere to go.
	/// </summary>
	Node RandomNode()
	{
		var node = tree.Roots.ElementAt( Random.Shared.Int( 0, 99 ) );
		var depth = Random.Shared.Int( 1, 3 );
		for ( int i = 0; i < depth && node.CanHaveChildren; i++ )
		{
			node = node.Children[Random.Shared.Int( 0, node.Children.Count - 1 )];
		}

		return node;
	}

	/// <summary>
	/// A tree node that makes its children the first time anyone asks for them. Five levels of
	/// ten gives a million leaves without building any of them up front.
	/// </summary>
	public class Node
	{
		public string Name { get; set; }
		public int Depth { get; }
		public Node Parent { get; set; }
		public bool IsSection { get; init; }
		public bool Hidden { get; set; }

		List<Node> _children;

		public Node( string name, int depth )
		{
			Name = name;
			Depth = depth;
		}

		public bool CanHaveChildren => Depth < 5;

		public string Tooltip => $"{Name} at depth {Depth}";

		public List<Node> Children
		{
			get
			{
				if ( _children is not null ) return _children;

				_children = new List<Node>();
				if ( !CanHaveChildren ) return _children;

				for ( int i = 0; i < 10; i++ )
				{
					var section = Depth == 0 && i == 5;
					_children.Add( new Node( section ? "A taller section row" : $"{Name}.{i}", Depth + 1 ) { Parent = this, IsSection = section } );
				}

				return _children;
			}
		}

		public bool IsDescendantOf( Node other )
		{
			for ( var p = Parent; p is not null; p = p.Parent )
			{
				if ( p == other ) return true;
			}

			return false;
		}

		public static List<Node> MakeRoots( int count )
		{
			var roots = new List<Node>( count );
			for ( int i = 0; i < count; i++ ) roots.Add( new Node( $"Root {i}", 0 ) );
			return roots;
		}
	}
}
