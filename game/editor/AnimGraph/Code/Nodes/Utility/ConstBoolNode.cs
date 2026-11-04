using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs a constant boolean value.
/// <br/><br/><b>Use when:</b>&#160;Supplying a fixed true or false value to another node.
/// </summary>
[Title( "Bool" ), Group( "Values" ), Icon( "toggle_off" )]
public class ConstBoolNode : Node
{
	[Property]
	public bool Value { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.Constant( Value );

}
