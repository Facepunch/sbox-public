namespace Sandbox.UI.NodeGraph;

//
// Comments: nodes join one by being dropped inside it and leave through the right-click menu.
// CommentPanel moves its members, grows to keep them inside and resizes from its edges.
//
public partial class NodeGraphPanel
{
	static readonly Vector2 DefaultCommentSize = new( 300f, 192f );

	/// <summary>
	/// Colour of comments added from the menu: the editor's green.
	/// </summary>
	public static readonly Color DefaultCommentColor = Color.Parse( "#33b679" ).Value;

	public IEnumerable<CommentPanel> Comments => _nodes.OfType<CommentPanel>();

	/// <summary>
	/// The comment holding <paramref name="node"/>, or null. Comments can hold comments.
	/// </summary>
	public CommentPanel FindComment( NodePanel node )
	{
		return Comments.FirstOrDefault( x => x != node && x.Comment.Members.Contains( node.Node.Identifier ) );
	}

	/// <summary>
	/// Whether <paramref name="node"/> is <paramref name="comment"/>, or in it at any depth.
	/// </summary>
	public bool IsWithin( NodePanel node, CommentPanel comment )
	{
		// Bounded, so a cycle written into a graph file can't hang this
		for ( var depth = 0; node is not null && depth <= _nodes.Count; depth++ )
		{
			if ( node == comment ) return true;
			node = FindComment( node );
		}

		return false;
	}

	/// <summary>
	/// Add a comment from <see cref="CommentNodeType"/>, as one undo step. Null when the graph has
	/// no comments.
	/// </summary>
	public virtual CommentPanel CreateNewComment( string text, Color color, Vector2 position, Vector2 size )
	{
		if ( CommentNodeType is null ) return null;

		using var undoScope = UndoScope( "Add Comment" );

		return CreateNewNode( CommentNodeType, node =>
		{
			node.Position = position.SnapToGrid( GridSize );

			var comment = (ICommentNode)node;

			comment.Size = size;
			comment.Color = color;
			comment.Title = text;
		} ) as CommentPanel;
	}

	/// <summary>
	/// Add a comment at <paramref name="position"/> holding <paramref name="members"/>, taking
	/// them out of any comment they were in, and select it. Null when the graph has no comments.
	/// </summary>
	public CommentPanel CreateComment( Vector2 position, IReadOnlyList<NodePanel> members )
	{
		if ( Graph is null || CommentNodeType is null ) return null;

		using var undoScope = UndoScope( "Add Comment" );

		if ( CreateNewComment( "Untitled", DefaultCommentColor, position, DefaultCommentSize ) is not { } panel ) return null;

		RemoveFromComment( members );
		panel.Comment.Members.AddRange( members.Select( x => x.Node.Identifier ) );

		SelectNode( panel );
		OrderComments();

		// Around what it holds, rather than the default box at the mouse
		if ( members.Count > 0 ) panel.FitToContent();

		return panel;
	}

	/// <summary>
	/// Put <paramref name="nodes"/> in <paramref name="comment"/>, out of any comment they were in.
	/// A comment can't go inside itself or anything it holds.
	/// </summary>
	public void AddToComment( CommentPanel comment, IEnumerable<NodePanel> nodes )
	{
		foreach ( var node in nodes.ToArray() )
		{
			if ( node is CommentPanel inner && IsWithin( comment, inner ) ) continue;

			FindComment( node )?.Comment.Members.Remove( node.Node.Identifier );
			comment.Comment.Members.Add( node.Node.Identifier );
		}

		OrderComments();
	}

	public void RemoveFromComment( IEnumerable<NodePanel> nodes )
	{
		foreach ( var node in nodes.ToArray() )
		{
			FindComment( node )?.Comment.Members.Remove( node.Node.Identifier );
		}

		OrderComments();
	}

