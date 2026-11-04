namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Supplies a state's layer weight, root-motion weight, and bone mask when its state machine is used as a layer.
/// <br/><br/><b>Use when:</b>&#160;Controlling how each state contributes through a State Machine Layer.
/// <br/><b>Important:</b>&#160;This editor-managed node cannot be cloned or removed.
/// </summary>
[Title( "State Layer Data" ), Group( "State Machine" ), Icon( "layers" )]
[AvailableIn]
public class StateLayerDataNode : Node
{
	[Input( typeof( FloatLink ) ), Title( "Layer Weight" )]
	public NodeInput LayerWeight { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Root Motion Weight" )]
	public NodeInput RootMotionWeight { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "Bone Mask" )]
	public NodeInput BoneMask { get; set; }

	public override bool CanClone => false;
	public override bool CanRemove => false;
	public override Color PrimaryColor => Theme.Red;
}
