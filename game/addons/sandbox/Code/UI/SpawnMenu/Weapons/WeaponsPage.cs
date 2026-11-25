[Title( "#spawnmenu.tab.weapons" ), Order( 2010 ), Icon( "target" )]
public class WeaponsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListLocal { Category = "Weapon" } );
		AddOption( "sports_martial_arts", "#spawnmenu.weapon.melee", () => new EntityListLocal { Category = "Weapon/Melee" } );
		AddOption( "handyman", "#spawnmenu.weapon.utility", () => new EntityListLocal { Category = "Weapon/Utility" } );
		AddOption( "target", "#spawnmenu.weapon.weapons", () => new EntityListLocal { Category = "Weapon", IncludeSubcategories = false } );

		AddMountedSections( MountedPrefabs.WeaponTag, entity => SpawnMenuCategory.Matches( entity.Category, "Weapon" ) );
	}
}