	/// <summary>
	/// Put comments before the nodes, outer ones first, so each draws and takes the mouse above the
	/// comment holding it and below every node.
	/// </summary>
	internal void OrderComments()
	{
		foreach ( var comment in Comments.OrderByDescending( Depth ) )
		{
			if ( comment.Parent is { } parent ) parent.SetChildIndex( comment, 0 );
		}
	}

	int Depth( CommentPanel comment )
	{
		var depth = 0;
		for ( var outer = FindComment( comment ); outer is not null && depth <= _nodes.Count; outer = FindComment( outer ) ) depth++;
		return depth;
	}

	CommentPanel _dropTarget;

	/// <summary>
	/// Selected nodes and comments that would join a comment if dropped in one: in none yet.
	/// </summary>
	IEnumerable<NodePanel> FreeSelectedNodes() => SelectedNodes.Where( x => FindComment( x ) is null );

	/// <summary>
	/// While nodes are dragged, highlight the comment they'd join: the smallest one under the
	/// mouse that isn't moving with them.
	/// </summary>
	internal void UpdateCommentDrop()
	{
		var mouse = ScreenToCanvas( MousePosition );
		var dragged = SelectedNodes.ToArray();
		var target = FreeSelectedNodes().Any()
			? Comments.Where( x => new Rect( x.Position, x.Size ).IsInside( mouse ) && !dragged.Any( d => d is CommentPanel c ? IsWithin( x, c ) : d == x ) )
				.MinBy( x => x.Size.x * x.Size.y )
			: null;

		if ( target == _dropTarget ) return;

		if ( _dropTarget.IsValid() ) _dropTarget.IsDropTarget = false;
		_dropTarget = target;
		if ( target is not null ) target.IsDropTarget = true;
	}

	internal void FinishCommentDrop( bool cancelled )
	{
		var target = _dropTarget;
		_dropTarget = null;

		if ( !target.IsValid() ) return;

		target.IsDropTarget = false;
		if ( !cancelled ) AddToComment( target, FreeSelectedNodes() );
	}

	Menu _menu;

	/// <summary>
	/// Fill the right-click menu for <paramref name="node"/>, the node clicked and already selected,
	/// laid out like the editor's node graph: cut, copy, duplicate or delete the selection, the
	/// node's own entries, then comment it or take it out of its comment. <paramref name="position"/>
	/// is where, in canvas units. Left empty, the menu is the node menu instead.
	/// </summary>
	protected virtual void BuildContextMenu( Menu menu, NodePanel node, Vector2 position )
	{
		if ( node is null ) return;

		var copyable = CopyableSelection().Length;

		if ( copyable > 0 && SerializeSelection() is not null )
		{
			menu.AddOption( $"Cut {NodeCount( copyable )}", "content_cut", CutSelection );
			menu.AddOption( $"Copy {NodeCount( copyable )}", "content_copy", CopySelection );
			menu.AddOption( $"Duplicate {NodeCount( copyable )}", "file_copy", DuplicateSelection );
		}

		var removable = SelectedNodes.Count( x => x.Node.CanRemove );

		if ( removable > 0 )
		{
			menu.AddOption( $"Delete {NodeCount( removable )}", "delete", DeleteSelection );
		}

		if ( node.Node.GoToDefinition is { } goToDefinition )
		{
			menu.AddOption( "Go to Definition", "read_more", goToDefinition );
		}

		// A menu with options of its own is a submenu row
		if ( node.Node.CreateContextMenu( node ) is { } nodeMenu )
		{
			menu.AddOption( nodeMenu );
		}

		var inComment = FindComment( node ) is not null;
		var fittable = node is CommentPanel { Comment.Members.Count: > 0 };
		if ( !inComment && !fittable && CommentNodeType is null ) return;

		if ( menu.HasOptions ) menu.AddSeparator();

		if ( fittable )
		{
			var comment = (CommentPanel)node;
			menu.AddOption( "Fit to Content", "fit_screen", comment.FitToContent );
		}

		if ( inComment )
		{
			var members = SelectedNodes.Where( x => FindComment( x ) is not null ).ToArray();
			menu.AddOption( "Remove from comment", "playlist_remove", () => RemoveFromComment( members ) );
		}

		if ( CommentNodeType is null ) return;

		// Things inside a selected comment stay in it; the new comment takes the comment
		var selectedComments = SelectedNodes.OfType<CommentPanel>().ToArray();
		var selected = SelectedNodes.Where( x => !selectedComments.Any( c => c != x && IsWithin( x, c ) ) ).ToArray();
		menu.AddOption( $"Add Comment for {NodeCount( selected.Length )}", "notes", () => CreateComment( position, selected ) );
	}

