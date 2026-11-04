namespace Editor.AnimGraph.Nodes;

[Title( "Result" ), Group( "Results" ), Icon( "output" ), AvailableIn]
public sealed class BoneMaskResultNode : VirtualParameterResultNode
{
	[Input( typeof( BoneMaskLink ) ), Title( "Out" )]
	public NodeInput Out { get; set; }
}
