namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Receives the pose that becomes the AnimGraph's final output.
/// <br/><br/><b>Use when:</b>&#160;Defining the root result of the graph.
/// <br/><b>Important:</b>&#160;Every graph has exactly one editor-managed Result node; it cannot be cloned or removed.
/// </summary>
[Title( "Result" ), Group( "Graph" ), Icon( "output" )]
[AvailableIn]
public class PoseResultNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Pose { get; set; }

	public override bool CanClone => false;
	public override bool CanRemove => HasDuplicate;
	public override Color PrimaryColor => Theme.Red;
}
