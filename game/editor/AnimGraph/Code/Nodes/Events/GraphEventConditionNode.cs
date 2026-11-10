using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true when sampled graph events match the configured event IDs and event-type filters.
/// <br/><br/><b>Use when:</b>&#160;Reacting to state entry, fully-in-state, exit, timed, or generic graph events.
/// <br/><b>Important:</b>&#160;<b>Or</b> requires one matching condition; <b>And</b> requires every condition during the same update. Blank IDs or an empty condition list return false.
/// </summary>
[Title( "Graph Event Condition" ), Group( "Events" ), Icon( "flag" )]
public class GraphEventConditionNode : Node
{
	public class Condition
	{
		[Property, KeyProperty]
		public string EventId { get; set; } = "";

		[Property, KeyProperty]
		public GraphBuilder.GraphEventType Type { get; set; } = GraphBuilder.GraphEventType.Any;
	}

	[Property]
	public GraphBuilder.EventMatch Operator { get; set; } = GraphBuilder.EventMatch.Or;

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

	[Property]
	public List<Condition> Conditions { get; set; } = [];

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var conditions = Conditions ?? [];
		if ( conditions.Count == 0 )
		{
			compiler.Warn( "Graph Event Condition has no conditions" );
			return compiler.ConstBool( false );
		}

		if ( conditions.Any( c => string.IsNullOrWhiteSpace( c.EventId ) ) )
		{
			compiler.Warn( "Graph Event Condition has a condition with no event ID" );
			return compiler.ConstBool( false );
		}

		var eventConditions = conditions.Select( condition => (condition.EventId, condition.Type) );

		return compiler.Builder.GraphEventCondition(
			eventConditions,
			Operator,
			new GraphBuilder.EventQuerySettings
			{
				SourceState = compiler.SourceState,
				LimitSearchToSourceState = LimitSearchToSourceState,
				IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
			} );
	}

}
