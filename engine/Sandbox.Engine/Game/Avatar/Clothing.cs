using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Describes an item of clothing and implicitly which other items it can be worn with
/// </summary>
[AssetType( Name = "Clothing Definition", Extension = "clothing", Category = "citizen", Flags = AssetTypeFlags.IncludeThumbnails )]
public sealed partial class Clothing : GameResource
{

	[FeatureEnabled( "Human Skin" )]
	public bool HasHumanSkin { get; set; }

	/// <summary>
	/// Model to replace the human skin with
	/// </summary>
	[ResourceType( "vmdl" )]
	[Feature( "Human Skin" )] public string HumanSkinModel { get; set; }

	/// <summary>
	/// Replace skin with this
	/// </summary>
	[ResourceType( "vmdl" )]
	[Feature( "Human Skin" )] public string HumanSkinMaterial { get; set; }

	/// <summary>
	/// Replace skin with this
	/// </summary>
	[ResourceType( "vmdl" )]
	[Feature( "Human Skin" )] public string HumanEyesMaterial { get; set; }

	/// <summary>
	/// Bodygroup on the model to choose
	/// </summary>
	[Model.BodyGroupMask( ModelParameter = "HumanSkinModel" )]
	[Feature( "Human Skin" )] public ulong HumanSkinBodyGroups { get; set; }

	/// <summary>
	/// Bodygroup on the model to choose
	/// </summary>
	[Model.MaterialGroup( ModelParameter = "HumanSkinModel" )]
	[Feature( "Human Skin" )] public string HumanSkinMaterialGroup { get; set; }

	/// <summary>
	/// Allows adding tags for this skin, ie "female". This affects which alternative clothing models are used with it.
	/// </summary>
	[Feature( "Human Skin" )] public TagSet HumanSkinTags { get; set; }



	/// <summary>
	/// Name of the clothing to show in UI.
	/// </summary>
	[Feature( "Display", Description = "The display information used in the Avatar Menu" )]
	public string Title { get; set; }

	/// <summary>
	/// A subtitle for this clothing piece.
	/// </summary>
	[Feature( "Display" )]
	public string Subtitle { get; set; }

	/// <summary>
	/// What kind of clothing this is?
	/// </summary>
	[Feature( "Display" )]
	public ClothingCategory Category { get; set; }

	/// <summary>
	/// A list of conditional models.
	/// (key) = tag(s), (value) = model
	/// </summary>
	[Category( "Tags & Condition" )]
	public Dictionary<string, string> ConditionalModels { get; set; }

	[Category( "Tags & Condition" )]
	[Editor( "tags" )]
	public string Tags { get; set; }

	public string GetModel( IEnumerable<Clothing> clothingList ) => GetModel( clothingList, default );

	/// <summary>
	///  Tries to get the model for this current clothing. Takes into account any conditional model for other clothing.
	/// </summary>
	public string GetModel( IEnumerable<Clothing> clothingList, TagSet tagset )
	{
		// Example: We're trying to get a hair model.
		// One of the clothing items has a hat tag. So we check the conditional models on our hair
		// If it has hat, we use the hat conditional model.

		// TODO: Don't cycle through a clothing list. Have an Avatar which holds the tags based on the clothing we have.
		// I'm purposefully not doing it right now though, better to wait for Entity stuff to get gone.

		foreach ( var cloth in clothingList )
		{
			if ( !cloth.IsValid() ) continue;
			if ( string.IsNullOrEmpty( cloth.Tags ) ) continue;

			// String array for the current clothing item's tags, so we can check them all.
			var tags = cloth.Tags.Split( " " );

			foreach ( var tag in tags )
			{
				// Do we have a conditional model on this clothing item that matches a tag?
				if ( ConditionalModels != null && ConditionalModels.TryGetValue( tag, out var conditionalModelPath ) )
				{
					return conditionalModelPath;
				}
			}
		}

		if ( tagset is not null )
		{
			if ( tagset.Contains( "human" ) )
			{
				if ( tagset.Contains( "female" ) && !string.IsNullOrWhiteSpace( HumanAltFemaleModel ) )
				{
					return HumanAltFemaleModel;
				}

				return HumanAltModel;
			}
		}



		// Otherwise just use the default model
		return Model;
	}

