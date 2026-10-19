namespace Sandbox.UI.NodeGraph;

public partial class NodeGraphPanel
{
	const float HitTolerance = 5f;
	const float WireDragDistance = 3f;

	readonly List<Connection> _connections = new();
	readonly HashSet<IPlugIn> _wiredInputs = new();

	/// <summary>
	/// Every wire, built from the model's connected outputs.
	/// </summary>
	public IReadOnlyList<Connection> Connections => _connections;

	public IEnumerable<Connection> SelectedConnections => _connections.Where( x => x.Selected );

	/// <summary>
	/// How wires are laid out.
	/// </summary>
	public virtual ConnectionStyle ConnectionStyle => ConnectionStyle.Default;

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

	Connection _pressWire;
	Vector2 _pressWireStart;
	bool _wireDragging;

	Connection _hover;
	int _hoverHandle = -1;
	Connection _handleConnection;
	int _handleIndex = -1;
	Vector2 _handleGrab;
	bool _handleUndo;

	string _cursor;

	partial void OnCleared()
	{
		_connections.Clear();
		_wiredInputs.Clear();

		EndPreview();
		_pressPlug = null;
		_pressSource = null;
		_plugDragging = false;
		_pressWire = null;
		_wireDragging = false;
		_hover = null;
		_hoverHandle = -1;
		_handleConnection = null;
	}

	partial void OnNodeRemoving( NodePanel node )
	{
		foreach ( var connection in _connections.Where( x => x.IsAttachedTo( node ) ).ToArray() )
		{
			connection.Disconnect();
		}
	}

