using Sandbox;
using static Sandbox.ClothingContainer;

public sealed partial class AvatarEditManager : Component
{
	[Header( "Bodies" )]
	[Property] public GameObject Citizen { get; set; }
	[Property] public GameObject Human { get; set; }
	[Property]
	public bool CitizenActive
	{
		get => !Container.PrefersHuman;
		set => Container.PrefersHuman = !value;
	}

	string lastSaved;
	public ClothingContainer Container { get; set; } = new ClothingContainer();
	public ClothingContainer PreviewContainer { get; set; } = new ClothingContainer();

	protected override void OnAwake()
	{
		BuildSteamInventoryClothing();

		Container = ClothingContainer.CreateFromLocalUser();
		lastSaved = Container.Serialize();

		ApplyChangesToModel();
		AvatarBackgroundRig.RestoreSaved();
	}

	List<Clothing> allClothing = new();

	void BuildSteamInventoryClothing()
	{
		foreach ( var c in ResourceLibrary.GetAll<Clothing>() )
		{
			if ( !c.ResourcePath.StartsWith( "models/citizen_clothes/" ) ) continue;

			allClothing.Add( c );
		}

		foreach ( var item in Sandbox.Services.Inventory.Definitions )
		{
			// Don't include any definitions that we already have as Clothing resources
			if ( allClothing.Any( x => x.SteamItemDefinitionId == item.Id ) )
				continue;

			var clothing = new Clothing();
			clothing.Title = item.Name;
			clothing.Category = Enum.TryParse<Clothing.ClothingCategory>( item.Category, out var category ) ? category : Clothing.ClothingCategory.HairLong;
			clothing.Icon = new Clothing.IconSetup() { Path = item.IconUrl };
			clothing.SteamItemDefinitionId = item.Id;

			if ( item.SellStart != null && item.SellStart > DateTime.UtcNow && !IsPurchased( clothing ) )
				continue;

			allClothing.Add( clothing );
		}
	}

	public IEnumerable<Clothing> GetAllClothing()
	{
		return allClothing;
	}

	protected override void OnUpdate()
	{
		Citizen.Enabled = CitizenActive;
		Human.Enabled = !CitizenActive;

		var active = CitizenActive ? Citizen : Human;
		var renderer = active.GetComponent<SkinnedModelRenderer>();

		UpdateEyes( renderer );
		UpdateCamera( renderer );
	}

	public bool IsSelected( Clothing clothing ) => Container.Has( clothing );

	public bool IsPurchased( Clothing item )
	{
		if ( !item.SteamItemDefinitionId.HasValue )
			return true;

		if ( Sandbox.Services.Inventory.HasItem( item.SteamItemDefinitionId.Value ) )
		{
			return true;
		}

		return false;
	}

	public string DisplayName
	{
		get => Container.DisplayName;
		set => Container.DisplayName = value;
	}

	public float Height
	{
		get => Container.Height;
		set
		{
			Container.Height = value;
			ApplyChangesToModel();
		}
	}

	public float Age
	{
		get => Container.Age;
		set
		{
			Container.Age = value;
			ApplyChangesToModel();
		}
	}

	public float Tint
	{
		get => Container.Tint;
		set
		{
			Container.Tint = value;
			ApplyChangesToModel();
		}
	}

	public void PreviewPackage( Package package )
	{
		if ( package == null )
		{
			RevertHovered();
			return;
		}

		MenuUtility.RunTask( () => PreviewPackageAsync( package ) );
	}

	public async Task PreviewPackageAsync( Package package )
	{
		var clothing = await Cloud.Load<Clothing>( package.FullIdent );
		OnClothingHover( clothing );
	}

	public void OnClothingHover( Clothing clothing )
	{
		if ( clothing == null )
		{
			RevertHovered();
			return;
		}

		PreviewContainer.Deserialize( Container.Serialize() );

		if ( !PreviewContainer.Has( clothing ) )
		{
			PreviewContainer.Toggle( clothing );
		}

		ApplyPreviewToModel();
	}

	public void SetTint( Clothing clothing, float f )
	{
		ClothingEntry e = Container.FindEntry( clothing );
		if ( e is null ) return;

		e.Tint = f;
		ApplyChangesToModel();
	}

	public float GetTint( Clothing clothing )
	{
		ClothingEntry e = Container.FindEntry( clothing );
		if ( e is not null && e.Tint.HasValue )
		{
			return e.Tint.Value;
		}

		return clothing.TintDefault;
	}

	public void OnClothingToggle( Clothing clothing )
	{
		if ( !IsPurchased( clothing ) )
		{
			// TODO - Pop up a shopping cart HA HA HA
			return;
		}

		RevertHovered();

		Container.Toggle( clothing );
		ApplyChangesToModel();
	}

	public void ApplyPreviewToModel()
	{
		// We have to run it this way so it'll be in the menu context
		MenuUtility.RunTask( () => ApplyAsync( PreviewContainer, Citizen.GetComponent<SkinnedModelRenderer>( true ) ) );
		MenuUtility.RunTask( () => ApplyAsync( PreviewContainer, Human.GetComponent<SkinnedModelRenderer>( true ) ) );
	}

	public void ApplyChangesToModel()
	{
		// We have to run it this way so it'll be in the menu context
		MenuUtility.RunTask( () => ApplyAsync( Container, Citizen.GetComponent<SkinnedModelRenderer>( true ) ) );
		MenuUtility.RunTask( () => ApplyAsync( Container, Human.GetComponent<SkinnedModelRenderer>( true ) ) );
	}

	async Task ApplyAsync( ClothingContainer container, SkinnedModelRenderer targetRenderer )
	{
		// apply the clothing
		await container.ApplyAsync( targetRenderer, default );
	}

	void RevertHovered()
	{
		ApplyChangesToModel();
	}

	public bool HasUnsavedChanges => lastSaved != Container.Serialize();

	public void SaveChanges()
	{
		lastSaved = Container.Serialize();
		ApplyChangesToModel();

		_ = MenuUtility.SaveAvatar( Container, true, 0 );
	}

	public void RevertChanges()
	{
		Container.Deserialize( lastSaved );
		ApplyChangesToModel();
	}
}


public struct ColorSwatch
{
	public float Value;
	public Color Color;
}
