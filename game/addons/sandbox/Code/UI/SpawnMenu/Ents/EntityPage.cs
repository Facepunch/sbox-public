
/// <summary>
/// Lists local Sandbox entities by category and installed game.
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
		"showcase" => "star",
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
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListLocal { Where = e => !IsWeaponOrNpc( SpawnMenuCategory.Root( e.Category ) ) } );

		var categories = ResourceLibrary.GetAll<ScriptedEntity>()
			.Where( e => !e.IsRemote && e.IsMountAvailable && (!e.Developer || ServerSettings.ShowDeveloperEntities) )
			.Select( e => SpawnMenuCategory.Root( e.Category ) )
			.Where( category => !IsWeaponOrNpc( category ) )
			.Distinct()
			.OrderBy( c => string.Equals( c, "Other", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( c => LocalizedText.Resolve( CategoryTitle( c ) ), StringComparer.CurrentCultureIgnoreCase );

		foreach ( var category in categories )
		{
			AddOption( CategoryIcon( category ), CategoryTitle( category ), () => new EntityListLocal { Category = category } );
		}

		AddMountedSections( MountedPrefabs.ItemTag, entity => !IsWeaponOrNpc( SpawnMenuCategory.Root( entity.Category ) ) );
	}

	static bool IsWeaponOrNpc( string category ) => string.Equals( category, "Weapon", StringComparison.OrdinalIgnoreCase ) || string.Equals( category, "Npc", StringComparison.OrdinalIgnoreCase );
}
