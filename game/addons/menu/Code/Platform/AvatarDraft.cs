using Sandbox;

namespace MenuProject.Platform;

/// <summary>
/// The outfit being made on the Avatar page - what's on, what's being looked at under the pointer, and whether it's
/// been saved. Started from what you wear now.
/// </summary>
public sealed class AvatarDraft
{
	public ClothingContainer Container { get; private set; }

	public ClothingContainer Shown => preview ?? Container;

	ClothingContainer preview;
	string saved;

	/// <summary>
	/// Goes up each time what's shown changes - for the avatar to be dressed again
	/// </summary>
	public int Version { get; private set; }

	public bool HasChanges { get; private set; }

	public bool Saving { get; private set; }

	public AvatarDraft() => Load();

	/// <summary>
	/// What you wear now - thrown over anything not saved
	/// </summary>
	public void Load()
	{
		Container = ClothingContainer.CreateFromLocalUser();
		Container.PrefersHuman = true;
		saved = Container.Serialize();
		preview = null;
		Changed();
	}

	public bool IsWorn( Clothing clothing ) => Container.Has( clothing );

	/// <summary>
	/// What's on that putting <paramref name="clothing"/> on would take off - what shares a slot with it on the same
	/// layer, or another skin (ClothingContainer.Add). None for what's on already, which comes off itself.
	/// </summary>
	public List<Clothing> Replaces( Clothing clothing )
	{
		if ( clothing is null || IsWorn( clothing ) ) return [];

		return Container.Clothing
			.Select( x => x.Clothing )
			.Where( x => x is not null && !x.CanBeWornWith( clothing ) )
			.ToList();
	}

	/// <summary>
	/// Only what's yours, for now - trying on the shop's comes with buying it here.
	/// </summary>
	public bool Toggle( Clothing clothing )
	{
		if ( !AvatarCatalog.IsOwned( clothing ) )
			return false;

		Container.Toggle( clothing );
		preview = null;
		Changed();
		return true;
	}

	/// <summary>
	/// Where their eyes are on Dresser's eye colour ramp
	/// </summary>
	public float EyeColor
	{
		get => Container.EyeColor;
		set
		{
			if ( Container.EyeColor == value ) return;

			Container.EyeColor = value;
			Changed();
		}
	}

	public float GetTint( Clothing clothing ) => Container.FindEntry( clothing )?.Tint ?? clothing.TintDefault;

	public void SetTint( Clothing clothing, float tint )
	{
		if ( Container.FindEntry( clothing ) is not { } entry || entry.Tint == tint ) return;

		entry.Tint = tint;
		Changed();
	}

	public Color? TintColor( Clothing clothing )
	{
		if ( !clothing.AllowTintSelect || Container.FindEntry( clothing )?.Tint is not { } tint ) return null;

		return clothing.TintSelection.Evaluate( tint );
	}

	/// <summary>
	/// Their skin - one's always on, so it's only ever swapped for another
	/// </summary>
	public void SetSkin( Clothing skin )
	{
		if ( !skin.ReplacesBody || Container.Has( skin ) || !AvatarCatalog.IsOwned( skin ) ) return;

		Container.Toggle( skin );
		preview = null;
		Changed();
	}

	public void Preview( Clothing clothing )
	{
		if ( clothing is null )
		{
			if ( preview is null ) return;

			preview = null;
			Version++;
			return;
		}

		var outfit = new ClothingContainer { PrefersHuman = true };
		outfit.Deserialize( Container.Serialize() );

		if ( !outfit.Has( clothing ) )
			outfit.Toggle( clothing );

		preview = outfit;
		Version++;
	}

	public async Task Save()
	{
		if ( Saving ) return;

		Saving = true;

		try
		{
			saved = Container.Serialize();
			HasChanges = false;
			await MenuUtility.SaveAvatar( Container, true, 0 );
		}
		finally
		{
			Saving = false;
		}
	}

	public void Revert()
	{
		Container.Deserialize( saved );
		preview = null;
		Changed();
	}

	void Changed()
	{
		HasChanges = Container.Serialize() != saved;
		Version++;
	}
}
