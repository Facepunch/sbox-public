namespace Sandbox.UI.NodeGraph;

//
// Dragging moves the selection; right-clicks and double-clicks go to the graph and the node.
//
public partial class NodePanel
{
	Dictionary<NodePanel, Vector2> _dragStart;
	bool _pushedMove;

	/// <summary>
	/// How far the drag has gone, in canvas units, as last applied.
	/// </summary>
	Vector2 _dragDelta;

	/// <summary>
	/// The view scrolled by <paramref name="shift"/> canvas units under a mouse that didn't move.
	/// The drag only updates when the mouse moves, so carry on with it from here - otherwise the
	/// node would stay behind while the view scrolls, then leap the whole way on the next move.
	/// </summary>
	internal void FollowPan( Vector2 shift )
	{
		if ( !IsDragging ) return;

		_dragDelta += shift;
		OnDragMove( _dragDelta );
	}

	protected override bool OnDragStart( MousePanelEvent e )
	{
		Graph.PressNode( this, e.HasCtrl, e.HasShift );

		_dragStart = Graph.SelectedNodes.ToDictionary( x => x, x => x.Position );
		_pushedMove = false;
		return Selected;
	}

	protected override void OnDragMove( Vector2 delta )
	{
		foreach ( var (node, start) in _dragStart )
		{
			if ( !node.IsValid() ) continue;

			var position = (start + delta + node.Origin).SnapToGrid( Graph.GridSize ) - node.Origin;

			// Once per drag, before the first change: comments write their members as they move
			if ( !_pushedMove && position != node.Position )
			{
				_pushedMove = true;
				Graph.PushUndo( "Move Item" );
			}

			node.MoveTo( position );
		}

		Graph.UpdateCommentDrop();
	}

	protected override void OnDragFinish( bool cancelled )
	{
		foreach ( var (node, start) in _dragStart )
		{
			if ( !node.IsValid() ) continue;
			if ( cancelled ) node.MoveTo( start );
			node.CommitPosition();
		}

		_dragStart = null;
		Graph.FinishCommentDrop( cancelled );

		// Even when cancelled: the undo step is open, and every PushUndo gets its PushRedo
		if ( _pushedMove )
		{
			_pushedMove = false;
			Graph.PushRedo();
		}
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( IsPreview ) return;

		Graph.OpenContextMenu( this );
		e.StopPropagation();
	}

	/// <summary>
	/// Hands the double-click to the node; a plug's double-click gets here only if the plug left it.
	/// </summary>
	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( IsPreview ) return;

		Node.OnDoubleClick( this, e );
	}

	/// <summary>
	/// The plug whose row contains <paramref name="canvasPosition"/>, in canvas units: inputs
	/// first, then outputs.
	/// </summary>
	protected internal virtual PlugPanel GetPlugAt( Vector2 canvasPosition )
	{
		foreach ( var plug in Inputs )
		{
			if ( new Rect( Position + plug.Position, plug.Size ).IsInside( canvasPosition ) ) return plug;
		}

		foreach ( var plug in Outputs )
		{
			if ( new Rect( Position + plug.Position, plug.Size ).IsInside( canvasPosition ) ) return plug;
		}

		return null;
	}
}
