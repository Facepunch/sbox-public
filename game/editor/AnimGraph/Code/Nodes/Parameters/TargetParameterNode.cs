using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named animation-target graph parameter.
/// <br/><br/><b>Use when:</b>&#160;Reading a transform or bone target supplied to targeting and selection nodes.
/// <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.
/// </summary>
[Title( "Target Parameter" ), Group( "Parameters" ), Icon( "my_location" )]
public class TargetParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.Target;

	[Output( typeof( TargetLink ) ), JsonIgnore]
	public object Value => default;
}
