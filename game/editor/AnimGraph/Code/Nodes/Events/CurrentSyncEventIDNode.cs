using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Returns the ID of the source state's current sync event.
/// <br/><br/><b>Use when:</b>&#160;Comparing or forwarding the synchronization marker active in a transition's source state.
/// <br/><b>Important:</b>&#160;Only valid inside a conduit's transition graph; elsewhere it returns an empty ID.
/// </summary>
[Title( "Current Sync Event ID" ), Group( "Events" ), Icon( "sync" )]
public class CurrentSyncEventIDNode : Node
{
	[Output( typeof( IDLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Warn( "Current Sync Event ID is only usable in a conduit's transition graph" );
			return compiler.ConstID();
		}

		return compiler.Builder.CurrentSyncEventId( compiler.SourceState );
	}

}
