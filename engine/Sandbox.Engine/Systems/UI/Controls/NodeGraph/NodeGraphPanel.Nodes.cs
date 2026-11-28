namespace Sandbox.UI.NodeGraph;

//
// Nodes: creating them from node types, removing them, and building the view from the graph.
//
public partial class NodeGraphPanel
{
	/// <summary>
	/// Add <paramref name="node"/> to the graph and show it.
	/// </summary>
	public NodePanel AddNode( INode node )
	{
		ArgumentNullException.ThrowIfNull( node );

		node.Position = node.Position.SnapToGrid( GridSize );
		Graph.AddNode( node );

		return ShowNode( node );
	}

	NodePanel ShowNode( INode node )
	{
		var panel = Content.AddChild( CreateNodePanel( node ) );
		_nodes.Add( panel );
		return panel;
	}

	/// <summary>
	/// What the "Add Reroute" entries, R-dropped wires and Shift-clicked wires create. Null turns
	/// reroutes off.
	/// </summary>
	protected virtual INodeType RerouteNodeType => null;

	/// <summary>
	/// What "Add Comment" creates; must make <see cref="ICommentNode"/>s. Null turns comments off.
	/// </summary>
	protected virtual INodeType CommentNodeType => null;

	/// <summary>
	/// A node made by <see cref="CreateNewNode(INodeType, Action{INode})"/> was added to the graph.
	/// </summary>
	protected virtual void OnNodeCreated( INode node ) { }

	/// <summary>
	/// Create a node of <paramref name="type"/> at <paramref name="position"/>, snapped to the grid,
	/// add it to the graph and show it. Null when there's no type or graph.
	/// </summary>
	public virtual NodePanel CreateNewNode( INodeType type, Vector2 position )
	{
		return CreateNewNode( type, node => node.Position = position.SnapToGrid( GridSize ) );
	}

	/// <summary>
	/// Create a node of <paramref name="type"/>, set it up with <paramref name="onCreated"/>, add it
	/// to the graph and show it. Null when there's no type or graph, or the type made nothing.
	/// </summary>
	public virtual NodePanel CreateNewNode( INodeType type, Action<INode> onCreated = null )
	{
		if ( type is null || Graph is null ) return null;

		var node = type.CreateNode( Graph );
		if ( node is null ) return null;

		onCreated?.Invoke( node );

		Graph.AddNode( node );
		OnNodeCreated( node );

		return ShowNode( node );
	}

	/// <summary>
	/// Add a node of <paramref name="type"/> at <paramref name="position"/>, as one undo step, and
	/// wire it to <paramref name="targetPlug"/> when it has a matching plug.
	/// </summary>
	public virtual void CreateNewNode( INodeType type, Vector2 position, PlugPanel targetPlug, bool selected = true )
	{
		using var undoScope = UndoScope( "Add Node" );

		var panel = CreateNewNode( type, position );
		if ( panel is null ) return;

		if ( selected ) SelectNode( panel );

		if ( !targetPlug.IsValid() ) return;

		if ( targetPlug is PlugInPanel plugIn )
		{
			if ( !type.TryGetOutput( plugIn.Inner.Type, out var targetName ) ) return;

			if ( panel.Outputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				// An input takes one wire: drop the old one now rather than on the next sync
				RemoveConnections( plugIn );
				CreateConnection( match, plugIn );
			}
		}
		else if ( targetPlug is PlugOutPanel plugOut )
		{
			if ( !type.TryGetInput( plugOut.Inner.Type, out var targetName ) ) return;

			if ( panel.Inputs.FirstOrDefault( x => x.Inner.Identifier == targetName ) is { } match )
			{
				CreateConnection( plugOut, match );
			}
		}
	}

	/// <summary>
	/// Add a reroute centred on <paramref name="position"/>.
	/// </summary>
	public void CreateNewReroute( Vector2 position )
	{
		using var undoScope = UndoScope( "Add Reroute" );

		CreateNewNode( RerouteNodeType, position );
	}

