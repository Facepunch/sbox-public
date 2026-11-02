using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Tracks the live input with a damped spring. Hertz controls response frequency, while Damping Ratio controls decay and overshoot.
/// <br/><br/><b>Use when:</b>&#160;A numeric value should lag, settle, or overshoot with spring-like motion.
/// <br/><b>Important:</b>&#160;Initialization can begin at Start Value or the input's initial value, always with zero spring velocity.
/// </summary>
[Title( "Spring" ), Group( "Values" ), Icon( "vibration" )]
public class FloatSpringNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	/// <summary>
	/// Oscillation frequency - higher values track the input more tightly.
	/// </summary>
	[Property]
	public float Hertz { get; set; } = 4.0f;

	/// <summary>
	/// How quickly oscillation dies out. 1 is critically damped, below 1 overshoots.
	/// </summary>
	[Property]
	public float DampingRatio { get; set; } = 0.7f;

	/// <summary>
	/// Start from a fixed value instead of the input's initial value.
	/// </summary>
	[Property]
	public bool UseStartValue { get; set; }

	[Property, ShowIf( nameof( UseStartValue ), true )]
	public float StartValue { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var value = compiler.EmitValue( this, Value );
		if ( value is null )
			value = compiler.ConstFloat( 0.0f );
		return compiler.Builder.Spring(
			value,
			new GraphBuilder.SpringSettings
			{
				Hertz = Hertz,
				DampingRatio = DampingRatio,
				UseStartValue = UseStartValue,
				StartValue = StartValue,
			} );
	}

}