	/// <summary>
	/// This should be a single word to describe the subcategory, and should match any other items you want to categorize in the same bunch. The work will be tokenized so it can become localized.
	/// </summary>
	[Obsolete]
	[JsonIgnore, Hide]
	public string SubCategory { get; set; }

	/// <summary>
	/// The clothing to parent this too.  It will be displayed as a variation of its parent
	/// </summary>
	[Obsolete]
	[JsonIgnore, Hide]
	public Clothing Parent { get; set; }

	/// <summary>
	/// The model to bonemerge to the player when this clothing is equipped.
	/// </summary>
	[ResourceType( "vmdl" )]
	[Category( "Clothing Setup" )]
	public string Model { get; set; }

	/// <summary>
	/// The model to bonemerge to the human player when this clothing is equipped.
	/// </summary>
	[ResourceType( "vmdl" )]
	[Category( "Clothing Setup" )]
	public string HumanAltModel { get; set; }

	/// <summary>
	/// The model to bonemerge to the human player when this clothing is equipped.
	/// </summary>
	[ResourceType( "vmdl" )]
	[Category( "Clothing Setup" )]
	public string HumanAltFemaleModel { get; set; }

	/// <summary>
	/// Replace the skin with this material
	/// </summary>
	[ResourceType( "vmat" )]
	[Category( "Clothing Setup" )]
	public string SkinMaterial { get; set; }

	/// <summary>
	/// Replace the eyes with this material
	/// </summary>
	[ResourceType( "vmat" )]
	[Category( "Clothing Setup" )]
	public string EyesMaterial { get; set; }

	/// <summary>
	/// Which material group of the model to use.
	/// </summary>
	[Category( "Clothing Setup" )]
	public string MaterialGroup { get; set; }

	/// <summary>
	/// Do we need to lift the heel up?
	/// </summary>
	[Category( "Clothing Setup" )]
	[Range( 0, 1 )]
	public float HeelHeight { get; set; }

	/// <summary>
	/// Which slots this clothing takes on "inner" layer.
	/// </summary>
	[BitFlags]
	[Category( "Body Slots" )]
	public Slots SlotsUnder { get; set; }

	/// <summary>
	/// Which slots this clothing takes on "outer" layer.
	/// </summary>
	[BitFlags]
	[Category( "Body Slots" )]
	public Slots SlotsOver { get; set; }

	/// <summary>
	/// Which body parts of the player model should not show when this clothing is equipped.
	/// </summary>
	[BitFlags]
	[Category( "Clothing Setup" )]
	public BodyGroups HideBody { get; set; }

	[Category( "User Customization" )]
	public bool AllowTintSelect { get; set; }

	[Category( "User Customization" )]
	[HideIf( nameof( AllowTintSelect ), false )]
	public Gradient TintSelection { get; set; } = Color.White;

	[Category( "User Customization" )]
	[HideIf( nameof( AllowTintSelect ), false )]
	[Range( 0, 1 )]
	public float TintDefault { get; set; } = 0.5f;

	/// <summary>
	/// The Steam Item Definition ID for this clothing item, if it's an inventory item
	/// </summary>
	[Category( "Steam Inventory" )]
	public int? SteamItemDefinitionId { get; set; }

	/// <summary>
	/// Can we wear this item?
	/// </summary>
	public bool HasPermissions()
	{
		if ( !SteamItemDefinitionId.HasValue ) return true;

		return Sandbox.Services.Inventory.HasItem( SteamItemDefinitionId.Value );
	}

