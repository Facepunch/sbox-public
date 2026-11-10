namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class VectorResultNode : VirtualParameterResultNode
{
	[Input( typeof( VectorLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
