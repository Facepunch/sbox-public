using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the source state's current sync position as its event index, percentage through that event, or index plus percentage.
/// <br/><br/><b>Use when:</b>&#160;Driving transition logic from the source state's synchronization position.
/// <br/><b>Important:</b>&#160;Only valid inside a conduit's transition graph; elsewhere it returns 0.
/// </summary>
[Title( "Current Sync Event Index" ), Group( "Events" ), Icon( "sync" )]
public class CurrentSyncEventNode : Node
{
	[Property]
	public GraphBuilder.SyncEventInfo Type { get; set; } = GraphBuilder.SyncEventInfo.IndexAndPercentage;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Warn( "Current Sync Event Index is only usable in a conduit's transition graph" );
			return compiler.ConstFloat( 0 );
		}

		return compiler.Builder.CurrentSyncEvent(
			compiler.SourceState, Type );
	}

}
