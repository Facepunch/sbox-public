namespace Editor.AnimGraph;

public static partial class McpTools
{
	private static void MoveNodes( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		if ( operation.Nodes is not { Length: > 0 } )
			throw new Exception( "move_nodes requires at least one node" );

		if ( string.IsNullOrWhiteSpace( operation.TargetGraph ) )
			throw new Exception( "move_nodes requires a target graph" );

		var source = FindGraph( root, operation.Graph, aliases );
		var target = FindGraph( root, operation.TargetGraph, aliases );
		if ( source == target )
			throw new Exception( "The source and target graphs are the same" );

		var nodes = operation.Nodes.Select( id => FindNode( source, id, aliases ) ).ToArray();
		var moving = nodes.ToHashSet();
		if ( moving.Count != nodes.Length )
			throw new Exception( "move_nodes cannot contain duplicate nodes" );

		var graphs = Graphs( root );
		var sourcePath = graphs.First( graph => graph.Graph == source ).Id;
		var targetPath = graphs.First( graph => graph.Graph == target ).Id;

		foreach ( var node in nodes )
		{
			if ( !node.CanClone || !node.CanRemove )
				throw new Exception( $"{node.GetType().Name} is managed by its owning graph and cannot be moved" );

			if ( targetPath.StartsWith( $"{sourcePath}/node:{node.Identifier}/", StringComparison.Ordinal ) )
				throw new Exception( "A node cannot be moved into one of its own graphs" );

			if ( !new NodeType( EditorTypeLibrary.GetType( node.GetType() ) ).IsAvailableIn( target ) )
				throw new Exception( $"{node.GetType().Name} is not available in {target.Kind} graphs" );

			if ( target.FindNode( node.Identifier ) is not null )
				throw new Exception( $"The target graph already has node '{node.Identifier}'" );
		}

		foreach ( var node in source.Nodes.OfType<Node>() )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput?.Node is Node connected && moving.Contains( node ) != moving.Contains( connected ) )
					throw new Exception( $"Moving these nodes would break '{node.Identifier}.{input.Identifier}' - disconnect it first" );
			}
		}

		foreach ( var node in nodes )
			source.DetachNode( node );

		foreach ( var node in nodes )
			target.AddNode( node );
	}
}
