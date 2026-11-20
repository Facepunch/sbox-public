namespace Sandbox.Npcs;

/// <summary>
/// A designed outfit - a set of clothing to dress an NPC in. Just clothes: heads, hair and facial
/// hair come from a <see cref="FacialAppearance"/>.
/// </summary>
public sealed class ClothingPreset
{
	public string Name { get; set; }
	public List<Clothing> Clothing { get; set; } = [];
}

/// <summary>
/// A head with the hair and beard that go with it, picked together - so nobody ends up with jet
/// black hair and a blonde beard, or a beard that doesn't suit their face.
/// </summary>
public sealed class FacialAppearance
{
	public string Name { get; set; }

	/// <summary>The head (skin clothing).</summary>
	public Clothing Head { get; set; }

	/// <summary>Hair, if any.</summary>
	public Clothing Hair { get; set; }

	/// <summary>Beard, moustache or the like, if any. Coloured to match the hair.</summary>
	public Clothing Beard { get; set; }

	/// <summary>
	/// Where on the hair's colour range to tint it - 0 is white, through blonde, brown and ginger, to
	/// black at 1. The beard is matched to whatever colour that gives.
	/// </summary>
	[Range( 0, 1 )]
	public float HairTint { get; set; } = 0.5f;
}

/// <summary>
/// Dresses an NPC in one of a few designed outfits and one of a few facial appearances, each picked
/// at random when it spawns. Replaces whatever's set on the <see cref="Sandbox.Dresser"/>, which
/// still does the actual dressing.
/// </summary>
public sealed class NpcOutfits : Component
{
	/// <summary>
	/// The dresser to put the outfit on.
	/// </summary>
	[Property]
	public Dresser Dresser { get; set; }

	/// <summary>
	/// Outfits to pick from. Empty leaves the dresser's clothes as they are.
	/// </summary>
	[Property]
	public List<ClothingPreset> Outfits { get; set; } = [];

	/// <summary>
	/// Heads with their hair and beard, picked on top of the outfit. Empty leaves the face alone.
	/// Headgear in the outfit wins: hair or a beard that can't be worn with it is left off.
	/// </summary>
	[Property]
	public List<FacialAppearance> FacialAppearances { get; set; } = [];

	protected override void OnValidate()
	{
		base.OnValidate();

		// Runs when the NPC is cloned in, before it's network spawned - so the outfit goes out in the
		// spawn snapshot and every client dresses it the same. Only in the game scene - not in the
		// editor, the cached prefab itself, thumbnails or other preview scenes, and not on clients.
		if ( Game.IsPlaying && Scene.IsValid() && !Scene.IsEditor && Scene is not PrefabScene && Scene == Game.ActiveScene && Networking.IsHost && !GameObject.Network.Active )
			PickOutfit();

		if ( Scene.IsValid() && Scene.IsEditor )
			WarnAboutClashes();
	}

	// While designing: flag outfits with items that can't be worn together - the dresser would drop one
	private void WarnAboutClashes()
	{
		foreach ( var outfit in Outfits ?? [] )
		{
			var items = outfit?.Clothing?.Where( x => x is not null ).ToList();
			if ( items is null ) continue;

			for ( var i = 0; i < items.Count; i++ )
			{
				for ( var j = i + 1; j < items.Count; j++ )
				{
					if ( !items[i].CanBeWornWith( items[j] ) )
						Log.Warning( $"{GameObject.Name}: outfit '{outfit.Name}' - {items[i].Title} can't be worn with {items[j].Title}, the dresser will drop one." );
				}
			}
		}
	}

