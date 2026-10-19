namespace Sandbox.UI.NodeGraph;

//
// Comments: nodes join one by being dropped inside it and leave through the right-click menu.
// CommentPanel moves its members, grows to keep them inside and resizes from its edges.
//
public partial class NodeGraphPanel
{
	static readonly Vector2 DefaultCommentSize = new( 300f, 192f );

	/// <summary>
	/// Makes a new comment for the right-click menu to add. Null means this graph has no comments.
	/// </summary>
	public Func<ICommentNode> CommentFactory { get; set; }

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
	/// Add a comment at <paramref name="position"/> holding <paramref name="members"/>, taking
	/// them out of any comment they were in. Null when the graph has no comments.
	/// </summary>
	public CommentPanel CreateComment( Vector2 position, IReadOnlyList<NodePanel> members )
	{
		if ( Graph is null || CommentFactory?.Invoke() is not { } comment ) return null;

		comment.Position = position;
		if ( comment.Size == default ) comment.Size = DefaultCommentSize;

		RemoveFromComment( members );
		comment.Members.AddRange( members.Select( x => x.Node.Identifier ) );

		var panel = AddNode( comment ) as CommentPanel;
		SelectNode( panel );
		OrderComments();

		// Around what it holds, rather than the default box at the mouse
		if ( members.Count > 0 ) panel?.FitToContent();

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
	/// Fill the right-click menu, laid out like the editor's node graph. <paramref name="node"/> is
	/// the node clicked, already selected, or null for empty space; <paramref name="position"/> is
	/// where, in canvas units. Empty space offers to paste and add a comment; a node offers to cut,
	/// copy, duplicate or delete the selection, then to comment it or take it out of its comment.
	/// </summary>
	protected virtual void BuildContextMenu( Menu menu, NodePanel node, Vector2 position )
	{
		if ( node is null )
		{
			if ( ReadClipboard() is { } text && text.StartsWith( $"{ClipboardIdent}:", StringComparison.Ordinal ) )
			{
				menu.AddOption( "Paste Node(s)", "content_paste", () => Paste( text, position ) );
			}

			if ( CommentFactory is not null ) menu.AddOption( "Add Comment", "notes", () => CreateComment( position, [] ) );
			return;
		}

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

		var inComment = FindComment( node ) is not null;
		var fittable = node is CommentPanel { Comment.Members.Count: > 0 };
		if ( !inComment && !fittable && CommentFactory is null ) return;

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

		if ( CommentFactory is null ) return;

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

		var position = ScreenToCanvas( MousePosition );
		OpenMenu( menu => BuildContextMenu( menu, node, position ) );
	}

	/// <summary>
	/// Open a menu under the mouse, filled by <paramref name="build"/>, closing any this graph has
	/// open. Nothing opens when it adds no options.
	/// </summary>
	protected void OpenMenu( Action<Menu> build )
	{
		_menu?.Delete( true );

		var menu = _menu = new Menu();
		build( menu );

		if ( !menu.HasOptions )
		{
			_menu = null;
			menu.Delete( true );
			return;
		}

		menu.Closed += _ =>
		{
			if ( _menu == menu ) _menu = null;
			menu.Delete( true );
		};

		menu.Open( this, Popup.PositionMode.UnderMouse );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( Graph is null ) return;

		OpenContextMenu( null );
		e.StopPropagation();
	}
}
