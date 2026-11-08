using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns true when the current sampled events contain any or all configured IDs. The search can include graph events, ID animation events, or both.
/// <br/><br/><b>Use when:</b>&#160;Driving conditions and transitions from sampled event IDs.
/// <br/><b>Important:</b>&#160;<b>Or</b> requires one matching ID; <b>And</b> requires every listed ID during the same update. Empty IDs are ignored, and an empty final list returns false.
/// </summary>
[Title( "ID Event Condition" ), Group( "Events" ), Icon( "label_important" )]
public class IDEventConditionNode : Node
{
	[Property]
	public GraphBuilder.EventMatch Operator { get; set; } = GraphBuilder.EventMatch.Or;

	[Property]
	public GraphBuilder.EventSearch SearchRule { get; set; } = GraphBuilder.EventSearch.SearchAll;

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
	public List<string> EventIds { get; set; } = [];

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var ids = EventIds?.Where( id => !string.IsNullOrWhiteSpace( id ) ).ToList() ?? [];
		if ( ids.Count == 0 )
		{
			compiler.Warn( "ID Event Condition has no event IDs" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.IdEventCondition(
			ids,
			Operator,
			SearchRule,
			new GraphBuilder.EventQuerySettings
			{
				SourceState = compiler.SourceState,
				LimitSearchToSourceState = LimitSearchToSourceState,
				IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
			} );
	}

}