	static string NodeCount( int count ) => $"{count} {(count == 1 ? "node" : "nodes")}";

	/// <summary>
	/// Right-clicking a node selects it, then opens the menu for it.
	/// </summary>
	internal void OpenContextMenu( NodePanel node )
	{
		if ( node is not null && !node.Selected ) SelectNode( node );

		OpenContextMenu( ScreenToCanvas( MousePosition ), node );
	}

	/// <summary>
	/// Open the right-click menu at the mouse for <paramref name="node"/>, or for empty space when
	/// null, at <paramref name="position"/> in canvas units. When nothing's on offer for a node -
	/// always on empty space, or for a wire dropped from <paramref name="targetPlug"/> - it's the
	/// node menu; otherwise the node menu is its "Create Node" submenu. <paramref name="onClose"/>
	/// runs once it's gone, or straight away when there was nothing to open.
	/// </summary>
	protected virtual void OpenContextMenu( Vector2 position, NodePanel node, PlugPanel targetPlug = null, Action onClose = null )
	{
		var menu = new Menu();

		if ( targetPlug is null ) BuildContextMenu( menu, node, position );

		var anySelected = menu.HasOptions;

		OnOpenContextMenu( menu, targetPlug );

		if ( !anySelected )
		{
			if ( !menu.HasOptions )
			{
				CreateNodeMenu( menu, position, targetPlug );
			}
			else
			{
				var nodeMenu = new Menu( "Create Node", "add" );
				CreateNodeMenu( nodeMenu, position, targetPlug );

				if ( nodeMenu.Options.Count > 0 ) menu.AddOption( nodeMenu );
				else nodeMenu.Delete( true );
			}
		}

		if ( onClose is not null ) menu.Closed += _ => onClose();

		if ( !OpenMenu( menu ) ) onClose?.Invoke();
	}

	/// <summary>
	/// Add to the right-click menu before it opens. <paramref name="targetPlug"/> is the plug a
	/// wire was dropped from, if that's what opened it.
	/// </summary>
	protected virtual void OnOpenContextMenu( Menu menu, PlugPanel targetPlug ) { }

	/// <summary>
	/// Open a menu under the mouse, filled by <paramref name="build"/>, closing any this graph has
	/// open. Nothing opens when it adds no options.
	/// </summary>
	protected void OpenMenu( Action<Menu> build )
	{
		var menu = new Menu();
		build( menu );
		OpenMenu( menu );
	}

	/// <summary>
	/// Open <paramref name="menu"/> under the mouse, closing any this graph has open, and put the
	/// keyboard in its first text field. It's deleted once closed. False, and deleted, when it has
	/// no options to pick.
	/// </summary>
	protected internal bool OpenMenu( Menu menu )
	{
		_menu?.Delete( true );
		_menu = null;

		if ( menu.Options.Count == 0 )
		{
			menu.Delete( true );
			return false;
		}

		menu.Open( this, Popup.PositionMode.UnderMouse );

		if ( !menu.IsOpen )
		{
			menu.Delete( true );
			return false;
		}

		_menu = menu;
		menu.Closed += OnMenuClosed;

		menu.Rows.OfType<TextEntry>().FirstOrDefault()?.Focus();
		return true;
	}

	void OnMenuClosed( Menu menu )
	{
		if ( _menu == menu ) _menu = null;
		menu.Delete( true );
	}

	//
	// The node menu: a filter box over the types GetRelevantNodes offers
	//

