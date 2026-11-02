using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Compares the source state's clip time, time in state, normalized progress, or sync-event progress against a fixed or driven value.
/// <br/><br/><b>Use when:</b>&#160;Gating a transition by playback progress or elapsed time.
/// <br/><b>Important:</b>&#160;Only valid inside a conduit transition graph; elsewhere it returns false.
/// </summary>
[Title( "Time Condition" ), Group( "State Machine" ), Icon( "schedule" )]
[AvailableIn( GraphKind.TransitionConduit )]
public class TimeConditionNode : Node, ITransitionScopedNode
{
	[Property]
	public GraphBuilder.StateMachineBuilder.TimeComparisonType Type { get; set; } = GraphBuilder.StateMachineBuilder.TimeComparisonType.ElapsedTime;

	[Property]
	public GraphBuilder.StateMachineBuilder.TimeComparison Operator { get; set; } = GraphBuilder.StateMachineBuilder.TimeComparison.GreaterThanEqual;

	[Property]
	public float Value { get; set; } = 1.0f;

	/// <summary>
	/// Optional float overriding the compare value.
	/// </summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput ValueInput { get; set; }

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Error( "Time Condition is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.TimeCondition(
			compiler.SourceState,
			new GraphBuilder.StateMachineBuilder.TimeConditionSettings
			{
				Type = Type,
				Comparison = Operator,
				Value = Value,
				InputValue = compiler.EmitValue( this, ValueInput ),
			} );
	}

}
