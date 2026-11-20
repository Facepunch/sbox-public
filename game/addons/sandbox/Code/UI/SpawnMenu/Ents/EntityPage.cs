
/// <summary>
/// This component has a kill icon that can be used in the killfeed, or somewhere else.
/// </summary>
[Title( "#spawnmenu.tab.entity" ), Order( 2000 ), Icon( "category" )]
public class EntityPage : BaseSpawnMenu
{
	static string CategoryIcon( string category ) => category?.ToLowerInvariant() switch
	{
		"chair" => "chair",
		"pickup" => "inventory_2",
		"vehicle" => "directions_car",
		"world" => "public",
		"animal" => "pets",
		"audio" => "volume_up",
		"effect" => "auto_awesome",
		"showcase" => "star_outline",
		"test toys" or "toyfun" => "toys",
		_ => "category"
	};

	/// <summary>
	/// Translate known category labels without changing their resource or search identifiers.
	/// </summary>
	static string CategoryTitle( string category ) => category?.ToLowerInvariant() switch
	{
		"chair" => "#ui.entity.chair",
		"pickup" => "#ui.entity.pickup",
		"world" => "#ui.entity.world",
		"vehicle" => "#spawnmenu.entity.vehicle",
		"other" => "#spawnmenu.entity.other",
		_ => category
	};

	protected override void Rebuild()
	{
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListCloud { IncludeLocalEntities = true, ExcludedLocalCategoryRoots = new[] { "Weapon", "Npc" }, Query = "-cat:weapon -cat:npc" } );

		var categories = ResourceLibrary.GetAll<ScriptedEntity>()
			.Where( e => !e.IsRemote && e.IsMountAvailable && (!e.Developer || ServerSettings.ShowDeveloperEntities) )
			.Select( e => SpawnMenuCategory.Root( e.Category ) )
			.Where( category => !IsWeaponOrNpc( category ) )
			.Distinct()
			.OrderBy( c => string.Equals( c, "Other", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( c => LocalizedText.Resolve( CategoryTitle( c ) ), StringComparer.CurrentCultureIgnoreCase );

		var addedCategories = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var category in categories )
		{
			addedCategories.Add( category );
			AddOption( CategoryIcon( category ), CategoryTitle( category ), () => new EntityListCloud { LocalCategory = category, Query = $"cat:{category.ToLowerInvariant()}" } );
		}

		void AddCloudCategory( string name, string category )
		{
			if ( !addedCategories.Add( category ) ) return;
			AddOption( CategoryIcon( category ), name, () => new EntityListCloud { Query = $"cat:{category}" } );
		}

		AddCloudCategory( "#spawnmenu.entity.animals", "animal" );
		AddCloudCategory( "#spawnmenu.entity.audio", "audio" );
		AddCloudCategory( "#spawnmenu.entity.effect", "effect" );
		AddCloudCategory( "#spawnmenu.entity.other", "other" );
		AddCloudCategory( "#spawnmenu.entity.showcase", "showcase" );
		AddCloudCategory( "#spawnmenu.entity.toys_and_fun", "toyfun" );
		AddCloudCategory( "#spawnmenu.entity.vehicle", "vehicle" );

		AddMountedSections( MountedPrefabs.ItemTag, entity => !IsWeaponOrNpc( SpawnMenuCategory.Root( entity.Category ) ) );
	}

	static bool IsWeaponOrNpc( string category ) => string.Equals( category, "Weapon", StringComparison.OrdinalIgnoreCase ) || string.Equals( category, "Npc", StringComparison.OrdinalIgnoreCase );
}
