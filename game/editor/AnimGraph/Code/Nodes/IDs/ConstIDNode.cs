using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs a constant ID token.
/// <br/><br/><b>Use when:</b>&#160;Supplying a fixed named state, action, or identifier to another node.
/// </summary>
[Title( "ID" ), Group( "Values" ), Icon( "label" )]
public class ConstIDNode : Node
{
	[Property]
	public string Value { get; set; } = "";

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.ConstantId( Value );

}
