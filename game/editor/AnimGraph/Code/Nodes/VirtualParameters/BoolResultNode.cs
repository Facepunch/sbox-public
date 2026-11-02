namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class BoolResultNode : VirtualParameterResultNode
{
	[Input( typeof( BoolLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