	partial void OnDeleteSelection()
	{
		var selected = SelectedConnections.ToArray();
		if ( selected.Length == 0 ) return;

		foreach ( var connection in selected )
		{
			connection.Disconnect();
		}

		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Connect <paramref name="output"/> to <paramref name="input"/>, replacing whatever the input had.
	/// <paramref name="uiOnly"/> adds the wire for a connection the model already has. Returns null
	/// when either plug hides its connection.
	/// </summary>
	protected virtual Connection CreateConnection( PlugOutPanel output, PlugInPanel input, bool uiOnly = false )
	{
		ArgumentNullException.ThrowIfNull( output );
		ArgumentNullException.ThrowIfNull( input );

		if ( !uiOnly )
		{
			input.Inner.ConnectedOutput = output.Inner;
			input.Node.MarkNodeChanged();
			output.Node.MarkNodeChanged();
		}

		// An input has one wire: whatever it had no longer matches the model
		foreach ( var old in _connections.Where( x => x.Input == input ).ToArray() )
		{
			Forget( old );
		}

		if ( !output.Inner.ShowConnection || !input.Inner.ShowConnection )
		{
			return null;
		}

		var connection = new Connection( this, output, input );
		_connections.Add( connection );
		_wiredInputs.Add( input.Inner );
		connection.Layout();

		return connection;
	}

	/// <summary>
	/// Remove <paramref name="connection"/> and clear its input's connected output.
	/// </summary>
	public virtual void RemoveConnection( Connection connection )
	{
		var input = connection.Input;
		var output = connection.Output;

		if ( input.IsValid() && output.IsValid() && Equals( input.Inner.ConnectedOutput, output.Inner ) )
		{
			input.Inner.ConnectedOutput = null;
		}

		Forget( connection );

		if ( input.IsValid() ) input.Node.MarkNodeChanged();
		if ( output.IsValid() ) output.Node.MarkNodeChanged();

		if ( connection.Selected )
		{
			connection.Selected = false;
			SelectionChanged?.Invoke();
		}
	}

	/// <summary>
	/// Remove every wire into <paramref name="plugIn"/>.
	/// </summary>
	internal void RemoveConnections( PlugInPanel plugIn )
	{
		foreach ( var connection in _connections.Where( x => x.Input == plugIn ).ToArray() )
		{
			RemoveConnection( connection );
		}
	}

	/// <summary>
	/// Remove every wire out of <paramref name="plugOut"/>.
	/// </summary>
	internal void RemoveConnections( PlugOutPanel plugOut )
	{
		foreach ( var connection in _connections.Where( x => x.Output == plugOut ).ToArray() )
		{
			RemoveConnection( connection );
		}
	}

	/// <summary>
	/// Drop a wire from the view, leaving the model alone.
	/// </summary>
	void Forget( Connection connection )
	{
		if ( !_connections.Remove( connection ) ) return;

		if ( connection.Input.IsValid() ) _wiredInputs.Remove( connection.Input.Inner );
		if ( _hover == connection ) _hover = null;
		if ( _pressWire == connection ) _pressWire = null;
		if ( _handleConnection == connection ) _handleConnection = null;
	}

	/// <summary>
	/// Keep the wire list matching the model: drop wires whose input no longer points at their output
	/// and add wires for inputs that do.
	/// </summary>
	void SyncConnections()
	{
		for ( var i = _connections.Count - 1; i >= 0; i-- )
		{
			var connection = _connections[i];

			if ( connection.IsValid
				&& Equals( connection.Input.Inner.ConnectedOutput, connection.Output.Inner )
				&& connection.Input.Inner.ShowConnection
				&& connection.Output.Inner.ShowConnection )
			{
				continue;
			}

			Forget( connection );
		}

		foreach ( var node in _nodes )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.Inner.ConnectedOutput is not { } output ) continue;
				if ( !input.Inner.ShowConnection || !output.ShowConnection ) continue;
				if ( _wiredInputs.Contains( input.Inner ) ) continue;
				if ( FindPlug( output ) is not PlugOutPanel outputPanel ) continue;

				CreateConnection( outputPanel, input, uiOnly: true );
			}
		}
	}

	/// <summary>
	/// The plug whose row contains <paramref name="position"/>, in canvas units. Comments are skipped:
	/// they have no plugs and sit behind what they hold, so they mustn't hide their members' plugs.
	/// </summary>
	protected PlugPanel GetPlugAt( Vector2 position )
	{
		for ( var i = _nodes.Count - 1; i >= 0; i-- )
		{
			var node = _nodes[i];
			if ( node is CommentPanel || !new Rect( node.Position, node.Size ).IsInside( position ) ) continue;

			return node.GetPlugAt( position );
		}

		return null;
	}

	/// <summary>
	/// The node at <paramref name="position"/>, in canvas units: the one owning the plug there,
	/// else the topmost node, else the innermost comment.
	/// </summary>
	protected NodePanel GetNodeAt( Vector2 position )
	{
		if ( GetPlugAt( position ) is { } plug ) return plug.Node;

		NodePanel comment = null;

		for ( var i = _nodes.Count - 1; i >= 0; i-- )
		{
			var node = _nodes[i];
			if ( !new Rect( node.Position, node.Size ).IsInside( position ) ) continue;

			if ( node is not CommentPanel ) return node;
			comment ??= node;
		}

		return comment;
	}

	Vector2 MouseCanvas => ScreenToCanvas( MousePosition );

	//
	// Plug dragging, driven by PlugPanel's mouse handlers
	//

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

	//
	// Selection
	//

	/// <summary>
	/// Select <paramref name="connection"/> for a press: Ctrl toggles it, Shift adds it, otherwise it
	/// becomes the only selection unless it's already selected.
	/// </summary>
	public void PressConnection( Connection connection, bool ctrl, bool shift )
	{
		if ( ctrl )
		{
			connection.Selected = !connection.Selected;
		}
		else if ( shift )
		{
			connection.Selected = true;
		}
		else if ( !connection.Selected )
		{
			DeselectNodes();
			foreach ( var other in _connections ) other.Selected = other == connection;
		}
		else
		{
			return;
		}

		SelectionChanged?.Invoke();
	}

	public void ClearConnectionSelection()
	{
		if ( DeselectConnections() ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Deselect every wire without raising <see cref="SelectionChanged"/>; true if any was selected.
	/// </summary>
	bool DeselectConnections()
	{
		var any = false;

		foreach ( var connection in _connections )
		{
			any |= connection.Selected;
			connection.Selected = false;
		}

		return any;
	}

	//
	// Hover and input
	//

	public override bool HasTooltip => _hover is { Input: not null, Output: not null };

	void BuildConnectionTooltip( Panel tip )
	{
		if ( _hover is not { Input: { } input, Output: { } output } ) return;

		static string Name( INode node ) => System.Net.WebUtility.HtmlEncode( node.DisplayInfo.Name );
		static string Plug( IPlug plug ) => $"<span style=\"color: #9CDCFE;\">{System.Net.WebUtility.HtmlEncode( plug.DisplayInfo.Name )}</span>";

		var label = tip.AddChild<Label>();
		label.IsRich = true;
		label.Text = $"<span style=\"white-space: nowrap;\">{NodeTheme.RichType( output.Inner.Type )}<br/>"
			+ $"<b>From</b>: {Name( output.Node.Node )} \u2192 {Plug( output.Inner )}<br/>"
			+ $"<b>To</b>: {Name( input.Node.Node )} \u2192 {Plug( input.Inner )}</span>";
	}

	void UpdateHover()
	{
		Connection hover = null;
		var handle = -1;

		var pressing = _pressWire is not null || _handleConnection is not null;
		var canHover = HasHovered && CanNavigate && !IsPanning && !IsBoxSelecting && _pressPlug is null
			&& Viewport.IsInside( MousePosition ) && !_nodes.Any( x => x.HasHovered );

		if ( pressing )
		{
			hover = _pressWire ?? _handleConnection;
			handle = _handleIndex;
		}
		else if ( canHover )
		{
			var mouse = MouseCanvas;

			foreach ( var connection in _connections )
			{
				if ( !connection.ShowHandles ) continue;
				if ( connection.HandleAt( mouse ) is var index and >= 0 )
				{
					hover = connection;
					handle = index;
					break;
				}
			}

			if ( hover is null )
			{
				var bestRank = -1;
				var tolerance = HitTolerance;

				foreach ( var connection in _connections )
				{
					if ( connection.Dragging || !connection.Bounds.Grow( tolerance ).IsInside( mouse ) ) continue;
					if ( connection.DistanceTo( mouse ) > tolerance ) continue;

					if ( connection.Rank >= bestRank )
					{
						bestRank = connection.Rank;
						hover = connection;
					}
				}
			}
		}

		foreach ( var connection in _connections )
		{
			connection.Hovered = connection == hover;
			connection.HoveredHandle = connection == hover ? handle : -1;
		}

		_hover = hover;
		_hoverHandle = handle;

		UpdateCursor();
	}

	void UpdateCursor()
	{
		string cursor = null;

		if ( _plugDragging || _wireDragging )
		{
			cursor = "crosshair";
		}
		else if ( _hover is not null )
		{
			cursor = _hoverHandle < 0 ? "pointer"
				: _hover.Handles[_hoverHandle].Direction == DragDirection.Horizontal ? "ew-resize" : "ns-resize";
		}

		if ( cursor == _cursor ) return;

		_cursor = cursor;
		Style.Cursor = cursor;
	}

	/// <summary>
	/// A window drops :active a frame before it sends mouseup, so a gesture that lost it has been
	/// released: finish it where the cursor is. A deleted plug cancels instead.
	/// </summary>
	void CheckConnectionGestures()
	{
		if ( _pressPlug is not null && !_pressPlug.IsValid() )
		{
			CancelPlugDrag();
		}
		else if ( _pressPlug is { HasActive: false } plug )
		{
			PlugReleased( plug );
		}

		if ( !HasActive )
		{
			ReleaseWire();
			EndHandleDrag();
		}
	}

	/// <summary>
	/// Drop a wire dragged off its input where the cursor is; a press without a drag only selected it.
	/// </summary>
	void ReleaseWire()
	{
		if ( _pressWire is not { } wire ) return;

		var dragging = _wireDragging;

		_pressWire = null;
		_wireDragging = false;

		if ( dragging ) DroppedPlug( wire.Output, MouseCanvas, wire );
	}

	void EndHandleDrag()
	{
		if ( _handleConnection is not null ) _handleConnection.DraggedHandle = -1;

		_handleConnection = null;
		_handleIndex = -1;

		if ( _handleUndo )
		{
			_handleUndo = false;
			PushRedo();
		}
	}

	/// <summary>
	/// Split <paramref name="connection"/> with a reroute centred on <paramref name="position"/>,
	/// in canvas units, as one undo step. Does nothing without <see cref="RerouteNodeType"/>.
	/// </summary>
	internal void RerouteConnection( Connection connection, Vector2 position )
	{
		if ( RerouteNodeType is null || Graph is null || !connection.IsValid ) return;

		using var undoScope = UndoScope( "Reroute Connection" );

		var panel = CreateNewNode( RerouteNodeType, position - new Vector2( ReroutePanel.Diameter * 0.5f ) );
		if ( panel is null || panel.Inputs.Count == 0 || panel.Outputs.Count == 0 ) return;

		var from = connection.Output;
		var to = connection.Input;

		RemoveConnection( connection );
		CreateConnection( from, panel.Inputs[0] );
		CreateConnection( panel.Outputs[0], to );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.Button == "mouseleft" )
		{
			UpdateHover();

			if ( _hover is { } connection )
			{
				e.StopPropagation();
				Focus();

				if ( _hoverHandle >= 0 )
				{
					BeginHandleDrag( connection, _hoverHandle );
					return;
				}

				// Shift-click splits the wire with a reroute, like the editor; without reroutes
				// Shift adds to the selection
				if ( e.HasShift && RerouteNodeType is not null && Graph is not null )
				{
					RerouteConnection( connection, MouseCanvas );
					return;
				}

				PressConnection( connection, e.HasCtrl, e.HasShift );

				_pressWire = connection;
				_pressWireStart = MousePosition;
				_wireDragging = false;
				return;
			}
		}

		base.OnMouseDown( e );
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( _handleConnection is not null )
		{
			MoveHandle();
			e.StopPropagation();
			return;
		}

		if ( _pressWire is { } wire )
		{
			e.StopPropagation();

			if ( !_wireDragging && (MousePosition - _pressWireStart).Length < WireDragDistance * ScaleToScreen )
			{
				return;
			}

			_wireDragging = true;
			DraggingPlug( wire.Output, MouseCanvas, wire );
			return;
		}

		base.OnMouseMove( e );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( e.Button == "mouseleft" )
		{
			if ( _handleConnection is not null )
			{
				MoveHandle();
				EndHandleDrag();
				e.StopPropagation();
				return;
			}

			if ( _pressWire is not null )
			{
				ReleaseWire();
				e.StopPropagation();
				return;
			}
		}

		base.OnMouseUp( e );
	}

	void BeginHandleDrag( Connection connection, int index )
	{
		_handleConnection = connection;
		_handleIndex = index;
		_handleUndo = false;
		connection.DraggedHandle = index;
		_handleGrab = MouseCanvas - connection.Handles[index].GetPosition( connection );
	}

	void MoveHandle()
	{
		var connection = _handleConnection;
		if ( connection is null || !connection.IsValid || _handleIndex >= connection.Handles.Count ) return;

		var config = connection.Handles[_handleIndex];

		var plug = config.RelativePlug == ConnectionPlug.Input ? connection.Input?.Node : connection.Output?.Node;
		if ( plug is { Selected: true } ) return;

		var diff = (MouseCanvas - _handleGrab - config.GetPosition( connection )).SnapToGrid( GridSize );
		var offset = config.Direction == DragDirection.Horizontal ? diff.x : diff.y;

		if ( offset.AlmostEqual( 0f ) ) return;

		var value = config.GetValue( connection ) + offset;

		if ( !_handleUndo )
		{
			_handleUndo = true;
			PushUndo( "Move Item" );
		}

		connection.Input.Inner.SetHandleOffset( config.Name, value.AlmostEqual( config.Default ) ? null : value );
		connection.Layout();
	}

	//
	// Drawing
	//

	/// <summary>
	/// Lay the wires out and hand each visible one to the node that paints it, see
	/// <see cref="Connection.PaintOwner"/>. Lower ranks first, so hovered and selected wires end on top.
	/// </summary>
	void UpdateConnections()
	{
		SyncConnections();
		CheckConnectionGestures();

		foreach ( var connection in _connections )
		{
			connection.Layout();
		}

		UpdateHover();

		foreach ( var node in _nodes )
		{
			node.PaintedWires.Clear();
		}

		var viewport = Viewport;
		var view = Rect.FromPoints( ScreenToCanvas( viewport.Position ), ScreenToCanvas( viewport.Position + viewport.Size ) );

		for ( var rank = 0; rank <= 2; rank++ )
		{
			foreach ( var connection in _connections )
			{
				if ( connection.Rank != rank || !connection.Bounds.Overlaps( view ) ) continue;

				connection.PaintOwner?.PaintedWires.Add( connection );
			}
		}

		Preview?.PaintOwner?.PaintedWires.Add( Preview );
	}
}
