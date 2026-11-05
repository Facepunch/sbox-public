namespace Editor.AnimGraph.Nodes;

public partial class Blend2DNode
{
	internal bool MigrateLegacyPoints( Data parent )
	{
		InitializePointGraphs();

		var migratedNodes = new HashSet<Node>();
		var migrated = false;

		foreach ( var point in Points )
		{
			if ( !point.Input.IsValid )
				continue;

			if ( parent.FindNode( point.Input.Identifier ) is { } source )
				CopyLegacyPointGraph( parent, point, source, migratedNodes );

			point.Input = default;
			migrated = true;
		}

		if ( migrated )
			RemoveMigratedNodes( parent, migratedNodes );

		return migrated;
	}

	private void CopyLegacyPointGraph( Data parent, BlendPoint point, Node source, HashSet<Node> migratedNodes )
	{
		var branch = CollectBranch( source );
		var originals = branch.ToArray();
		var copies = point.Graph.DeserializeNodes( parent.SerializeNodes( originals ) ).OfType<Node>().ToArray();
		var sourceIndex = Array.IndexOf( originals, source );

		if ( sourceIndex < 0 || sourceIndex >= copies.Length
			|| point.Graph.Nodes.OfType<PoseResultNode>().FirstOrDefault() is not { } output )
			return;

		var copy = copies[sourceIndex];
		output.Pose = new NodeInput { Identifier = copy.Identifier, Output = point.Input.Output };
		migratedNodes.UnionWith( branch );
	}

	private HashSet<Node> CollectBranch( Node root )
	{
		var branch = new HashSet<Node>();
		var pending = new Stack<Node>();
		pending.Push( root );

		while ( pending.TryPop( out var node ) )
		{
			if ( node == this || !branch.Add( node ) )
				continue;

			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput?.Node is Node source )
					pending.Push( source );
			}
		}

		return branch;
	}

	private void RemoveMigratedNodes( Data parent, HashSet<Node> migratedNodes )
	{
		var preserved = new HashSet<Node>();

		foreach ( var node in parent.Nodes.OfType<Node>() )
		{
			if ( node != this && !migratedNodes.Contains( node ) )
				PreserveConnectedBranch( node, migratedNodes, preserved );

			if ( node is not Blend2DNode blend || blend == this )
				continue;

			foreach ( var point in blend.Points )
			{
				if ( point.Input.IsValid && parent.FindNode( point.Input.Identifier ) is { } source )
					PreserveBranch( source, migratedNodes, preserved );
			}
		}

		foreach ( var node in migratedNodes )
		{
			if ( !preserved.Contains( node ) )
				parent.DetachNode( node );
		}
	}

	private static void PreserveConnectedBranch( Node node, HashSet<Node> candidates, HashSet<Node> preserved )
	{
		foreach ( var input in node.Inputs )
		{
			if ( input.ConnectedOutput?.Node is Node source )
				PreserveBranch( source, candidates, preserved );
		}
	}

	private static void PreserveBranch( Node root, HashSet<Node> candidates, HashSet<Node> preserved )
	{
		var pending = new Stack<Node>();
		pending.Push( root );

		while ( pending.TryPop( out var node ) )
		{
			if ( !candidates.Contains( node ) || !preserved.Add( node ) )
				continue;

			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput?.Node is Node source )
					pending.Push( source );
			}
		}
	}
}
