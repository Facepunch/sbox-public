using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Samples a float-curve animation event at its current percentage through, using the connected or configured default when no event qualifies.
/// <br/><br/><b>Use when:</b>&#160;Reading authored animation-event curves as live graph values.
/// <br/><b>Important:</b>&#160;<b>Match Event ID</b> filters by exact ID; leave it empty to accept any float-curve event. Priority chooses by greatest weight or greatest percentage through.
/// </summary>
[Title( "Float Curve Event" ), Group( "Events" ), Icon( "show_chart" )]
public class FloatCurveEventNode : Node
{
	/// <summary>
	/// Optional float overriding the default value.
	/// </summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput Default { get; set; }

	/// <summary>
	/// Only sample float curve events with this ID. Leave empty to match any.
	/// </summary>
	[Property]
	public string MatchEventId { get; set; } = "";

	[Property]
	public float DefaultValue { get; set; }

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


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var defaultValue = compiler.EmitValue( this, Default );
		return compiler.Builder.CurveEvent( new GraphBuilder.CurveEventSettings
		{
			EventId = MatchEventId,
			DefaultValue = DefaultValue,
			DefaultValueNode = defaultValue,
			Priority = PriorityRule,
			LimitSearchToSourceState = LimitSearchToSourceState,
			IgnoreInactiveEvents = IgnoreInactiveBranchEvents,
		} );
	}

}
