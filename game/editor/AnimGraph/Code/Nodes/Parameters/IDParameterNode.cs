using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named ID graph parameter.
/// <br/><br/><b>Use when:</b>&#160;Reading a named state, action, or selection supplied to the graph.
/// <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.
/// </summary>
[Title( "ID Parameter" ), Group( "Parameters" ), Icon( "label" )]
public class IDParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.ID;

	[Output( typeof( IDLink ) ), JsonIgnore]
	public object Value => default;
}
