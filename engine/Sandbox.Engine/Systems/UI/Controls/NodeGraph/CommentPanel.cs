namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A comment in a <see cref="NodeGraphPanel"/>: a titled box drawn behind the nodes it holds.
/// Moving it moves its members. It covers its own box plus any member past its edge, following a
/// dragged member out and back in, so circling one never leaves it bigger. Drag an edge or corner to
/// resize it - never smaller than its members need. <see cref="FitToContent"/> shrinks it back
/// around them.
/// </summary>
public class CommentPanel : NodePanel
{
	/// <summary>
	/// Space between the members and the edges. Grid multiples keep a fitted comment on the grid.
	/// </summary>
	const float Padding = 24f;

	const float HeaderHeight = 36f;

	static readonly Vector2 MinimumSize = new( 120f, 96f );

	/// <summary>
	/// How far in from an edge a press resizes rather than moves, in canvas units.
	/// </summary>
	const float EdgeGrab = 8f;

	[Flags]
	enum Edge
	{
		None = 0,
		Left = 1,
		Top = 2,
		Right = 4,
		Bottom = 8,
	}

	/// <summary>
	/// The edges being dragged, or None while the drag moves the comment.
	/// </summary>
	Edge _resizing;
	Vector2 _resizeStartPosition;
	Vector2 _resizeStartSize;
	string _cursor;

	public ICommentNode Comment => (ICommentNode)Node;

	/// <summary>
	/// Highlighted while dragged nodes would join this comment if dropped now.
	/// </summary>
	public bool IsDropTarget { get; set; }

	protected override float TitleHeight => HeaderHeight;

	/// <summary>
	/// Where the comment was last tick, to tell when something else moved it.
	/// </summary>
	Vector2? _lastPosition;

	/// <summary>
	/// The box the comment has of its own - as loaded, created, resized or fitted. It always shows
	/// this plus whatever members are past it, never more.
	/// </summary>
	(Vector2 Min, Vector2 Max)? _ownBounds;

	/// <summary>
	/// The box as shown, which mid-drag can be ahead of what's written to the node.
	/// </summary>
	(Vector2 Min, Vector2 Max) CurrentBounds => (Position, Position + Size);

	public CommentPanel( NodeGraphPanel graph, INode node, bool isPreview = false ) : base( graph, node, isPreview )
	{
		AddClass( "comment" );
	}

	/// <summary>
	/// Sit behind the nodes by going first among them. A negative z-index would too, but once the
	/// render order goes below zero the mouse goes through to the canvas.
	/// </summary>
	public override void OnParentChanged()
	{
		base.OnParentChanged();

		if ( Parent is { } parent ) parent.SetChildIndex( this, 0 );
		_ordered = false;
	}

	/// <summary>
	/// Comments stay behind the nodes even while dragged or focused.
	/// </summary>
	protected override bool RaisesOnInteraction => false;

	/// <summary>
	/// Whether nested comments have been put in order since this one was added - not possible
	/// until the graph knows every comment, so it happens on the first tick.
	/// </summary>
	bool _ordered;

	/// <summary>
	/// The panels of this comment's members.
	/// </summary>
	public IEnumerable<NodePanel> MemberPanels()
	{
		RefreshMemberIds();
		return _memberIds.Count == 0 ? [] : Graph.Nodes.Where( IsMember );
	}

	/// <summary>
	/// <see cref="ICommentNode.Members"/> as a set, refilled each tick so membership checks don't
	/// scan the list for every node.
	/// </summary>
	readonly HashSet<string> _memberIds = new();

	void RefreshMemberIds()
	{
		_memberIds.Clear();
		if ( Comment.Members is { } members ) _memberIds.UnionWith( members );
	}

	bool IsMember( NodePanel node ) => node != this && _memberIds.Contains( node.Node.Identifier );

	/// <summary>
	/// How many nodes the graph showed when removed members were last forgotten.
	/// </summary>
	int _knownNodeCount = -1;

	public override void Tick()
	{
		base.Tick();

		if ( IsPreview ) return;

		if ( !_ordered )
		{
			_ordered = true;
			Graph.OrderComments();
		}

		ForgetRemovedMembers();
		RefreshMemberIds();

		_ownBounds ??= CurrentBounds;

		var dragging = false;
		foreach ( var node in Graph.Nodes )
		{
			if ( !node.IsDragging ) continue;
			dragging = true;
			break;
		}

		if ( _lastPosition is { } last && Position != last )
		{
			// Something moved the comment: bring the members along. Selected ones are already
			// moving with the drag.
			var delta = Position - last;
			_ownBounds = (_ownBounds.Value.Min + delta, _ownBounds.Value.Max + delta);

			foreach ( var member in Graph.Nodes )
			{
				if ( member.Selected || !IsMember( member ) ) continue;

				member.MoveTo( member.Position + delta );
				member.CommitPosition();
			}
		}
		else if ( !(Selected && dragging) )
		{
			Grow( dragging );
		}

		_lastPosition = Position;

		UpdateCursor();
	}

