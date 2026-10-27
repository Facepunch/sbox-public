namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A comment in a <see cref="NodeGraphPanel"/>: a titled box drawn behind the nodes it holds.
/// Moving it moves its members. It hugs them, growing and shrinking as they move, until it's sized
/// by hand: then it keeps that box plus any member past its edge, so circling one never leaves it
/// bigger. Drag an edge or corner to resize it - never smaller than its members need.
/// <see cref="FitToContent"/> goes back to hugging them.
/// </summary>
public partial class CommentPanel : NodePanel
{
	/// <summary>
	/// Space between the members and the edges. Grid multiples keep a fitted comment on the grid.
	/// </summary>
	const float Padding = 24f;

	const float HeaderHeight = 36f;

	/// <summary>
	/// As small as the comment can get: wide enough for its title, tall enough for the header and a
	/// strip of body. Anything a fixed size would cover still has room to shrink into.
	/// </summary>
	Vector2 MinimumSize
	{
		get
		{
			var title = Comment.Title ?? "";
			if ( title != _measuredTitle )
			{
				_measuredTitle = title;
				_titleWidth = NodePaint.Measure( title, 10f, 600 ).x;
			}

			var icon = string.IsNullOrEmpty( Comment.DisplayInfo.Icon ) ? 0f : 24f;
			return new Vector2( MathF.Max( _titleWidth + icon + 24f, 48f ), HeaderHeight + Padding );
		}
	}

	string _measuredTitle;
	float _titleWidth;

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
	/// The box the comment was sized to by hand, or null when it hasn't been (or was fitted since):
	/// then it hugs its members, shrinking as they do. A hand-sized comment shows this box plus
	/// whatever members are past it, never more.
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
			if ( _ownBounds is { } own ) _ownBounds = (own.Min + delta, own.Max + delta);

			foreach ( var member in Graph.Nodes )
			{
				if ( member.Selected || !IsMember( member ) ) continue;

				member.MoveTo( member.Position + delta );
				member.CommitPosition();
			}
		}
		else if ( !_following && !(Selected && dragging) )
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
	/// Cover the members: around them alone, or the hand-sized box plus whatever members are past it.
	/// A comment holding nothing but comments always fits around them - its empty space would only
	/// be padding around padding - so nested comments sit one padding inside each other.
	/// Mid-drag that's on screen only, so the node isn't rewritten every frame; once the drag ends
	/// it's written back.
	/// </summary>
	void Grow( bool dragging )
	{
		if ( ContentBounds() is not { } content ) return;

		var before = CurrentBounds;
		var own = HoldsOnlyComments() ? content : _ownBounds ?? content;
		SetBounds( Vector2.Min( own.Min, content.Min ), Vector2.Max( own.Max, content.Max ), commit: !dragging );

		// Outer comments tick first, so without this each level would trail the one inside it by a
		// frame and nested comments would wobble apart instead of moving as one
		if ( CurrentBounds != before && Graph.FindComment( this ) is { _following: false } outer && !(outer.Selected && dragging) )
		{
			outer.RefreshMemberIds();
			outer.Grow( dragging );
		}
	}

	/// <summary>
	/// Grow around the members and write that to the node now, rather than on the next tick. Not
	/// while selected: it moved with the drag, and its members only catch up on its next tick.
	/// </summary>
	internal void CommitGrowth()
	{
		if ( IsPreview || _following || Selected ) return;

		RefreshMemberIds();
		Grow( false );
	}

	/// <summary>
	/// Whether every member is a comment. False when it holds none.
	/// </summary>
	bool HoldsOnlyComments()
	{
		var any = false;

		foreach ( var node in Graph.Nodes )
		{
			if ( !IsMember( node ) ) continue;
			if ( node is not CommentPanel ) return false;
			any = true;
		}

		return any;
	}

	/// <summary>
	/// Shrink or grow the comment to sit snugly around its members. Does nothing when it holds none.
	/// </summary>
	public void FitToContent()
	{
		RefreshMemberIds();
		if ( ContentBounds() is not { } content ) return;

		_ownBounds = null;
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

	static Vector2 Floor( Vector2 value, float grid ) => new( MathF.Floor( value.x / grid ) * grid, MathF.Floor( value.y / grid ) * grid );

	static Vector2 Ceiling( Vector2 value, float grid ) => new( MathF.Ceiling( value.x / grid ) * grid, MathF.Ceiling( value.y / grid ) * grid );

	protected override void Layout()
	{
		Size = Vector2.Max( Comment.Size, MinimumSize );
		Style.Width = Size.x;
		Style.Height = Size.y;
	}

	protected override void OnRebuild()
	{
		base.OnRebuild();
		Layout();
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

		var titleRect = header.Shrink( 12f, 0f );

		if ( !string.IsNullOrEmpty( comment.DisplayInfo.Icon ) )
		{
			paint.DrawIcon( titleRect, comment.DisplayInfo.Icon, 19f, TextFlag.LeftCenter );
			titleRect = titleRect.Shrink( 24f, 0f, 0f, 0f );
		}

		paint.DrawText( titleRect, comment.Title, TextFlag.LeftCenter );

		if ( !string.IsNullOrEmpty( comment.Description ) )
		{
			var body = new Rect( 0f, HeaderHeight, rect.Width, rect.Height - HeaderHeight ).Shrink( 12f, 8f );
			var description = comment.Description.Length > 300 ? comment.Description[..300] : comment.Description;

			paint.SetDefaultFont( comment.DescriptionFontSize.Clamp( 8, 64 ) * 0.75f );
			paint.SetPen( Color.White.WithAlpha( 0.7f ) );
			paint.DrawText( body, description, TextFlag.LeftTop | TextFlag.WordWrap );
		}
	}
}
