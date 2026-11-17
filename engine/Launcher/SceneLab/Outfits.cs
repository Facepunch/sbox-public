namespace Sandbox.SceneLab;

/// <summary>
/// Dressing citizens in the citizen addon's clothing, for the scenes that dress them.
/// </summary>
internal static class Outfits
{
	internal static readonly string[] Default =
	[
		"models/citizen_clothes/jacket/Hoodie/hoodie.clothing",
		"models/citizen_clothes/trousers/Jeans/jeans.clothing",
		"models/citizen_clothes/shoes/Trainers/trainers.clothing",
		"models/citizen_clothes/hat/Baseball_Cap/baseball_cap.clothing",
	];

	/// <summary>
	/// Dress a body in an outfit, as <c>ClothingContainer.Apply</c> did: its appearance, then its clothing (<see cref="Dresser"/>).
	/// </summary>
	internal static void Dress( ClothingContainer outfit, SkinnedModelRenderer body )
	{
		var dresser = Dresser.GetOrCreate( body );
		outfit.Normalize();
		dresser.UpdateAppearance( outfit );
		dresser.Apply( outfit );
	}

	/// <summary>
	/// An outfit of the citizen addon's clothing definitions.
	/// </summary>
	internal static ClothingContainer Outfit( IEnumerable<string> paths )
	{
		var outfit = new ClothingContainer();
		foreach ( var path in paths )
		{
			if ( GameScenes.LoadResource<Clothing>( path, "addons/citizen/Assets" ) is { } clothing ) outfit.Add( new ClothingContainer.ClothingEntry( clothing ) );
		}

		return outfit;
	}
}