	public enum ClothingCategory : int
	{
		None,
		Hat,
		HatCap = Hat,
		Hair,
		Skin,
		Footwear,
		Bottoms,
		Tops,
		Gloves,
		Facial,
		Eyewear,
		NecklaceChain,
		EarringStud,
		TShirt,
		Sweatshirt,
		Hoodie,
		Shirt,
		Vest,
		Knitwear,
		Jacket,
		Cardigan,
		Coat,
		Gilet,
		Shorts,
		Trousers,
		Jeans,
		Skirt,
		Socks,
		Heels,
		Sandals,
		Shoes,
		Trainers,
		Boots,
		Slippers,
		Underwear,
		Wristwear,
		Ring,
		Piercing,
		Headwear,
		Fullbody,
		Dress,
		Suit,
		Costume,
		Uniform,
		Bra,
		Underpants,
		HairShort,
		HairMedium,
		HairLong,
		HairUpdo,
		HairSpecial,
		Eyes,
		Eyebrows,
		Eyelashes,
		MakeupLips,
		MakeupEyeshadow,
		MakeupEyeliner,
		MakeupHighlighter,
		MakeupBlush,
		MakeupSpecial,
		ComplexionFreckles,
		ComplexionScars,
		ComplexionAcne,
		FacialHairMustache,
		FacialHairBeard,
		FacialHairStubble,
		FacialHairSideburns,
		FacialHairGoatee,
		GlassesEye,
		GlassesSun,
		GlassesSpecial,
		NecklacePendant,
		NecklaceSpecial,
		EarringDangle,
		EarringSpecial,
		HatBeanie,
		HatFormal,
		HatCostume,
		HatUniform,
		HatSpecial,
		HeadTech,
		HeadBand,
		HeadJewel,
		HeadSpecial,
		WristWatch,
		WristBand,
		WristJewel,
		WristSpecial,
		PierceNose,
		PierceEyebrow,
		PierceSpecial,
	}

	[Flags]
	public enum Slots : int
	{
		Skin = 1 << 0,
		HeadTop = 1 << 1,
		HeadBottom = 1 << 2,
		Face = 1 << 3,
		Chest = 1 << 4,
		LeftArm = 1 << 5,
		RightArm = 1 << 6,
		LeftWrist = 1 << 7,
		RightWrist = 1 << 8,
		LeftHand = 1 << 9,
		RightHand = 1 << 10,
		Groin = 1 << 11,
		LeftThigh = 1 << 12,
		RightThigh = 1 << 13,
		LeftKnee = 1 << 14,
		RightKnee = 1 << 15,
		LeftShin = 1 << 16,
		RightShin = 1 << 17,
		LeftFoot = 1 << 18,
		RightFoot = 1 << 19,
		Glasses = 1 << 20,
		EyeBrows = 1 << 21,
		Eyes = 1 << 22,
		Ears = 1 << 23,
		Lips = 1 << 24,
		Chin = 1 << 25,
		Philtrum = 1 << 26,
		Teeth = 1 << 27,
		Waist = 1 << 28,
	}

	[Flags]
	public enum BodyGroups : int
	{
		Head = 1 << 0,
		Chest = 1 << 1,
		Legs = 1 << 2,
		Hands = 1 << 3,
		Feet = 1 << 4,
	}

	/// <summary>
	/// Return true if this item of clothing can be worn with the target item, at the same time.
	/// </summary>
	public bool CanBeWornWith( Clothing target )
	{
		if ( target is null ) return true;
		if ( target == this ) return false;

		if ( (target.SlotsOver & SlotsOver) != 0 ) return false;
		if ( (target.SlotsUnder & SlotsUnder) != 0 ) return false;

		if ( HasHumanSkin && target.HasHumanSkin ) return false;

		return true;
	}

	/// <summary>
	/// Icon for this clothing piece.
	/// </summary>
	[Feature( "Icon", Description = "Generate the icon used for the clothing thumbnail" )]
	public IconSetup Icon { get; set; }

	public struct IconSetup
	{
		[Hide]
		public string Path { get; set; }
		public IconModes Mode { get; set; }
		public Vector3 PositionOffset { get; set; }

		[Expose]
		public enum IconModes
		{
			Generic,
			CitizenSkin,
			HumanSkin,
			Foot,
			Hand,
			Eyes,
			Head,
			Mouth,
			Chest,
			Wrist,
			Ear
		}
	}


	/// <summary>
	/// Dress this sceneobject with the passed clothes. Return the created clothing.
	/// </summary>
	[Obsolete( "We should be using Scene/Components now" )]
	public static List<SceneModel> DressSceneObject( SceneModel citizen, IEnumerable<Clothing> Clothing )
	{
		var created = new List<SceneModel>();
		return created;
	}

	/// <summary>
	/// Is this just a SteamItemDefinitionId. Usually created in the avatar editing scenario.
	/// </summary>
	internal bool IsPlaceholder()
	{
		if ( !SteamItemDefinitionId.HasValue ) return false;
		if ( SteamItemDefinitionId == 0 ) return false;
		if ( ResourceId != 0 ) return false;

		return true;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "checkroom", width, height, "#fdea60", "black" );
	}

}
