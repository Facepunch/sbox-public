namespace Sandbox.UI.NodeGraph;

//
// Resizing from the edges and corners, never smaller than the title or the members need. Comments
// inside that sit snug against a dragged edge stretch with it, so nested comments stay nested.
//
public partial class CommentPanel
{
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

	/// <summary>
	/// A comment inside this one that stretches with the edges being dragged.
	/// </summary>
	record struct Follower( CommentPanel Comment, Edge Edges, Vector2 StartMin, Vector2 StartMax, List<Follower> Followers );

	List<Follower> _followers;

	/// <summary>
	/// The comments holding this one, innermost first, whose edges sit snug against the edges being
	/// dragged: they move with them, so shrinking this one shrinks them too where they're empty.
	/// </summary>
	List<Follower> _leaders;

	/// <summary>
	/// Being stretched by another comment's resize, so it mustn't snap back to its members.
	/// </summary>
	bool _following;

	/// <summary>
	/// Which edges of <paramref name="inner"/> sit snug against <paramref name="outer"/>'s: no more
	/// than the padding (and a grid step of rounding) away, as they are when the outer one hugs it.
	/// </summary>
	static Edge SnugEdges( CommentPanel outer, CommentPanel inner )
	{
		var slack = outer.Graph.GridSize;
		var edges = Edge.None;

		if ( inner.Position.x - outer.Position.x <= Padding + slack ) edges |= Edge.Left;
		if ( outer.Position.x + outer.Size.x - (inner.Position.x + inner.Size.x) <= Padding + slack ) edges |= Edge.Right;
		if ( inner.Position.y - outer.Position.y <= Padding + HeaderHeight + slack ) edges |= Edge.Top;
		if ( outer.Position.y + outer.Size.y - (inner.Position.y + inner.Size.y) <= Padding + slack ) edges |= Edge.Bottom;

		return edges;
	}

	/// <summary>
	/// The comments inside this one sitting snug against any of <paramref name="edges"/>.
	/// </summary>
	List<Follower> FindFollowers( Edge edges )
	{
		var followers = new List<Follower>();

		foreach ( var node in Graph.Nodes )
		{
			if ( node is not CommentPanel inner || !IsMember( inner ) ) continue;

			var stuck = SnugEdges( this, inner ) & edges;
			if ( stuck == Edge.None ) continue;

			inner.RefreshMemberIds();
			followers.Add( new Follower( inner, stuck, inner.Position, inner.Position + inner.Size, inner.FindFollowers( stuck ) ) );
		}

		return followers;
	}

	/// <summary>
	/// The comments holding this one that are snug against any of <paramref name="edges"/>, outward
	/// until one isn't.
	/// </summary>
	List<Follower> FindLeaders( Edge edges )
	{
		var leaders = new List<Follower>();
		var inner = this;

		// Bounded, so a cycle written into a graph file can't hang this
		for ( var outer = Graph.FindComment( this ); outer is not null && leaders.Count < Graph.Nodes.Count; outer = Graph.FindComment( outer ) )
		{
			edges &= SnugEdges( outer, inner );
			if ( edges == Edge.None ) break;

			outer.RefreshMemberIds();
			leaders.Add( new Follower( outer, edges, outer.Position, outer.Position + outer.Size, null ) );
			inner = outer;
		}

		return leaders;
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
		_followers = FindFollowers( _resizing );
		_leaders = FindLeaders( _resizing );
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
		var startMin = _resizeStartPosition;
		var startMax = _resizeStartPosition + _resizeStartSize;
		var min = startMin;
		var max = startMax;

		if ( _resizing.HasFlag( Edge.Left ) ) min.x = Snap( min.x + delta.x, grid );
		if ( _resizing.HasFlag( Edge.Right ) ) max.x = Snap( max.x + delta.x, grid );
		if ( _resizing.HasFlag( Edge.Top ) ) min.y = Snap( min.y + delta.y, grid );
		if ( _resizing.HasFlag( Edge.Bottom ) ) max.y = Snap( max.y + delta.y, grid );

		ResizeEdges( _resizing, startMin, startMax, min - startMin, max - startMax, _followers );

		// The holders' edges go where this one's did, after its clamp. Each holder's own clamp keeps
		// its other members inside, so it only shrinks into space that's empty.
		var minDelta = Position - startMin;
		var maxDelta = Position + Size - startMax;

		foreach ( var leader in _leaders ?? [] )
		{
			leader.Comment._following = true;
			leader.Comment.ResizeEdges( leader.Edges, leader.StartMin, leader.StartMax, minDelta, maxDelta, null );
		}
	}

