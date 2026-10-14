using Sandbox;
using System.Threading;
using static Sandbox.ClothingContainer;

public sealed partial class AvatarEditManager : Component
{
	/// <summary>
	/// The Human body used to preview the edited avatar.
	/// </summary>
	[Header( "Bodies" )]
	[Property] public GameObject Human { get; set; }

	string lastSaved;
	string currentAppearance;
	CancellationTokenSource appearanceUpdate;

	/// <summary>
	/// The selected outfit and appearance values being edited.
	/// </summary>
	public ClothingContainer Container
	{
		get;
		set
		{
			value.PrefersHuman = true;
			field = value;
			InvalidateUnsavedChanges();
		}
	} = new ClothingContainer { PrefersHuman = true };
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

			if ( item.StoreHidden && !Sandbox.Services.Inventory.HasItem( item.Id ) )
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

	/// <summary>
	/// Everything there is to wear - less what's only for sale, for an account too new to be sold things
	/// (see <see cref="MenuHelpers.ShowMicrotransactions"/>). What it owns, it still has.
	/// </summary>
	public IEnumerable<Clothing> GetAllClothing()
	{
		return MenuHelpers.ShowMicrotransactions ? allClothing : allClothing.Where( IsPurchased );
	}

	protected override void OnUpdate()
	{
		var renderer = Human.GetComponent<SkinnedModelRenderer>();

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
		set
		{
			Container.DisplayName = value;
			InvalidateUnsavedChanges();
		}
	}

	public float Height
	{
		get => Container.Height;
		set
		{
			Container.Height = value;
			ApplyAppearanceChanges();
		}
	}

	public float Age
	{
		get => Container.Age;
		set
		{
			Container.Age = value;
			ApplyAppearanceChanges();
		}
	}

	public float Tint
	{
		get => Container.Tint;
		set
		{
			Container.Tint = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Previews a workshop item until another appearance request replaces it.
	/// </summary>
	public void PreviewPackage( Package package )
	{
		if ( package == null )
		{
			RevertHovered();
			return;
		}

		var token = BeginAppearanceUpdate();
		MenuUtility.RunTask( () => PreviewPackageAsync( package, token ) );
	}

	/// <summary>
	/// Loads and previews a workshop item unless the preview is superseded.
	/// </summary>
	public Task PreviewPackageAsync( Package package ) => PreviewPackageAsync( package, BeginAppearanceUpdate() );

	async Task PreviewPackageAsync( Package package, CancellationToken token )
	{
		try
		{
			token.ThrowIfCancellationRequested();
			var clothing = await Cloud.Load<Clothing>( package.FullIdent );
			token.ThrowIfCancellationRequested();

			OnClothingHover( clothing );
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			// A newer appearance request owns the preview now.
		}
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
		ApplyAppearanceChanges();
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

		Container.Toggle( clothing );
		ApplyChangesToModel();
	}

	/// <summary>
	/// Applies the hovered outfit, cancelling any previous appearance request.
	/// </summary>
	public void ApplyPreviewToModel() => ApplyToModels( PreviewContainer );

	/// <summary>
	/// Applies the selected outfit, cancelling any pending hover preview.
	/// </summary>
	public void ApplyChangesToModel()
	{
		Container.PrefersHuman = true;
		InvalidateUnsavedChanges();
		ApplyToModels( Container );
	}

	void ApplyToModels( ClothingContainer container )
	{
		var token = BeginAppearanceUpdate();

		// We have to run it this way so it'll be in the menu context
		MenuUtility.RunTask( () => ApplyAsync( container, Human, token ) );
	}

	CancellationToken BeginAppearanceUpdate()
	{
		CancelAppearanceUpdate();
		appearanceUpdate = new CancellationTokenSource();
		return appearanceUpdate.Token;
	}

	void CancelAppearanceUpdate()
	{
		appearanceUpdate?.Cancel();
		appearanceUpdate?.Dispose();
		appearanceUpdate = null;
	}

	protected override void OnDisabled() => CancelAppearanceUpdate();

	protected override void OnDestroy() => CancelAppearanceUpdate();

	async Task ApplyAsync( ClothingContainer container, GameObject target, CancellationToken token )
	{
		try
		{
			token.ThrowIfCancellationRequested();
			if ( !target.IsValid() )
				return;

			var targetRenderer = target.GetComponent<SkinnedModelRenderer>( true );
			if ( !targetRenderer.IsValid() )
				return;

			var dresser = Dresser.GetOrCreate( targetRenderer );
			dresser.UpdateAppearance( Container );
			await dresser.ApplyAsync( container, token );

			// A human skin swaps the body model, which rebuilds the anim graph with default
			// parameters - put it back on the avatar menu's looping idles, skipping the entry wave
			targetRenderer.Set( "b_avatar_skip_intro", true );
			targetRenderer.Set( "special_idle_states", "avatar_menu" );
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
		{
			// Unhovering, editing or closing the avatar editor supersedes this outfit.
		}
		finally
		{
			// Applying clothing can normalize the selected outfit or resolve downloaded items.
			if ( ReferenceEquals( container, Container ) )
				InvalidateUnsavedChanges();
		}
	}

	void ApplyAppearanceChanges()
	{
		InvalidateUnsavedChanges();
		UpdateBody( Human );

		void UpdateBody( GameObject target )
		{
			var renderer = target?.GetComponent<SkinnedModelRenderer>( true );
			if ( renderer.IsValid() )
				Dresser.GetOrCreate( renderer ).UpdateAppearance( Container );
		}
	}

	void RevertHovered()
	{
		ApplyChangesToModel();
	}

	/// <summary>
	/// Whether the edited values differ from the saved avatar. Rechecks only after an edit or clothing load.
	/// </summary>
	public bool HasUnsavedChanges => lastSaved != (currentAppearance ??= Container.Serialize());

	void InvalidateUnsavedChanges() => currentAppearance = null;

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
