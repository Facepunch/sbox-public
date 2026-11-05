namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class FloatResultNode : VirtualParameterResultNode
{
	[Input( typeof( FloatLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
