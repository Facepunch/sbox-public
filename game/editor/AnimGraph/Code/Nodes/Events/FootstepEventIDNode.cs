using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the sync-event ID stored by the highest-priority sampled foot event, or an empty ID when none qualify.
/// <br/><br/><b>Use when:</b>&#160;Reading the synchronization marker associated with the dominant foot event.
/// <br/><b>Important:</b>&#160;Priority can prefer the greatest event weight or greatest percentage through.
/// </summary>
[Title( "Footstep Event ID" ), Group( "Events" ), Icon( "label" )]
public class FootstepEventIDNode : Node
{
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

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.FootstepEventId(
		PriorityRule,
		new GraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );

}
