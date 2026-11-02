using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Outputs identity transforms for every bone: zero translation, identity rotation, and zero scale.
/// <br/><br/><b>Use when:</b>&#160;A neutral transform contribution is needed, especially as an empty additive or layered pose.
/// <br/><b>Important:</b>&#160;This is not the skeleton's bind pose; use Reference Pose when the authored reference pose is required.
/// </summary>
[Title( "Zero Pose" ), Group( "Animation" ), Icon( "accessibility_new" )]
public class ZeroPoseNode : Node
{
	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;


	internal override GraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.ZeroPose();

}
