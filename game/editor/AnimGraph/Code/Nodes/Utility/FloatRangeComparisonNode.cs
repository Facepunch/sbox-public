using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns whether a float lies between the configured Min and Max values.
/// <br/><br/><b>Use when:</b>&#160;Testing whether a parameter occupies a specific numeric interval.
/// <br/><b>Important:</b>&#160;Inclusive mode accepts the bounds; exclusive mode requires the value to be strictly inside them.
/// </summary>
[Title( "Float Range Check" ), Group( "Values" ), Icon( "linear_scale" )]
public class FloatRangeComparisonNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public float Min { get; set; }

	[Property]
	public float Max { get; set; } = 1.0f;

	/// <summary>
	/// Whether the range bounds count as inside.
	/// </summary>
	[Property]
	public bool Inclusive { get; set; } = true;

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.InRange( value, Min, Max, Inclusive );
	}

}