	/// <summary>
	/// Move <paramref name="edges"/> of the box that started at <paramref name="startMin"/> and
	/// <paramref name="startMax"/> by the deltas, stretching the followers first so the clamp below
	/// sees their new size. Never below the minimum, and never cutting a member off.
	/// </summary>
	void ResizeEdges( Edge edges, Vector2 startMin, Vector2 startMax, Vector2 minDelta, Vector2 maxDelta, List<Follower> followers )
	{
		var min = startMin;
		var max = startMax;

		if ( edges.HasFlag( Edge.Left ) ) min.x += minDelta.x;
		if ( edges.HasFlag( Edge.Right ) ) max.x += maxDelta.x;
		if ( edges.HasFlag( Edge.Top ) ) min.y += minDelta.y;
		if ( edges.HasFlag( Edge.Bottom ) ) max.y += maxDelta.y;

		foreach ( var follower in followers ?? [] )
		{
			follower.Comment._following = true;
			follower.Comment.ResizeEdges( follower.Edges, follower.StartMin, follower.StartMax, minDelta, maxDelta, follower.Followers );
		}

		var content = ContentBounds();
		if ( edges.HasFlag( Edge.Left ) ) min.x = MathF.Min( min.x, MathF.Min( max.x - MinimumSize.x, content?.Min.x ?? float.MaxValue ) );
		if ( edges.HasFlag( Edge.Right ) ) max.x = MathF.Max( max.x, MathF.Max( min.x + MinimumSize.x, content?.Max.x ?? float.MinValue ) );
		if ( edges.HasFlag( Edge.Top ) ) min.y = MathF.Min( min.y, MathF.Min( max.y - MinimumSize.y, content?.Min.y ?? float.MaxValue ) );
		if ( edges.HasFlag( Edge.Bottom ) ) max.y = MathF.Max( max.y, MathF.Max( min.y + MinimumSize.y, content?.Max.y ?? float.MinValue ) );

		SetBounds( min, max, commit: false );
	}

	/// <summary>
	/// Write the followers' new boxes back, or put them back where they were. A follower that
	/// changed is sized by hand from now on, so it keeps the size it was stretched to.
	/// </summary>
	static void FinishFollowers( List<Follower> followers, bool cancelled )
	{
		foreach ( var follower in followers ?? [] )
		{
			FinishFollowers( follower.Followers, cancelled );

			var comment = follower.Comment;
			comment._following = false;

			(Vector2 Min, Vector2 Max) start = (follower.StartMin, follower.StartMax);
			var bounds = cancelled ? start : comment.CurrentBounds;
			if ( !cancelled && bounds != start ) comment._ownBounds = bounds;

			comment.SetBounds( bounds.Min, bounds.Max );
		}
	}

	protected override void OnDragFinish( bool cancelled )
	{
		if ( _resizing == Edge.None )
		{
			base.OnDragFinish( cancelled );
			return;
		}

		_resizing = Edge.None;

		var followers = _followers;
		var leaders = _leaders;
		_followers = null;
		_leaders = null;

		(Vector2 Min, Vector2 Max) start = (_resizeStartPosition, _resizeStartPosition + _resizeStartSize);
		var bounds = cancelled ? start : CurrentBounds;

		// A press on the edge that didn't move it (a click to select) mustn't freeze the comment at
		// whatever size its members had pushed it to
		if ( !cancelled && bounds != start )
		{
			_ownBounds = bounds;

			using var undoScope = Graph.UndoScope( "Resize Comment" );
			FinishFollowers( followers, false );
			FinishFollowers( leaders, false );
			SetBounds( bounds.Min, bounds.Max );
			return;
		}

		FinishFollowers( followers, true );
		FinishFollowers( leaders, true );
		SetBounds( bounds.Min, bounds.Max );
	}

	static float Snap( float value, float grid ) => MathF.Round( value / grid ) * grid;
}
