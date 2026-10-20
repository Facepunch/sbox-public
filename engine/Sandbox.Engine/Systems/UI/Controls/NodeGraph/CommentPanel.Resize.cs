namespace Sandbox.UI.NodeGraph;

//
// Resizing from the edges and corners, never smaller than the title or the members need.
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

		(Vector2 Min, Vector2 Max) start = (_resizeStartPosition, _resizeStartPosition + _resizeStartSize);
		var bounds = cancelled ? start : CurrentBounds;

		// A press on the edge that didn't move it (a click to select) mustn't freeze the comment at
		// whatever size its members had pushed it to
		if ( !cancelled && bounds != start )
		{
			_ownBounds = bounds;

			using var undoScope = Graph.UndoScope( "Resize Comment" );
			SetBounds( bounds.Min, bounds.Max );
			return;
		}

		SetBounds( bounds.Min, bounds.Max );
	}

	static float Snap( float value, float grid ) => MathF.Round( value / grid ) * grid;
}
