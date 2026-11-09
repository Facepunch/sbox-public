using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The Avatar page's camera - where it goes for each slot, and for what's being tried on
/// </summary>
partial class AvatarShelf
{
	/// <summary>
	/// Where the camera goes for a slot - how high it looks from and how far back, or for the head's, just how far back
	/// from their head, which it's centred on wherever it is. The whole of you for the rest.
	/// </summary>
	static (float Height, float Distance, bool Head) FramingFor( AvatarClothingCategory.Category category ) => category?.Name switch
	{
		"eyes" => (0, 24, true),
		"makeup" or "complexion" or "facialhair" => (0, 28, true),
		"hair" or "hat" or "glasses" or "ear" or "piercing" or "head" => (0, 40, true),
		"neck" => (54, 70, false),
		"tops" or "outwear" or "underwear" => (46, 120, false),
		"wrist" or "hands" => (40, 115, false),
		"bottoms" => (26, 125, false),
		"shoes" => (10, 90, false),
		_ => (32, 150, false)
	};

	void FrameOn( AvatarClothingCategory.Category category )
	{
		if ( !Model.IsValid() ) return;

		var (height, distance, head) = FramingFor( category );
		if ( head ) Model.FrameHead( distance );
		else Model.Frame( height, distance );
	}

	static AvatarClothingCategory.Category SlotFor( Clothing clothing ) =>
		Groups.SelectMany( CategoriesOf ).FirstOrDefault( x => Covers( x ).Contains( clothing.Category ) );

	/// <summary>
	/// The camera on what's being tried on, as picking its slot would put it - when no slot's picked, which has the
	/// camera on it already
	/// </summary>
	void FrameOnItem( Clothing clothing )
	{
		if ( Category is not null || clothing is null ) return;

		FrameOn( SlotFor( clothing ) );
	}
}
