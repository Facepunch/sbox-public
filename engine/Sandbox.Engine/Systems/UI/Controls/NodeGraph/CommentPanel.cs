namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A comment in a <see cref="NodeGraphPanel"/>: a titled box drawn behind the nodes it holds.
/// Moving it moves its members, and it fits itself around them. An empty comment keeps its own size.
/// </summary>
public class CommentPanel : NodePanel
{
	/// <summary>
	/// Space between the members and the edges. Grid multiples keep a fitted comment on the grid.
	/// </summary>
	const float Padding = 24f;

	const float HeaderHeight = 36f;

	static readonly Vector2 MinimumSize = new( 120f, 96f );

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
	/// Whether nested comments have been put in order since this one was added - not possible
	/// until the graph knows every comment, so it happens on the first tick.
	/// </summary>
	bool _ordered;

	/// <summary>
	/// The panels of this comment's members.
	/// </summary>
	public IEnumerable<NodePanel> MemberPanels()
	{
		var members = Comment.Members;
		if ( members is null || members.Count == 0 ) return [];

		return Graph.Nodes.Where( x => x != this && members.Contains( x.Node.Identifier ) );
	}

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

		if ( _lastPosition is { } last && Position != last )
		{
			// Something moved the comment: bring the members along. Selected ones are already
			// moving with the drag.
			var delta = Position - last;

			foreach ( var member in MemberPanels() )
			{
				if ( member.Selected ) continue;

				member.MoveTo( member.Position + delta );
				member.CommitPosition();
			}
		}
		else if ( !(Selected && Graph.Nodes.Any( x => x.IsDragging )) )
		{
			Fit();
		}

		_lastPosition = Position;
	}

	void ForgetRemovedMembers()
	{
		var members = Comment.Members;
		if ( members is null || members.Count == 0 || Graph.Graph is not { } graph ) return;

		members.RemoveAll( id => !graph.Nodes.Any( x => x.Identifier == id ) );
	}

	/// <summary>
	/// Put the comment around its members.
	/// </summary>
	void Fit()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );
		var any = false;

		foreach ( var member in MemberPanels() )
		{
			min = Vector2.Min( min, member.Position );
			max = Vector2.Max( max, member.Position + member.Size );
			any = true;
		}

		if ( !any ) return;

		var grid = Graph.GridSize;
		var position = Floor( min - new Vector2( Padding, Padding + HeaderHeight ), grid );
		var size = Vector2.Max( Ceiling( max + Padding - position, grid ), MinimumSize );

		if ( position != Position )
		{
			MoveTo( position );
			CommitPosition();
		}

		if ( size != Comment.Size )
		{
			Comment.Size = size;
			Layout();
		}
	}

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
		paint.SetPen( Selected ? NodeTheme.Accent : color.WithAlpha( IsDropTarget ? 0.9f : HasHovered ? 0.6f : 0.35f ), Selected || IsDropTarget ? 2f : 1f );
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
