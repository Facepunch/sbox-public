using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the current value of a named vector graph parameter.
/// <br/><br/><b>Use when:</b>&#160;Reading a direction, velocity, position, or other vector supplied to the graph.
/// <br/><b>Important:</b>&#160;A missing, unknown, or type-mismatched name produces a compiler warning.
/// </summary>
[Title( "Vector Parameter" ), Group( "Parameters" ), Icon( "open_with" )]
public class VectorParameterNode : ParameterNode
{
	public override AnimParameterType ParameterType => AnimParameterType.Vector;

	[Output( typeof( VectorLink ) ), JsonIgnore]
	public object Value => default;
}
