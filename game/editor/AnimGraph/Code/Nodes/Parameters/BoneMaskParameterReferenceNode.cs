using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named virtual bone-mask parameter.
/// </summary>
[Title( "Bone Mask Parameter" ), Icon( "blur_on" ), Hide]
public sealed class BoneMaskParameterReferenceNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.BoneMask;

	[Output( typeof( BoneMaskLink ) ), JsonIgnore]
	public object Value => default;
}
