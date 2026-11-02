using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Normalizes an angle in degrees to [-180, 180] or [0, 360], or flips it to the opposite hemisphere with optional negation.
/// <br/><br/><b>Use when:</b>&#160;Normalizing angular parameters or expressing the opposite facing hemisphere.
/// </summary>
[Title( "Angle Math" ), Group( "Values" ), Icon( "rotate_right" )]
public class FloatAngleMathNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	[Property]
	public GraphBuilder.AngleOperation Operation { get; set; } = GraphBuilder.AngleOperation.ClampTo180;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.AngleMath( value, Operation );
	}

}
