using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs externally supplied poses, root motion and an optional bone mask through a named runtime slot.
/// <br/><br/><b>Use when:</b>&#160;Native code needs to provide poses directly to a blend tree.
/// <br/><b>Important:</b>&#160;Poses persist until replaced or cleared. Root motion is consumed once per update. Slot names must be unique.
/// </summary>
[Title( "External Pose" ), Group( "Animation" ), Icon( "input" )]
[AvailableIn( GraphKind.BlendTree )]
public class ExternalPoseNode : Node
{
	/// <summary> Slot id game code uses to set the pose. Must be unique. </summary>
	[Property]
	public string SlotName { get; set; } = "External Pose";

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	protected override DisplayInfo DefaultDisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			if ( !string.IsNullOrWhiteSpace( SlotName ) )
				info.Name = $"External Pose: {SlotName}";
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		return compiler.RegisterExternalPoseSlot( SlotName, this )
			? compiler.Builder.ExternalPose( SlotName )
			: compiler.ReferencePose();
	}

}
