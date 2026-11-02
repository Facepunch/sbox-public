using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named boolean graph parameter.
/// <br/><br/><b>Use when:</b>&#160;Reading an externally controlled true or false state.
/// <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.
/// </summary>
[Title( "Bool Parameter" ), Group( "Parameters" ), Icon( "toggle_on" )]
public class BoolParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.Bool;

	[Output( typeof( BoolLink ) ), JsonIgnore]
	public object Value => default;
}
