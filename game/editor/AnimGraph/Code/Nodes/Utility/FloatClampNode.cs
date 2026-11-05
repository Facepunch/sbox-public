using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Constrains a float to the configured Min and Max values.
/// <br/><br/><b>Use when:</b>&#160;Limiting a numeric control to an allowed range.
/// <br/><b>Important:</b>&#160;Values below Min return Min; values above Max return Max.
/// </summary>
[Title( "Clamp" ), Group( "Values" ), Icon( "compress" )]
public class FloatClampNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public float Min { get; set; }

	[Property]
	public float Max { get; set; } = 1.0f;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.Clamp( value, Min, Max );
	}

}
