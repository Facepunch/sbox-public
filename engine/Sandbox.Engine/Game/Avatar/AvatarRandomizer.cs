using static Sandbox.Clothing;

namespace Sandbox;

/// <summary>
/// Utility class to make random outfits for the avatar. Uses a predefined set of groups and chances.
/// </summary>
internal static class AvatarRandomizer
{
	public struct Group
	{
		public float Chance;
		public ClothingCategory[] Categories;
	}

	private static readonly Group Head = new Group
	{
		Chance = 1.0f,
		Categories =
		[
			ClothingCategory.Hat,
			ClothingCategory.HatCap,
			ClothingCategory.Hair,
			ClothingCategory.HairShort,
			ClothingCategory.HairLong,
			ClothingCategory.HairMedium
		]
	};

	private static readonly Group Legs = new Group
	{
		Chance = 1.0f,
		Categories =
		[
			ClothingCategory.Trousers,
			ClothingCategory.Shorts,
			ClothingCategory.Underwear,
			ClothingCategory.Skirt
		]
	};

	private static readonly Group Torso = new Group
	{
		Chance = 1.0f,
		Categories =
		[
			ClothingCategory.Shirt,
			ClothingCategory.TShirt,
			ClothingCategory.Tops
		]
	};

	private static readonly Group Feet = new Group
	{
		Chance = 1.0f,
		Categories =
		[
			ClothingCategory.Shoes,
			ClothingCategory.Boots
		]
	};

	private static readonly Group Jackets = new Group
	{
		Chance = 0.5f,
		Categories =
		[
			ClothingCategory.Jacket,
			ClothingCategory.Vest,
			ClothingCategory.Coat,
			ClothingCategory.Cardigan
		]
	};

	private static readonly Group FacialHair = new Group
	{
		Chance = 0.3f,
		Categories =
		[
			ClothingCategory.FacialHairBeard,
			ClothingCategory.FacialHairGoatee,
			ClothingCategory.FacialHairSideburns
		]
	};

	private static readonly Group Glasses = new Group
	{
		Chance = 0.5f,
		Categories =
		[
			ClothingCategory.GlassesEye,
			ClothingCategory.GlassesSun
		]
	};

	private static readonly Group Gloves = new Group
	{
		Chance = 0.2f,
		Categories =
		[
			ClothingCategory.Gloves
		]
	};

	private static readonly Group[] Groups =
	{
		Head,
		Legs,
		Torso,
		Feet,
		Jackets,
		FacialHair,
		Glasses,
		Gloves
	};

	public static IEnumerable<ClothingContainer.ClothingEntry> GetRandom()
	{
		//
		// Not using Game.Random here -- this could be applied in the editor and we're not ticking in-game
		//
		var rnd = new Random();
		var all = ResourceLibrary.GetAll<Clothing>();

		foreach ( var rule in Groups )
		{
			if ( rnd.Float() > rule.Chance ) continue;

			var category = rnd.FromArray( rule.Categories );
			var options = all.Where( c => c.Category == category ).ToList();

			if ( options == null || options.Count == 0 )
			{
				continue;
			}

			var item = rnd.FromList( options );
			if ( !item.IsValid() ) continue;

			yield return new ClothingContainer.ClothingEntry( item )
			{
				Tint = rnd.Float()
			};
		}
	}
}
