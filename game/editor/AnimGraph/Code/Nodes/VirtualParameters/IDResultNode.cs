namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class IDResultNode : VirtualParameterResultNode
{
	[Input( typeof( IDLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
