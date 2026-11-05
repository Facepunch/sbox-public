using Editor.AnimGraph.Nodes;
using Editor.NodeEditor;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	private static void GroupNodes( Data root, EditOperation operation, Dictionary<string, string> aliases )
	{
		if ( operation.Nodes is not { Length: > 0 } )
			throw new Exception( "group_nodes requires at least one node" );

		var graph = FindGraph( root, operation.Graph, aliases );
		if ( graph.Kind != GraphKind.BlendTree )
			throw new Exception( "Pose groups require a blend-tree graph" );

		var nodes = operation.Nodes.Select( id => FindNode( graph, id, aliases ) ).ToHashSet();
		if ( nodes.Count != operation.Nodes.Length )
			throw new Exception( "group_nodes cannot contain duplicate nodes" );
		if ( nodes.Any( node => !node.CanClone || !node.CanRemove || node is GroupInputNode ) )
			throw new Exception( "Graph-managed nodes and group inputs cannot be grouped" );

		var outputNode = FindNode( graph, operation.Node, aliases );
		var outputName = string.IsNullOrWhiteSpace( operation.FromOutput ) ? "Result" : operation.FromOutput;
		var output = outputNode.Outputs.FirstOrDefault( plug => plug.Identifier == outputName );
		if ( !nodes.Contains( outputNode ) || output?.Type != typeof( PoseLink ) )
			throw new Exception( "Node must identify a selected node with a pose output" );

		var outgoing = graph.Nodes.OfType<Node>().Where( node => !nodes.Contains( node ) )
			.SelectMany( node => node.Inputs )
			.Where( input => input.ConnectedOutput?.Node is Node source && nodes.Contains( source ) )
			.ToArray();
		if ( outgoing.Any( input => input.ConnectedOutput != output ) )
			throw new Exception( "The selection has more than one exported output; include its consumers or select a smaller group" );

		var incoming = nodes.SelectMany( node => node.Inputs )
			.Where( input => input.ConnectedOutput?.Node is Node source && !nodes.Contains( source ) )
			.Select( input => (Input: input, Source: input.ConnectedOutput) ).ToArray();
		foreach ( var (_, source) in incoming )
			GroupInputNode.KindFor( source.Type );

		var group = new PoseGroupNode { Position = outputNode.Position };
		ApplyValues( group, operation.Values, nameof( Node.Identifier ), nameof( Node.Graph ), nameof( PoseGroupNode.Contents ) );
		if ( !string.IsNullOrWhiteSpace( operation.Alias ) && !aliases.TryAdd( operation.Alias, group.Identifier ) )
			throw new Exception( $"Alias '{operation.Alias}' is already in use" );

		foreach ( var node in nodes )
		{
			graph.DetachNode( node );
			group.Contents.AddNode( node );
		}
		group.Contents.Nodes.OfType<PoseResultNode>().Single().Pose = new() { Identifier = outputNode.Identifier, Output = outputName };
		foreach ( var boundary in incoming.GroupBy( edge => edge.Source ) )
		{
			var source = boundary.Key;
			var input = new GroupInputNode
			{
				Name = source.Node.DisplayInfo.Name,
				ValueType = GroupInputNode.KindFor( source.Type ),
				Source = new() { Identifier = source.Node.Identifier, Output = source.Identifier }
			};
			group.Contents.AddNode( input );
			foreach ( var edge in boundary )
				edge.Input.ConnectedOutput = input.Outputs.Single();
		}
		graph.AddNode( group );
		foreach ( var input in outgoing )
			input.ConnectedOutput = group.Outputs.Single();
		GraphLayout.Arrange( group.Contents );
	}

	private static void UngroupNode( Data root, EditOperation operation, IReadOnlyDictionary<string, string> aliases )
	{
		var graph = FindGraph( root, operation.Graph, aliases );
		if ( FindNode( graph, operation.Node, aliases ) is not PoseGroupNode group )
			throw new Exception( "ungroup_node requires a Pose Group" );

		var output = group.Contents.Nodes.OfType<PoseResultNode>().Single();
		var source = output.Inputs.Single().ConnectedOutput;
		var outgoing = graph.Nodes.SelectMany( node => node.Inputs )
			.Where( input => input.ConnectedOutput?.Node == group ).ToArray();
		var connections = group.Contents.Nodes.SelectMany( node => node.Inputs )
			.Where( input => input.ConnectedOutput?.Node is GroupInputNode )
			.Select( input => (Input: input, Boundary: (GroupInputNode)input.ConnectedOutput.Node) ).ToArray();

		IPlugOut Resolve( GroupInputNode input ) => group.GetConnectedOutput( input.Source );

		if ( source?.Node is GroupInputNode sourceInput )
			source = Resolve( sourceInput );
		foreach ( var (input, boundary) in connections )
			input.ConnectedOutput = Resolve( boundary );
		foreach ( var node in group.Contents.Nodes.OfType<Node>().Where( node => node is not GroupInputNode && node != output ).ToArray() )
		{
			if ( graph.FindNode( node.Identifier ) is not null )
				throw new Exception( $"The parent graph already has node '{node.Identifier}'" );
			group.Contents.DetachNode( node );
			node.Position += group.Position;
			graph.AddNode( node );
		}
		graph.DetachNode( group );
		foreach ( var input in outgoing )
			input.ConnectedOutput = source;
	}
}
