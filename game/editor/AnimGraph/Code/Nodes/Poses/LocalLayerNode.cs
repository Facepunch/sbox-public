using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Defines a local animation layer with a pose, blend mode, optional weight, root-motion weight, and bone mask.
/// <br/><br/><b>Use when:</b>&#160;Adding an overlay, additive, or model-space pose through a Layer Blend node.
/// <br/><b>Important:</b>&#160;Must connect to Layer Blend. Synchronized layers follow the base pose's sync timing, and model-space layers require a bone mask.
/// </summary>
[Title( "Local Layer" ), Group( "Layers" ), Icon( "layers" )]
public class LocalLayerNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Weight { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Root Motion Weight" )]
	public NodeInput RootMotionWeight { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "Bone Mask" )]
	public NodeInput BoneMask { get; set; }

	[Property]
	public GraphBuilder.LayerBlendMode BlendMode { get; set; } = GraphBuilder.LayerBlendMode.Overlay;

	[Property]
	public bool IsSynchronized { get; set; }

	[Property]
	public bool IgnoreEvents { get; set; }

	[Output( typeof( LayerLink ) )]
	[JsonIgnore]
	public object Layer => default;

	public override Color PrimaryColor => Color.Parse( "#ff6347" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		compiler.Warn( "Local Layer must be connected to a Layer Blend node", this );
		return compiler.ReferencePose();
	}

}
