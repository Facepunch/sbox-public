using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named float graph parameter.
/// <br/><br/><b>Use when:</b>&#160;Reading a numeric value supplied to the AnimGraph.
/// <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.
/// </summary>
[Title( "Float Parameter" ), Group( "Parameters" ), Icon( "pin" )]
public class FloatParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.Float;

	[Output( typeof( FloatLink ) ), JsonIgnore]
	public object Value => default;
}