	/// <summary>
	/// Drop members whose nodes are gone. Only when the graph's node count changes - a removal is
	/// the only way one goes.
	/// </summary>
	void ForgetRemovedMembers()
	{
		if ( Graph.Nodes.Count == _knownNodeCount ) return;
		_knownNodeCount = Graph.Nodes.Count;

		var members = Comment.Members;
		if ( members is null || members.Count == 0 || Graph.Graph is not { } graph ) return;

		var ids = new HashSet<string>( graph.Nodes.Select( x => x.Identifier ) );
		members.RemoveAll( id => !ids.Contains( id ) );
	}

	/// <summary>
	/// The box the members need, padded and on the grid. Null when the comment holds nothing.
	/// </summary>
	(Vector2 Min, Vector2 Max)? ContentBounds()
	{
		if ( _memberIds.Count == 0 ) return null;

		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		var any = false;

		foreach ( var member in Graph.Nodes )
		{
			if ( !IsMember( member ) ) continue;

			min = Vector2.Min( min, member.Position );
			max = Vector2.Max( max, member.Position + member.Size );
			any = true;
		}

		if ( !any ) return null;

		var grid = Graph.GridSize;
		return (Floor( min - new Vector2( Padding, Padding + HeaderHeight ), grid ), Ceiling( max + Padding, grid ));
	}

	/// <summary>
	/// Cover the comment's own box and whatever members are past it. Mid-drag that's on screen only,
	/// so the node isn't rewritten every frame; once the drag ends it's written back.
	/// </summary>
	void Grow( bool dragging )
	{
		if ( ContentBounds() is not { } content || _ownBounds is not { } own ) return;

		SetBounds( Vector2.Min( own.Min, content.Min ), Vector2.Max( own.Max, content.Max ), commit: !dragging );
	}

	/// <summary>
	/// Shrink or grow the comment to sit snugly around its members. Does nothing when it holds none.
	/// </summary>
	public void FitToContent()
	{
		RefreshMemberIds();
		if ( ContentBounds() is not { } content ) return;

		_ownBounds = content;
		SetBounds( content.Min, content.Max );
	}

	/// <summary>
	/// Put the comment's corners at <paramref name="min"/> and <paramref name="max"/>. With
	/// <paramref name="commit"/> off it only changes on screen, so a drag doesn't write the node
	/// every frame; committing writes back whatever differs from the node.
	/// </summary>
	void SetBounds( Vector2 min, Vector2 max, bool commit = true )
	{
		var size = Vector2.Max( max - min, MinimumSize );

		if ( min != Position )
		{
			MoveTo( min );
			_lastPosition = Position;
		}

		if ( size != Size )
		{
			Size = size;
			Style.Width = size.x;
			Style.Height = size.y;
		}

		if ( !commit ) return;

		if ( !Equals( Node.Position, Position ) )
		{
			CommitPosition();
			_lastPosition = Position;
		}

		if ( Comment.Size != size ) Comment.Size = size;
	}

	/// <summary>
	/// The edges under the mouse, if it's close enough to any to resize.
	/// </summary>
	Edge EdgeUnderMouse()
	{
		var local = Graph.ScreenToCanvas( Graph.MousePosition ) - Position;
		var edge = Edge.None;

		if ( local.x < EdgeGrab ) edge |= Edge.Left;
		else if ( local.x > Size.x - EdgeGrab ) edge |= Edge.Right;

		if ( local.y < EdgeGrab ) edge |= Edge.Top;
		else if ( local.y > Size.y - EdgeGrab ) edge |= Edge.Bottom;

		return edge;
	}

	void UpdateCursor()
	{
		var edge = _resizing != Edge.None ? _resizing : HasHovered && !IsDragging ? EdgeUnderMouse() : Edge.None;

		var cursor = edge switch
		{
			Edge.Left | Edge.Top or Edge.Right | Edge.Bottom => "nwse-resize",
			Edge.Right | Edge.Top or Edge.Left | Edge.Bottom => "nesw-resize",
			Edge.Left or Edge.Right => "ew-resize",
			Edge.Top or Edge.Bottom => "ns-resize",
			_ => null,
		};

		if ( cursor == _cursor ) return;
		_cursor = cursor;
		Style.Cursor = cursor;
	}

	protected override bool OnDragStart( MousePanelEvent e )
	{
		_resizing = EdgeUnderMouse();
		if ( _resizing == Edge.None ) return base.OnDragStart( e );

		Graph.PressNode( this, e.HasCtrl, e.HasShift );
		_resizeStartPosition = Position;
		_resizeStartSize = Size;
		return true;
	}

