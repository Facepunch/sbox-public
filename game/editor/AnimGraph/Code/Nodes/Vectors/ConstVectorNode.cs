using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs a constant three-component vector.
/// <br/><br/><b>Use when:</b>&#160;Supplying a fixed direction, position, offset, or other vector.
/// </summary>
[Title( "Vector" ), Group( "Values" ), Icon( "open_with" )]
public class ConstVectorNode : Node
{
	[Property]
	public Vector3 Value { get; set; }

	[Output( typeof( VectorLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.Constant( Value );

}
