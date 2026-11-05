using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the sampled percentage through an ID animation event matching <b>Event ID</b>, or -1 when no matching event is sampled.
/// <br/><br/><b>Use when:</b>&#160;Driving behavior from progress through a specifically identified animation event.
/// <br/><b>Important:</b>&#160;When several events match, the priority rule chooses by greatest weight or greatest percentage through.
/// </summary>
[Title( "ID Event Percentage Through" ), Group( "Events" ), Icon( "percent" )]
public class IDEventPercentageThroughNode : Node
{
	[Property]
	public string EventId { get; set; } = "";

	[Property]
	public GraphBuilder.EventPriority PriorityRule { get; set; } = GraphBuilder.EventPriority.HighestWeight;

	/// <summary>
	/// When used in a transition, limit the search to the source state only.
	/// Useful to ignore events from lower layers.
	/// </summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	/// <summary>
	/// Ignore any events from states that we are transitioning away from.
	/// </summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.IdEventPercentage(
		EventId,
		PriorityRule,
		new GraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );

}
