namespace Sandbox.UI.NodeGraph;

public abstract partial class PlugPanel
{
	bool _pressed;

	/// <summary>
	/// Press on a connected input to pull its wire off; drag to connect to a compatible plug.
	/// Ctrl or Shift presses fall through to the node.
	/// </summary>
	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.Button != "mouseleft" || e.HasCtrl || e.HasShift )
		{
			base.OnMouseDown( e );
			return;
		}

		_pressed = true;
		Node.Graph.PlugPressed( this );
		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( !_pressed )
		{
			base.OnMouseMove( e );
			return;
		}

		Node.Graph.PlugMoved( this );
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( !_pressed || e.Button != "mouseleft" )
		{
			base.OnMouseUp( e );
			return;
		}

		_pressed = false;
		Node.Graph.PlugReleased( this );
		e.StopPropagation();
	}

	/// <summary>
	/// The pointer while a wire is being dragged from this plug.
	/// </summary>
	internal void SetDragCursor( bool dragging )
	{
		Style.Cursor = dragging ? "crosshair" : "pointer";

		if ( !dragging ) _pressed = false;
	}
}