	/// <summary>
	/// The node types the node menu offers for <paramref name="query"/>, such as
	/// <c>AllTypes.Filter( query )</c>. None by default.
	/// </summary>
	protected virtual IEnumerable<INodeType> GetRelevantNodes( NodeQuery query ) => [];

	/// <summary>
	/// Entries above the node types while the filter is empty: paste, and add a comment - or a
	/// reroute, for a wire dropped from <paramref name="targetPlug"/>.
	/// </summary>
	protected virtual void OnPopulateNodeMenuSpecialOptions( Menu menu, Vector2 position, PlugPanel targetPlug, string filter )
	{
		if ( ReadClipboard() is { } text && text.StartsWith( $"{ClipboardIdent}:", StringComparison.Ordinal ) )
		{
			menu.AddOption( "Paste Node(s)", "content_paste", () => Paste( text, position ) );
		}

		if ( !targetPlug.IsValid() )
		{
			if ( CommentNodeType is not null )
			{
				menu.AddOption( "Add Comment", "notes", () => CreateNewComment( "Untitled", DefaultCommentColor, position, DefaultCommentSize ) );
			}
		}
		else if ( RerouteNodeType is not null )
		{
			menu.AddOption( "Add Reroute", "route", () => CreateNewNode( RerouteNodeType, position - new Vector2( ReroutePanel.Diameter * 0.5f ), targetPlug ) );
		}
	}

	/// <summary>
	/// The node types matching <paramref name="filter"/> and <paramref name="targetPlug"/>: as
	/// submenus by path, or a flat list of the best 20 while filtering. Picking one adds it at
	/// <paramref name="position"/>, wired to the plug.
	/// </summary>
	protected virtual void OnPopulateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug, string filter )
	{
		if ( Graph is null ) return;

		var query = new NodeQuery( Graph, targetPlug.IsValid() ? targetPlug.Inner : null, filter );
		var nodes = GetRelevantNodes( query ).ToArray();

		var useFilter = query.Filter.Count > 0;
		var truncated = 0;

		const int maxFilteredResults = 20;

		if ( useFilter && nodes.Length > maxFilteredResults )
		{
			truncated = nodes.Length - maxFilteredResults;
			nodes = nodes.OrderByDescending( x => query.GetScore( x.Path ) ?? int.MinValue ).Take( maxFilteredResults ).ToArray();
		}

		NodeMenu.AddOptions( menu, nodes, useFilter ? query : null, type => CreateNewNode( type, position, targetPlug ) );

		if ( truncated > 0 )
		{
			menu.AddOption( $"... and {truncated} more" ).Enabled = false;
		}
	}

	/// <summary>
	/// Refill the node menu for <paramref name="filter"/>, keeping its filter box.
	/// </summary>
	protected virtual void PopulateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug = null, string filter = null )
	{
		foreach ( var row in menu.Rows.Where( x => x is not TextEntry ).ToArray() )
		{
			menu.Remove( row );
			row.Delete( true );
		}

		if ( string.IsNullOrWhiteSpace( filter ) )
		{
			OnPopulateNodeMenuSpecialOptions( menu, position, targetPlug, filter );
		}

		OnPopulateNodeMenu( menu, position, targetPlug, filter );
	}

	/// <summary>
	/// Make <paramref name="menu"/> the node menu: a filter box over what
	/// <see cref="PopulateNodeMenu"/> fills in.
	/// </summary>
	protected virtual void CreateNodeMenu( Menu menu, Vector2 position, PlugPanel targetPlug = null )
	{
		var filter = menu.AddWidget( new TextEntry { Placeholder = "Filter Nodes.." } );
		filter.AddClass( "node-filter" );
		filter.OnTextEdited = text => PopulateNodeMenu( menu, position, targetPlug, text );

		PopulateNodeMenu( menu, position, targetPlug );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( Graph is null ) return;

		OpenContextMenu( null );
		e.StopPropagation();
	}
}
