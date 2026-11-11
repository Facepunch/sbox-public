using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Exposes a named runtime slot into which game code can connect another AnimGraph instance, using the fallback pose while disconnected.
/// <br/><br/><b>Use when:</b>&#160;Dynamically attaching or replacing animation behavior at runtime.
/// <br/><b>Important:</b>&#160;Use renderer.Animation.ExternalGraphs.Connect with this slot name. Connecting shuts down the fallback and resets the child; disconnecting reinitializes the fallback. Unlike Referenced Graph, parent parameters are not copied into external graphs. Slot names must be unique.
/// </summary>
[Title( "External Graph" ), Group( "Graphs" ), Icon( "extension" )]
[AvailableIn( GraphKind.BlendTree )]
public class ExternalReferencedGraphNode : Node
{
	/// <summary> Slot id game code uses to connect a graph. Must be unique. </summary>
	[Property]
	public string SlotName { get; set; } = "External Graph";

	/// <summary> Pose used while no graph is connected. </summary>
	[Input( typeof( PoseLink ) )]
	public NodeInput Fallback { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			if ( !string.IsNullOrWhiteSpace( SlotName ) )
				info.Name = $"External Graph: {SlotName}";
			return info;
		}
	}

	public override Color PrimaryColor => Color.Parse( "#9575cd" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var fallback = compiler.EmitValue( this, Fallback );
		return compiler.RegisterExternalGraphSlot( SlotName, this )
			? compiler.Builder.ExternalGraphSlot( SlotName, fallback )
			: fallback ?? compiler.ReferencePose();
	}

}
