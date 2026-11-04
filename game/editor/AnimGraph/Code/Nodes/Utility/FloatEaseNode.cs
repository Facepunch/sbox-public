using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Eases from its current value toward the live input over <b>Ease Time</b> using the selected easing operation.
/// <br/><br/><b>Use when:</b>&#160;Smoothing abrupt changes in a float parameter.
/// <br/><b>Important:</b>&#160;A changed target restarts easing from the current output. Initialization can begin at Start Value or the input's initial value.
/// </summary>
[Title( "Ease" ), Group( "Values" ), Icon( "waves" )]
public class FloatEaseNode : Node
{
	[Input( typeof( FloatLink ) )]
	public NodeInput Value { get; set; }

	/// <summary>
	/// Time in seconds to reach a new target value.
	/// </summary>
	[Property]
	public float EaseTime { get; set; } = 0.3f;

	[Property]
	public GraphBuilder.Easing Easing { get; set; } = GraphBuilder.Easing.Linear;

	/// <summary>
	/// Start easing from a fixed value instead of the input's initial value.
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
		return compiler.Builder.Ease(
			value,
			new GraphBuilder.EaseSettings
			{
				Time = EaseTime,
				Easing = Easing,
				UseStartValue = UseStartValue,
				StartValue = StartValue,
			} );
	}

}
