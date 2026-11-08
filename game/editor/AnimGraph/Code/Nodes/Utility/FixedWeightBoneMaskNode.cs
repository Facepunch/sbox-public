using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Produces a bone mask that applies the same configured weight to every bone.
/// <br/><br/><b>Use when:</b>&#160;A uniform full-skeleton mask is needed, including fully disabled or fully enabled masks.
/// </summary>
[Title( "Fixed Weight Bone Mask" ), Group( "Bone Mask" ), Icon( "opacity" )]
public class FixedWeightBoneMaskNode : Node
{
	[Property]
	public float BoneWeight { get; set; } = 0.0f;

	[Output( typeof( BoneMaskLink ) ), Title( "Bone Mask" )]
	[JsonIgnore]
	public object Mask => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
		=> compiler.Builder.BoneMask( BoneWeight );

}
