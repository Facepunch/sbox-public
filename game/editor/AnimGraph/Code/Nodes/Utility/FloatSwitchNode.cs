using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs True Value or False Value according to a boolean condition, optionally easing linearly between them.
/// <br/><br/><b>Use when:</b>&#160;Turning a boolean state into a float such as a blend weight.
/// <br/><b>Important:</b>&#160;Ease Time at or below zero switches immediately. A disconnected condition uses true.
/// </summary>
[Title( "Float Switch" ), Group( "Values" ), Icon( "toggle_on" )]
public class FloatSwitchNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Condition { get; set; }

	[Property]
	public float TrueValue { get; set; } = 1.0f;

	[Property]
	public float FalseValue { get; set; } = 0.0f;

	/// <summary>
	/// Time in seconds to ease between values when the condition changes. Zero snaps instantly.
	/// </summary>
	[Property]
	public float EaseTime { get; set; } = 0.2f;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var condition = compiler.EmitValue( this, Condition );
		if ( condition is null )
			condition = compiler.ConstBool( true );
		var result = compiler.Builder.Switch( condition, TrueValue, FalseValue );
		return EaseTime <= 0.0f
			? result
			: compiler.Builder.Ease( result, new GraphBuilder.EaseSettings { Time = EaseTime } );
	}

}
