using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true when the source state reaches the point where its transition should begin so the blend finishes at the state's end.
/// <br/><br/><b>Use when:</b>&#160;Leaving a state automatically as its animation completes.
/// <br/><b>Important:</b>&#160;By default it fires early by the transition duration. <b>At Clip End</b> waits for the exact end or loop boundary. Only valid inside a conduit transition graph.
/// </summary>
[Title( "State Completed" ), Group( "State Machine" ), Icon( "task_alt" )]
[AvailableIn( GraphKind.TransitionConduit )]
public class StateCompletedConditionNode : Node, ITransitionScopedNode
{
	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	/// <summary>
	/// Fire at the exact end of the clip instead of early by the transition's
	/// blend duration.
	/// </summary>
	[Property]
	public bool AtClipEnd { get; set; }

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Error( "State Completed is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.StateCompletedCondition(
			compiler.SourceState,
			AtClipEnd ? 0.0f : compiler.TransitionDuration,
			AtClipEnd ? default : compiler.TransitionDurationOverride );
	}

}
