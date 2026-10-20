namespace Sandbox.UI.NodeGraph;

//
// Plug dragging, driven by PlugPanel's mouse handlers: a wire follows the mouse until it's dropped.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// The plug a dragged wire would connect to if released now.
	/// </summary>
	public PlugPanel DropTarget { get; private set; }

	/// <summary>
	/// The wire shown while dragging from a plug, and after a drop on empty space until the node
	/// menu it opened closes.
	/// </summary>
	public Connection Preview { get; private set; }

	PlugPanel _pressPlug;
	Connection _pressSource;
	bool _plugDragging;
	PlugPanel _draggingPlug;

	/// <summary>
	/// A plug was pressed. A connected input detaches its wire so it can be dragged somewhere else.
	/// </summary>
	internal void PlugPressed( PlugPanel plug )
	{
		// The graph takes keys from here on, so holding R while dropping the wire is heard
		Focus();

		_pressPlug = plug;
		_pressSource = null;
		_plugDragging = false;

		if ( plug is PlugInPanel { Inner.ConnectedOutput: { } output } input )
		{
			_pressSource = _connections.FirstOrDefault( x => x.Input == input && Equals( x.Output.Inner, output ) );
			input.Inner.ConnectedOutput = null;

			if ( _pressSource is not null )
			{
				_connections.Remove( _pressSource );
				_wiredInputs.Remove( input.Inner );
			}
		}
	}

	internal void PlugMoved( PlugPanel plug )
	{
		if ( plug != _pressPlug ) return;

		_plugDragging = true;

		if ( _pressSource is { } source )
		{
			DraggingPlug( source.Output, MouseCanvas, source );
			return;
		}

		DraggingPlug( plug, MouseCanvas, null );
	}

	internal void PlugReleased( PlugPanel plug )
	{
		if ( plug != _pressPlug ) return;

		var source = _pressSource;
		var dragging = _plugDragging;
		var position = MouseCanvas;

		ClearPlugPress();

		if ( source is not null )
		{
			DroppedPlug( source.Output, position, source );
		}
		else if ( dragging )
		{
			DroppedPlug( plug, position, null );
		}
	}

	void ClearPlugPress()
	{
		_pressPlug?.SetDragCursor( false );
		_pressPlug = null;
		_pressSource = null;
		_plugDragging = false;
	}

	/// <summary>
	/// Abandon a drag that lost the mouse: a detached wire goes back where it was.
	/// </summary>
	void CancelPlugDrag()
	{
		var source = _pressSource;

		ClearPlugPress();
		EndPreview();

		if ( source is not null ) Restore( source );
	}

	/// <summary>
	/// Drop the preview wire and the drop target.
	/// </summary>
	void EndPreview()
	{
		DropTarget = null;
		Preview = null;

		if ( _draggingPlug.IsValid() ) _draggingPlug.Dragging = false;
		_draggingPlug = null;
	}

	void Restore( Connection connection )
	{
		if ( !connection.Input.IsValid() || !connection.Output.IsValid() ) return;

		if ( connection.Input.Inner.ConnectedOutput is null )
		{
			connection.Input.Inner.ConnectedOutput = connection.Output.Inner;
		}

		if ( !_connections.Contains( connection ) )
		{
			_connections.Add( connection );
			_wiredInputs.Add( connection.Input.Inner );
		}

		connection.Dragging = false;
	}

	void DraggingPlug( PlugPanel plug, Vector2 position, Connection source )
	{
		var target = GetPlugAt( position );

		target = plug is PlugInPanel ? target as PlugOutPanel : target as PlugInPanel;

		DropTarget = target?.Node != plug.Node ? target : null;

		Preview ??= new Connection( this, plug );
		Preview.LayoutForPreview( plug, position, DropTarget );

		if ( _draggingPlug != plug )
		{
			if ( _draggingPlug.IsValid() ) _draggingPlug.Dragging = false;
			_draggingPlug = plug;
			plug.Dragging = true;
		}

		if ( source is not null ) source.Dragging = true;
		plug.SetDragCursor( true );
	}

	/// <summary>
	/// A wire from <paramref name="plug"/> was let go at <paramref name="position"/>, in canvas
	/// units. <paramref name="source"/> is the wire it was pulled off, if any. On a plug it
	/// connects; on nothing it drops the wire it came from, or opens the node menu to make
	/// something to connect to - or, with R held, a reroute.
	/// </summary>
	internal void DroppedPlug( PlugPanel plug, Vector2 position, Connection source )
	{
		var disconnected = false;
		var connected = false;

		if ( source is not null )
		{
			source.Dragging = false;

			if ( source.Input == DropTarget )
			{
				Restore( source );
				EndPreview();
				return;
			}

			// A pressed input let go of its wire already: put it back, so the undo step starts
			// from the graph as it was
			Restore( source );
			PushUndo( DropTarget.IsValid() ? "Change Connection" : "Drop Connection" );

			disconnected = true;
			source.Disconnect();
		}

		if ( DropTarget.IsValid() && DropTarget.Node != plug.Node )
		{
			connected = true;

			if ( !disconnected ) PushUndo( "Create Connection" );

			var output = plug as PlugOutPanel ?? DropTarget as PlugOutPanel;
			var input = plug as PlugInPanel ?? DropTarget as PlugInPanel;

			RemoveConnections( input );
			CreateConnection( output, input );
		}

		if ( disconnected || connected ) PushRedo();

		DropTarget = null;

		if ( !disconnected && !connected && Graph is not null )
		{
			if ( _createReroute && RerouteNodeType is not null )
			{
				CreateNewNode( RerouteNodeType, position - new Vector2( ReroutePanel.Diameter * 0.5f ), plug, selected: false );
			}
			else
			{
				// The preview keeps showing the dangling wire until the menu closes
				OpenContextMenu( position, null, plug, onClose: EndPreview );
				return;
			}
		}

		EndPreview();
	}
}
