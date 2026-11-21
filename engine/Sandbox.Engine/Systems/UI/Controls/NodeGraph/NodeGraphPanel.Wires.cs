namespace Sandbox.UI.NodeGraph;

//
// Wires under the mouse: hover, tooltip, cursor, pressing, dragging off an input and moving handles.
//
public partial class NodeGraphPanel
{
	const float HitTolerance = 5f;
	const float WireDragDistance = 3f;

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
}
