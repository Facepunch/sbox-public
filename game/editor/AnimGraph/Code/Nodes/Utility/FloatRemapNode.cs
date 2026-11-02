using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Maps a float from the input range into the output range, clamping its normalized input position to 0-1.
/// <br/><br/><b>Use when:</b>&#160;Converting values such as speed or distance into blend weights or another bounded scale.
/// <br/><b>Important:</b>&#160;Values outside the input range return the corresponding output endpoint. Reversed ranges are preserved.
/// </summary>
[Title( "Remap" ), Group( "Values" ), Icon( "swap_calls" )]
public class FloatRemapNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public float InputMin { get; set; }

	[Property]
	public float InputMax { get; set; } = 1.0f;

	[Property]
	public float OutputMin { get; set; }

	[Property]
	public float OutputMax { get; set; } = 1.0f;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.Remap(
			value,
			new Vector2( InputMin, InputMax ),
			new Vector2( OutputMin, OutputMax ) );
	}

}
