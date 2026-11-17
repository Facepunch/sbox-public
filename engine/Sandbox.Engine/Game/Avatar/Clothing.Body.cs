using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public sealed partial class Clothing
{
	/// <summary>
	/// Compatibility accessor for Type. True selects Player Model; false selects Clothing.
	/// </summary>
	[Hide]
	[JsonPropertyName( "HasHumanSkin" )]
	public bool ReplacesBody
	{
		get => Type == ClothingType.PlayerModel;
		set => Type = value ? ClothingType.PlayerModel : ClothingType.Clothing;
	}

	/// <summary>
	/// Skeletal model to use for the replacement body.
	/// </summary>
	[Group( "Player Model", Order = -50 ), Order( 0 ), Title( "Model" ), ResourceType( "vmdl" )]
	[ShowIf( nameof( Type ), ClothingType.PlayerModel )]
	[JsonPropertyName( "HumanSkinModel" )]
	public string BodyModel { get; set; }

	/// <summary>
	/// Bodygroup choices. Null uses the model's defaults; an explicit zero mask is preserved.
	/// </summary>
	[Group( "Player Model", Order = -50 ), Order( 1 ), Title( "Bodygroups" )]
	[ShowIf( nameof( Type ), ClothingType.PlayerModel )]
	[Model.BodyGroupMask( ModelParameter = nameof( BodyModel ) )]
	[JsonPropertyName( "HumanSkinBodyGroups" )]
	public ulong? BodyGroupMask { get; set; }

	/// <summary>
	/// Material group used by the replacement body, independently of the garment's material group.
	/// </summary>
	[Group( "Player Model", Order = -50 ), Order( 2 ), Title( "Material Group" )]
	[ShowIf( nameof( Type ), ClothingType.PlayerModel )]
	[Model.MaterialGroup( ModelParameter = nameof( BodyModel ) )]
	[JsonPropertyName( "HumanSkinMaterialGroup" )]
	public string BodyMaterialGroup { get; set; } = "default";

	/// <summary>
	/// Tags used to select garment variants, such as female.
	/// </summary>
	[Feature( "Advanced" ), Category( "Tags & Condition" ), Title( "Tags" )]
	[ShowIf( nameof( Type ), ClothingType.PlayerModel )]
	[JsonPropertyName( "HumanSkinTags" )]
	public TagSet BodyTags { get; set; } = [];

	/// <summary>
	/// Allowed garment slots across both layers. All accepts every garment; None accepts no clothing.
	/// </summary>
	[Group( "Player Model", Order = -50 ), Order( 5 ), BitFlags]
	[ShowIf( nameof( Type ), ClothingType.PlayerModel )]
	public Slots AllowedSlots { get; set; } = Slots.All;

	/// <summary>
	/// Returns whether this player model permits a garment's slots. Does not check mesh or skeleton compatibility.
	/// </summary>
	public bool CanWear( Clothing garment )
	{
		if ( !ReplacesBody || garment is null || garment.ReplacesBody )
			return false;

		var slots = garment.SlotsUnder | garment.SlotsOver;
		return AllowedSlots == Slots.All
			|| (slots != Slots.None && (slots & ~AllowedSlots) == 0);
	}

	/// <summary>
	/// Compatibility alias for ReplacesBody.
	/// </summary>
	[Obsolete( "Use ReplacesBody instead." ), Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )]
	public bool HasHumanSkin { get => ReplacesBody; set => ReplacesBody = value; }

	/// <summary>
	/// Compatibility alias for BodyModel.
	/// </summary>
	[Obsolete( "Use BodyModel instead." ), Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )]
	public string HumanSkinModel { get => BodyModel; set => BodyModel = value; }

	/// <summary>
	/// Compatibility alias for BodyGroupMask. Unspecified model defaults are reported as zero by this legacy API.
	/// </summary>
	[Obsolete( "Use BodyGroupMask instead." ), Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )]
	public ulong HumanSkinBodyGroups { get => BodyGroupMask ?? 0; set => BodyGroupMask = value; }

	/// <summary>
	/// Compatibility alias for BodyMaterialGroup.
	/// </summary>
	[Obsolete( "Use BodyMaterialGroup instead." ), Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )]
	public string HumanSkinMaterialGroup { get => BodyMaterialGroup; set => BodyMaterialGroup = value; }

	/// <summary>
	/// Compatibility alias for BodyTags.
	/// </summary>
	[Obsolete( "Use BodyTags instead." ), Hide, JsonIgnore, EditorBrowsable( EditorBrowsableState.Never )]
	public TagSet HumanSkinTags { get => BodyTags; set => BodyTags = value; }

	/// <summary>
	/// Legacy Human skin material path. Retained verbatim for existing resources and code.
	/// </summary>
	[Obsolete( "Use BodyMaterialGroup instead." ), Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public string HumanSkinMaterial { get; set; }

	/// <summary>
	/// Legacy Human eye material path. Retained verbatim for existing resources and code.
	/// </summary>
	[Obsolete( "Use BodyMaterialGroup instead." ), Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public string HumanEyesMaterial { get; set; }

	/// <summary>
	/// New resources use Type; old resources can still choose their type through HasHumanSkin.
	/// </summary>
	protected override void OnJsonDeserialize( JsonObject node )
	{
		base.OnJsonDeserialize( node );

		Type = ClothingType.Clothing;
		AllowedSlots = Slots.All;
		BodyGroupMask = null;

		// The explicit type wins when both new and legacy fields are present, regardless of JSON order.
		if ( node.ContainsKey( nameof( Type ) ) )
			node.Remove( "HasHumanSkin" );
	}

	/// <summary>
	/// Omit an unspecified mask so existing metadata consumers with a non-nullable mask can read new resources.
	/// </summary>
	protected override void OnJsonSerialize( JsonObject node )
	{
		base.OnJsonSerialize( node );

		if ( BodyGroupMask is null )
			node.Remove( "HumanSkinBodyGroups" );
	}
}
