namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Defines the value returned by a virtual parameter's private graph.
/// <br/><br/><b>Important:</b>&#160;The input type matches the virtual parameter. This required node cannot be cloned or removed.
/// </summary>
public abstract class VirtualParameterResultNode : Node
{
	public override bool CanClone => false;
	public override bool CanRemove => HasDuplicate;
	public override Color PrimaryColor => Theme.Red;
}
