using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Compares the source state's current sync-event index with a configured index using exact or greater-than-or-equal matching.
/// <br/><br/><b>Use when:</b>&#160;Allowing a transition at or after a specific synchronization marker.
/// <br/><b>Important:</b>&#160;Only valid inside a conduit's transition graph. A missing source state or negative index returns false.
/// </summary>
[Title( "Sync Event Index Condition" ), Group( "Events" ), Icon( "sync" )]
public class SyncEventIndexConditionNode : Node
{
	[Property]
	public GraphBuilder.SyncEventTrigger Mode { get; set; } = GraphBuilder.SyncEventTrigger.ExactlyAtEventIndex;

	[Property]
	public int SyncEventIndex { get; set; } = -1;

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Warn( "Sync Event Index Condition is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		if ( SyncEventIndex < 0 )
		{
			compiler.Warn( "Sync Event Index Condition has an invalid sync event index" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.SyncEventCondition(
			compiler.SourceState, SyncEventIndex, Mode );
	}

}
