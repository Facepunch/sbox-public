namespace Sandbox.UI.NodeGraph;

//
// Comments: nodes join one by being dropped inside it and leave through the right-click menu.
// CommentPanel moves its members and fits itself around them.
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
	/// The comment holding <paramref name="node"/>, or null.
	/// </summary>
	public CommentPanel FindComment( NodePanel node )
	{
		if ( node is CommentPanel ) return null;

		return Comments.FirstOrDefault( x => x.Comment.Members.Contains( node.Node.Identifier ) );
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
		comment.Members.AddRange( members.Where( x => x is not CommentPanel ).Select( x => x.Node.Identifier ) );

		var panel = AddNode( comment );
		SelectNode( panel );

		return panel as CommentPanel;
	}

	/// <summary>
	/// Put <paramref name="nodes"/> in <paramref name="comment"/>, out of any comment they were in.
	/// </summary>
	public void AddToComment( CommentPanel comment, IEnumerable<NodePanel> nodes )
	{
		foreach ( var node in nodes.ToArray() )
		{
			if ( node is CommentPanel ) continue;

			FindComment( node )?.Comment.Members.Remove( node.Node.Identifier );
			comment.Comment.Members.Add( node.Node.Identifier );
		}
	}

	public void RemoveFromComment( IEnumerable<NodePanel> nodes )
	{
		foreach ( var node in nodes.ToArray() )
		{
			FindComment( node )?.Comment.Members.Remove( node.Node.Identifier );
		}
	}

	CommentPanel _dropTarget;

	/// <summary>
	/// Selected nodes that would join a comment if dropped in one: not comments, and in none yet.
	/// </summary>
	IEnumerable<NodePanel> FreeSelectedNodes() => SelectedNodes.Where( x => x is not CommentPanel && FindComment( x ) is null );

	/// <summary>
	/// While nodes are dragged, highlight the comment they'd join: the smallest one under the
	/// mouse that isn't moving with them.
	/// </summary>
	internal void UpdateCommentDrop()
	{
		var mouse = ScreenToCanvas( MousePosition );
		var target = FreeSelectedNodes().Any()
			? Comments.Where( x => !x.Selected && new Rect( x.Position, x.Size ).IsInside( mouse ) ).MinBy( x => x.Size.x * x.Size.y )
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
	/// Fill the right-click menu. <paramref name="node"/> is the node clicked, already selected,
	/// or null for empty space; <paramref name="position"/> is where, in canvas units.
	/// </summary>
	protected virtual void BuildContextMenu( Menu menu, NodePanel node, Vector2 position )
	{
		var members = SelectedNodes.Where( x => FindComment( x ) is not null ).ToArray();

		if ( members.Length > 0 )
		{
			menu.AddOption( "Remove from comment", "playlist_remove", () => RemoveFromComment( members ) );
		}

		if ( CommentFactory is null ) return;

		var selected = SelectedNodes.Where( x => x is not CommentPanel ).ToArray();
		menu.AddOption( "Add Comment", "notes", () => CreateComment( position, [] ) );

		if ( selected.Length > 0 )
		{
			menu.AddOption( $"Add Comment for {selected.Length} {(selected.Length == 1 ? "node" : "nodes")}", "notes", () => CreateComment( position, selected ) );
		}
	}

	/// <summary>
	/// Right-clicking a node selects it, then opens the menu for it.
	/// </summary>
	internal void OpenContextMenu( NodePanel node )
	{
		if ( node is not null && !node.Selected ) SelectNode( node );

		_menu?.Delete( true );

		var menu = _menu = new Menu();
		BuildContextMenu( menu, node, ScreenToCanvas( MousePosition ) );

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
