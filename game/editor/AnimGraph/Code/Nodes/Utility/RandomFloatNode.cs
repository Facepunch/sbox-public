using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

[Title( "Random Float" ), Group( "Values" ), Icon( "casino" )]
public class RandomFloatNode : Node
{
	[Property]
	public float Min { get; set; }

	[Property]
	public float Max { get; set; } = 1.0f;

	[Output( typeof( FloatLink ) ), JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.RandomFloat( Min, Max );

}
