using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Examines sampled transition animation events and tests their combined permission rule, optionally filtering by a specific marker ID.
/// <br/><br/><b>Use when:</b>&#160;Allowing or blocking state transitions from authored transition-event permissions.
/// <br/><b>Important:</b>&#160;Block is more restrictive than conditional allow, which is more restrictive than allow. The node returns false when no qualifying transition event is sampled.
/// </summary>
[Title( "Transition Event Condition" ), Group( "Events" ), Icon( "alt_route" )]
public class TransitionEventConditionNode : Node
{
	[Property]
	public GraphBuilder.TransitionRule RuleCondition { get; set; } = GraphBuilder.TransitionRule.AnyAllowed;

	[Property]
	public bool MatchOnlySpecificMarkerId { get; set; }

	[Property, ShowIf( nameof( MatchOnlySpecificMarkerId ), true )]
	public string MarkerIdToMatch { get; set; } = "";

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

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( MatchOnlySpecificMarkerId && string.IsNullOrWhiteSpace( MarkerIdToMatch ) )
		{
			compiler.Warn( "Transition Event Condition is set to match a specific marker ID but none is set" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.TransitionEventCondition(
			RuleCondition,
			MatchOnlySpecificMarkerId ? MarkerIdToMatch : null,
			new GraphBuilder.EventQuerySettings
			{
				SourceState = compiler.SourceState,
				LimitSearchToSourceState = LimitSearchToSourceState,
				IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
			} );
	}

}
