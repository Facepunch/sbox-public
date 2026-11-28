namespace Sandbox.UI.NodeGraph;

//
// Wires: kept matching the model's connected outputs, created and removed, and laid out to draw.
//
public partial class NodeGraphPanel
{
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
	/// Split <paramref name="connection"/> with a reroute centred on <paramref name="position"/>,
	/// in canvas units, as one undo step. Does nothing without <see cref="RerouteNodeType"/>.
	/// </summary>
	internal void RerouteConnection( Connection connection, Vector2 position )
	{
		if ( RerouteNodeType is null || Graph is null || !connection.IsValid ) return;

		using var undoScope = UndoScope( "Reroute Connection" );

		var panel = CreateNewNode( RerouteNodeType, position );
		if ( panel is null || panel.Inputs.Count == 0 || panel.Outputs.Count == 0 ) return;

		var from = connection.Output;
		var to = connection.Input;

		RemoveConnection( connection );
		CreateConnection( from, panel.Inputs[0] );
		CreateConnection( panel.Outputs[0], to );
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
