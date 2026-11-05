namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class TargetResultNode : VirtualParameterResultNode
{
	[Input( typeof( TargetLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
