namespace Sandbox.Services;

/// <summary>
/// Extracted metadata for a clothing package, read from its compiled .clothing_c resource.
/// Shared DTO (website + game engine). The enum types below mirror the engine's
/// Sandbox.Clothing definition, so the compiled .clothing_c parses straight into them.
/// </summary>
public class ClothingMetaData : BaseMetaData
{
	/// <summary>Display name shown in the dresser/store (resource "Title").</summary>
	public string Title { get; set; }

	/// <summary>Flavour text shown under the title (resource "Subtitle").</summary>
	public string Subtitle { get; set; }

	/// <summary>Slot category, e.g. Hat, Tops, Footwear.</summary>
	public ClothingCategory Category { get; set; }

	/// <summary>Finer grouping within the category, e.g. "Helmets".</summary>
	public string SubCategory { get; set; }

	/// <summary>Space-separated creator tags from the resource.</summary>
	public string Tags { get; set; }

	/// <summary>True if this item supplies its own human skin (body) instead of just an attachment.</summary>
	public bool HasHumanSkin { get; set; }

	/// <summary>Body groups toggled on when wearing the human-skin variant.</summary>
	public ulong HumanSkinBodyGroups { get; set; }

	/// <summary>Tags applied to the human-skin body.</summary>
	public string HumanSkinTags { get; set; }

	/// <summary>Material used for the eyes when this item replaces them.</summary>
	public string EyesMaterial { get; set; }

	/// <summary>Eyes material applied to the supplied human skin, if any.</summary>
	public string HumanEyesMaterial { get; set; }

	/// <summary>Body slots this item occupies on the outer layer.</summary>
	public Slots SlotsOver { get; set; }

	/// <summary>Body slots this item occupies on the inner layer.</summary>
	public Slots SlotsUnder { get; set; }

	/// <summary>Body regions hidden while this item is worn.</summary>
	public BodyGroups HideBody { get; set; }

	/// <summary>True if the player can recolour this item.</summary>
	public bool AllowTintSelect { get; set; }

	/// <summary>Heel height offset (footwear only; 0 otherwise).</summary>
	public float HeelHeight { get; set; }

	/// <summary>The Steam item definition id this clothing maps to, if any.</summary>
	public int? SteamItemDefinitionId { get; set; }

	/// <summary>Models swapped in under specific conditions, keyed by condition.</summary>
	public Dictionary<string, string> ConditionalModels { get; set; }

	/// <summary>
	/// True if this clothing package has been selected as a Steam workshop item (an ItemDef points
	/// at it). Mirrors <c>PackageFlags.WorkshopItemApproved</c> - projected on at index time by
	/// <c>Package.GetSearchIndex</c> so it stays current without re-extracting the asset.
	/// </summary>
	public bool WorkshopItemApproved { get; set; }

	public override void GetAutoTags( HashSet<string> tags )
	{
		SetTag( tags, "hashumanskin", HasHumanSkin );
		SetTag( tags, "tintable", AllowTintSelect );
		SetTag( tags, "approved", WorkshopItemApproved );
	}

	// Mirrors the engine's Sandbox.Clothing definition (Clothing.cs in sbox-engine): the compiled
	// .clothing_c serialises Category/SlotsUnder/SlotsOver/HideBody using these, either as enum
	// names ("Chin") or numeric flag values, so the types must match for the resource to parse.

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
}
