using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the ID of the highest-priority ID animation event currently sampled, or <b>Default Value</b> when none qualify.
/// <br/><br/><b>Use when:</b>&#160;Reading an ID carried by the active animation events.
/// <br/><b>Important:</b>&#160;Priority can prefer the greatest event weight or greatest percentage through. This node reads ID animation events, not graph events.
/// </summary>
[Title( "ID Event Value" ), Group( "Events" ), Icon( "label" )]
public class IDEventNode : Node
{
	/// <summary>
	/// The ID this node returns when nothing matches.
	/// </summary>
	[Property]
	public string DefaultValue { get; set; } = "";

	/// <summary>
	/// When used in a transition, limit the search to the source state only.
	/// Useful to ignore events from lower layers.
	/// </summary>
	[Property]
	public bool LimitSearchToSourceState { get; set; }

	[Property]
	public GraphBuilder.EventPriority PriorityRule { get; set; } = GraphBuilder.EventPriority.HighestWeight;

	/// <summary>
	/// Ignore any events from states that we are transitioning away from.
	/// </summary>
	[Property]
	public bool IgnoreInactiveBranchEvents { get; set; }

	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.IdEvent(
		DefaultValue,
		PriorityRule,
		new GraphBuilder.EventQuerySettings
		{
			SourceState = compiler.SourceState,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );

}