	/// <summary>
	/// Remove a node, and its wires, from the graph. Nodes that can't be removed stay.
	/// </summary>
	public virtual void RemoveNode( NodePanel node )
	{
		if ( node is null || !node.Node.CanRemove || !_nodes.Contains( node ) ) return;

		using var undoScope = UndoScope( "Delete Node" );

		_nodes.Remove( node );
		OnNodeRemoving( node );
		Graph?.RemoveNode( node.Node );
		node.Delete( true );

		if ( node.Selected ) SelectionChanged?.Invoke();
	}

	partial void OnNodeRemoving( NodePanel node );

	/// <summary>
	/// Fix up the graph, such as replacing obsolete nodes. Does nothing by default.
	/// </summary>
	public virtual void CleanUp() { }

	/// <summary>
	/// Rebuild every node panel and wire from <see cref="Graph"/>.
	/// </summary>
	public virtual void RebuildFromGraph()
	{
		var hadSelection = _nodes.Any( x => x.Selected );

		OnClear();

		foreach ( var node in _nodes )
		{
			node.Delete( true );
		}

		_nodes.Clear();
		OnCleared();

		if ( Graph is not null )
		{
			BuildFromNodes( Graph.Nodes, insert: true );
		}

		OnRebuild();

		if ( _viewInitialized ) RestoreViewFromCookie();
		if ( hadSelection ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// The view is about to drop every node panel, in <see cref="RebuildFromGraph"/>.
	/// </summary>
	protected virtual void OnClear() { }

	/// <summary>
	/// <see cref="RebuildFromGraph"/> has shown the graph again.
	/// </summary>
	protected virtual void OnRebuild() { }

	partial void OnCleared();

	/// <summary>
	/// Show <paramref name="nodes"/> and their wires. When <paramref name="insert"/>, they're new:
	/// each moves by <paramref name="offset"/> and gets a panel. Otherwise they're the whole graph:
	/// panels of nodes not among them go, the rest re-read their node, and missing ones are added.
	/// <paramref name="selectNew"/> selects the new panels and the wires on them.
	/// </summary>
	public virtual void BuildFromNodes( IEnumerable<INode> nodes, bool insert, Vector2 offset = default, bool selectNew = false )
	{
		var set = new HashSet<INode>();
		var ordered = new List<INode>();

		foreach ( var node in nodes )
		{
			if ( node is not null && set.Add( node ) ) ordered.Add( node );
		}

		if ( !insert )
		{
			foreach ( var panel in _nodes.Where( x => !set.Contains( x.Node ) ).ToArray() )
			{
				_nodes.Remove( panel );
				panel.Delete( true );
			}
		}

		foreach ( var node in ordered )
		{
			if ( !insert && FindNode( node ) is { } existing )
			{
				existing.Rebuild();
				continue;
			}

			if ( offset != default ) node.Position += offset;

			var panel = ShowNode( node );
			if ( selectNew ) panel.Selected = true;
		}

		UpdateConnections( ordered, selectNew );

		if ( selectNew && ordered.Count > 0 ) SelectionChanged?.Invoke();
	}

	/// <summary>
	/// Match the wires to the model, then lay out those on <paramref name="nodes"/>, selecting them
	/// too when <paramref name="selectNew"/>.
	/// </summary>
	public void UpdateConnections( IEnumerable<INode> nodes, bool selectNew = false )
	{
		SyncConnections();

		var set = nodes as ISet<INode> ?? nodes.ToHashSet();

		foreach ( var connection in _connections )
		{
			if ( !connection.IsValid ) continue;
			if ( !set.Contains( connection.Input.Node.Node ) && !set.Contains( connection.Output.Node.Node ) ) continue;

			connection.Layout();
			if ( selectNew ) connection.Selected = true;
		}
	}

	/// <summary>
	/// Re-read <paramref name="node"/>'s plugs and position.
	/// </summary>
	public void UpdateNode( INode node ) => FindNode( node )?.MarkNodeChanged();
}
