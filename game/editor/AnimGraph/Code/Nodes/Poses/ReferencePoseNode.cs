using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs the skeleton's authored reference or bind pose.
/// <br/><br/><b>Use when:</b>&#160;A valid base pose, bind pose, or explicit fallback is required.
/// <br/><b>Important:</b>&#160;Unlike Zero Pose, this contains the skeleton's authored bone transforms.
/// </summary>
[Title( "Reference Pose" ), Group( "Animation" ), Icon( "accessibility_new" )]
public class ReferencePoseNode : Node
{
	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.ReferencePose();

}