	protected override void OnDragMove( Vector2 delta )
	{
		if ( _resizing == Edge.None )
		{
			base.OnDragMove( delta );
			return;
		}

		var grid = Graph.GridSize;
		var min = _resizeStartPosition;
		var max = _resizeStartPosition + _resizeStartSize;

		if ( _resizing.HasFlag( Edge.Left ) ) min.x = Snap( min.x + delta.x, grid );
		if ( _resizing.HasFlag( Edge.Right ) ) max.x = Snap( max.x + delta.x, grid );
		if ( _resizing.HasFlag( Edge.Top ) ) min.y = Snap( min.y + delta.y, grid );
		if ( _resizing.HasFlag( Edge.Bottom ) ) max.y = Snap( max.y + delta.y, grid );

		// Never below the minimum, and never cutting a member off
		var content = ContentBounds();
		if ( _resizing.HasFlag( Edge.Left ) ) min.x = MathF.Min( min.x, MathF.Min( max.x - MinimumSize.x, content?.Min.x ?? float.MaxValue ) );
		if ( _resizing.HasFlag( Edge.Right ) ) max.x = MathF.Max( max.x, MathF.Max( min.x + MinimumSize.x, content?.Max.x ?? float.MinValue ) );
		if ( _resizing.HasFlag( Edge.Top ) ) min.y = MathF.Min( min.y, MathF.Min( max.y - MinimumSize.y, content?.Min.y ?? float.MaxValue ) );
		if ( _resizing.HasFlag( Edge.Bottom ) ) max.y = MathF.Max( max.y, MathF.Max( min.y + MinimumSize.y, content?.Max.y ?? float.MinValue ) );

		SetBounds( min, max, commit: false );
	}

	protected override void OnDragFinish( bool cancelled )
	{
		if ( _resizing == Edge.None )
		{
			base.OnDragFinish( cancelled );
			return;
		}

		_resizing = Edge.None;

		(Vector2 Min, Vector2 Max) bounds = cancelled ? (_resizeStartPosition, _resizeStartPosition + _resizeStartSize) : CurrentBounds;
		if ( !cancelled ) _ownBounds = bounds;
		SetBounds( bounds.Min, bounds.Max );
	}

	static float Snap( float value, float grid ) => MathF.Round( value / grid ) * grid;

	static Vector2 Floor( Vector2 value, float grid ) => new( MathF.Floor( value.x / grid ) * grid, MathF.Floor( value.y / grid ) * grid );

	static Vector2 Ceiling( Vector2 value, float grid ) => new( MathF.Ceiling( value.x / grid ) * grid, MathF.Ceiling( value.y / grid ) * grid );

	protected override void Layout()
	{
		Size = Vector2.Max( Comment.Size, MinimumSize );
		Style.Width = Size.x;
		Style.Height = Size.y;
	}

	public override void OnDraw( Painter painter )
	{
		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };
		var comment = Comment;
		var color = comment.Color;
		var rect = new Rect( 0f, Size );
		var radius = NodeTheme.Radius;

		if ( IsDropTarget )
		{
			paint.DrawShadow( rect, radius, color.WithAlpha( 0.5f ), 12f, default, 1f );
		}

		// Body, a faint tint of the comment colour
		paint.SetBrush( color.WithAlpha( 0.08f ) );
		paint.SetPen( Selected ? Graph.SelectionColor : color.WithAlpha( IsDropTarget ? 0.9f : HasHovered ? 0.6f : 0.35f ), Selected || IsDropTarget ? 2f : 1f );
		paint.DrawRect( rect, radius );

		// Header, rounded on top only. Opaque, so the square patch over its bottom corners doesn't
		// darken where it overlaps.
		var header = new Rect( 0f, 0f, rect.Width, HeaderHeight );
		paint.ClearPen();
		paint.SetBrush( Color.Lerp( NodeTheme.Canvas, color, 0.45f ).WithAlpha( 1f ) );
		paint.DrawRect( header, radius );
		paint.DrawRect( new Rect( 0f, HeaderHeight * 0.5f, rect.Width, HeaderHeight * 0.5f ) );

		paint.SetDefaultFont( 10f, 600 );
		paint.SetPen( Color.White );
		paint.DrawText( header.Shrink( 12f, 0f ), comment.Title, TextFlag.LeftCenter );

		if ( !string.IsNullOrEmpty( comment.Description ) )
		{
			var body = new Rect( 0f, HeaderHeight, rect.Width, rect.Height - HeaderHeight ).Shrink( 12f, 8f );

			paint.SetDefaultFont( comment.DescriptionFontSize.Clamp( 8, 64 ) * 0.75f );
			paint.SetPen( Color.White.WithAlpha( 0.7f ) );
			paint.DrawText( body, comment.Description, TextFlag.LeftTop | TextFlag.WordWrap );
		}
	}
}
