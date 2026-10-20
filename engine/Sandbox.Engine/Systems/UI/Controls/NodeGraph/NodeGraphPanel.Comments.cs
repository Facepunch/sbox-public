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
}
