namespace Sandbox.UI.NodeGraph;

//
// Selection of nodes and wires: presses, box selection, select all and clear.
//
public partial class NodeGraphPanel
{
	HashSet<NodePanel> _boxBase;

	/// <summary>
	/// A box selection began. Shift-dragging (<paramref name="additive"/>) keeps the current selection.
	/// Overrides should call the base, which the rest of the gesture relies on.
	/// </summary>
	protected virtual void OnBoxSelectionStarted( bool additive )
	{
		_boxBase = additive ? SelectedNodes.ToHashSet() : [];
		if ( !additive ) DeselectConnections();
	}

	/// <summary>
	/// A box selection ended or was <paramref name="cancelled"/>. Overrides should call the base.
	/// </summary>
	protected virtual void OnBoxSelectionFinished( bool cancelled ) => _boxBase = null;

	/// <summary>
	/// Select <paramref name="node"/> for a press: Ctrl toggles it, Shift adds it, otherwise it
	/// becomes the only selection unless it's already selected.
	/// </summary>
	public void PressNode( NodePanel node, bool ctrl, bool shift )
	{
		if ( ctrl )
		{
			node.Selected = !node.Selected;
		}
		else if ( shift )
		{
			node.Selected = true;
		}
		else if ( !node.Selected )
		{
			DeselectConnections();
			foreach ( var other in _nodes ) other.Selected = other == node;
		}
		else
		{
			return;
		}

		SelectionChanged?.Invoke();
	}

	public void SelectNode( NodePanel node )
	{
		DeselectConnections();
		foreach ( var other in _nodes ) other.Selected = other == node;
		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Make the panel showing <paramref name="node"/> the only selection. Does nothing if it isn't shown.
	/// </summary>
	public void SelectNode( INode node )
	{
		if ( node is not null && FindNode( node ) is { } panel )
			SelectNode( panel );
	}

	/// <summary>
	/// Select every node and wire.
	/// </summary>
	public void SelectAll()
	{
		foreach ( var node in _nodes ) node.Selected = true;
		foreach ( var connection in _connections ) connection.Selected = true;
		SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Deselect every node and wire.
	/// </summary>
	public void ClearSelection()
	{
		if ( DeselectNodes() | DeselectConnections() ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Deselect every node without raising <see cref="SelectionChanged"/>; true if any was selected.
	/// </summary>
	bool DeselectNodes()
	{
		var any = false;

		foreach ( var node in _nodes )
		{
			any |= node.Selected;
			node.Selected = false;
		}

		return any;
	}

	/// <summary>
	/// Remove the selected wires, and every selected node that can be removed, as one undo step.
	/// </summary>
	public virtual void DeleteSelection()
	{
		if ( !_nodes.Any( x => x.Selected ) && !_connections.Any( x => x.Selected ) ) return;

		using var undoScope = UndoScope( "Delete Selection" );

		OnDeleteSelection();

		foreach ( var node in SelectedNodes.Where( x => x.Node.CanRemove ).ToArray() )
		{
			RemoveNode( node );
		}
	}

	partial void OnDeleteSelection();

	/// <summary>
	/// The box moved, in canvas units. Any overlap selects a node. A comment needs the whole of it
	/// inside the box, so selecting nodes in a comment doesn't drag the comment and everything in it along.
	/// </summary>
	protected virtual void OnBoxSelectionChanged( Rect box )
	{
		foreach ( var node in _nodes )
		{
			var rect = new Rect( node.Position, node.Size );
			var inBox = node is CommentPanel ? box.IsInside( rect, true ) : rect.Overlaps( box );
			node.Selected = _boxBase.Contains( node ) || inBox;
		}

		SelectionChanged?.Invoke();
	}

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
}
