[Title( "#spawnmenu.tab.weapons" ), Order( 2010 ), Icon( "my_location" )]
public class WeaponsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListCloud { LocalCategory = "Weapon", Query = "+weapon" } );
		AddOption( "sports_martial_arts", "#spawnmenu.weapon.melee", () => new EntityListCloud { LocalCategory = "Weapon/Melee", Query = "+weapon +melee" } );
		AddOption( "handyman", "#spawnmenu.weapon.utility", () => new EntityListCloud { LocalCategory = "Weapon/Utility", Query = "+weapon +utility" } );
		AddOption( "gps_fixed", "#spawnmenu.weapon.weapons", () => new EntityListCloud { LocalCategory = "Weapon", IncludeLocalSubcategories = false, Query = "+weapon +ranged" } );

		AddMountedSections( MountedPrefabs.WeaponTag, entity => SpawnMenuCategory.Matches( entity.Category, "Weapon" ) );
	}
}