	private void PickOutfit()
	{
		if ( !Dresser.IsValid() )
			return;

		var clothing = Dresser.Clothing.Select( x => x.Clothing ).Where( x => x is not null ).ToList();

		if ( Outfits is { Count: > 0 } && Pick( Outfits )?.Clothing is { Count: > 0 } outfit )
			clothing = outfit.Where( x => x is not null ).ToList();

		var look = FacialAppearances is { Count: > 0 } ? Pick( FacialAppearances ) : null;
		if ( look is not null )
		{
			// The look replaces any head, hair and facial hair already there
			clothing.RemoveAll( IsFacial );

			if ( look.Head is not null )
				clothing.Add( look.Head );

			AddIfItFits( clothing, look.Hair );

			// Masks and balaclavas say a beard fits, but it pokes straight through them
			if ( !clothing.Any( CoversFace ) )
				AddIfItFits( clothing, look.Beard );
		}

		// Keep the dresser's own settings (tint etc.) for anything it already had on
		var existing = Dresser.Clothing.Where( x => x.Clothing is not null ).DistinctBy( x => x.Clothing ).ToDictionary( x => x.Clothing );
		var entries = clothing
			.Select( x => existing.GetValueOrDefault( x ) ?? new ClothingContainer.ClothingEntry( x ) )
			.ToList();

		if ( look is not null )
		{
			var hair = entries.FirstOrDefault( x => x.Clothing == look.Hair );
			var beard = entries.FirstOrDefault( x => x.Clothing == look.Beard );

			if ( hair is not null )
				hair.Tint = look.HairTint;
			else if ( beard is not null )
				beard.Tint = look.HairTint;
		}

		MatchFacialHair( entries );
		Dresser.Clothing = entries;
	}

	private static bool IsFacial( Clothing clothing )
	{
		var category = clothing.Category.ToString();
		return clothing.Category == Clothing.ClothingCategory.Skin || category.StartsWith( "Hair" ) || category.StartsWith( "FacialHair" );
	}

	private const Clothing.Slots FaceSlots = Clothing.Slots.HeadBottom | Clothing.Slots.Face | Clothing.Slots.Chin | Clothing.Slots.Lips | Clothing.Slots.Philtrum;

	// Balaclavas, gas masks, bandanas - anything over the lower face
	private static bool CoversFace( Clothing clothing )
	{
		return clothing.Category == Clothing.ClothingCategory.HeadSpecial
			|| ((clothing.SlotsOver | clothing.SlotsUnder) & FaceSlots) != 0;
	}

	// Headgear wins - leave hair or a beard off rather than have the dresser drop a hat for it
	private static void AddIfItFits( List<Clothing> clothing, Clothing item )
	{
		if ( item is not null && clothing.All( x => x.CanBeWornWith( item ) ) )
			clothing.Add( item );
	}

	/// <summary>
	/// Tint beards, moustaches and the like the same colour as the hair on their head. Each item has
	/// its own tint gradient, so match the colour rather than the tint value.
	/// </summary>
	private static void MatchFacialHair( List<ClothingContainer.ClothingEntry> entries )
	{
		var hair = entries.FirstOrDefault( x => x.Clothing is { } c && c.Category.ToString().StartsWith( "Hair" ) );
		if ( hair is null )
			return;

		var colour = hair.Clothing.TintSelection.Evaluate( hair.Tint ?? hair.Clothing.TintDefault );

		foreach ( var entry in entries )
		{
			if ( entry.Clothing is not { AllowTintSelect: true } c || !c.Category.ToString().StartsWith( "FacialHair" ) )
				continue;

			entry.Tint = ClosestTint( c.TintSelection, colour );
		}
	}

	private static float ClosestTint( Gradient gradient, Color colour )
	{
		var best = 0f;
		var bestDistance = float.MaxValue;

		for ( var t = 0f; t <= 1f; t += 0.01f )
		{
			var c = gradient.Evaluate( t );
			var distance = (c.r - colour.r) * (c.r - colour.r) + (c.g - colour.g) * (c.g - colour.g) + (c.b - colour.b) * (c.b - colour.b);
			if ( distance < bestDistance )
			{
				bestDistance = distance;
				best = t;
			}
		}

		return best;
	}

	private static T Pick<T>( List<T> list ) => list[Game.Random.Int( 0, list.Count - 1 )];
}
