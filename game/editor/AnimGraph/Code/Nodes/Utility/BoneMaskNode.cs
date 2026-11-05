using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects one of the skeleton's authored bone masks. With no selection, it applies <b>Default Weight</b> to every bone.
/// <br/><br/><b>Use when:</b>&#160;Restricting a layer, transition or scale to part of the skeleton.
/// <br/><b>Important:</b>&#160;Masks are authored on the skeleton, not the model. A mask does nothing until something takes it - a Local Layer's Bone Mask input, for example.
/// </summary>
[Title( "Bone Mask" ), Group( "Bone Mask" ), Icon( "filter_alt" )]
public class BoneMaskNode : VariationDataNode<BoneMaskNode.Data>
{
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary>
		/// The bone mask to use for this variation. Leave empty to use the node's shared Mask.
		/// </summary>
		[Property, Editor( "animgraph.bonemask" )]
		public string MaskOverride { get; set; } = "";
	}

	/// <summary>
	/// Which of the skeleton's bone masks to use. Leave empty to weight every bone the same.
	/// </summary>
	[Property, Title( "Mask" ), Editor( "animgraph.bonemask" )]
	public string BoneName { get; set; } = "";

	/// <summary>
	/// Weight applied to every bone when no mask is set.
	/// </summary>
	[Property, Range( 0, 1 ), ShowIf( nameof( BoneName ), "" )]
	public float DefaultWeight { get; set; } = 1.0f;

	[Output( typeof( BoneMaskLink ) )]
	[JsonIgnore]
	public object Mask => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		var mask = string.IsNullOrWhiteSpace( data.MaskOverride ) ? BoneName : data.MaskOverride;

		return string.IsNullOrWhiteSpace( mask )
			? compiler.Builder.BoneMask( DefaultWeight )
			: compiler.Builder.BoneMask( mask );
	}

}
