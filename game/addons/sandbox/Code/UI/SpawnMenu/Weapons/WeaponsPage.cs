[Title( "#spawnmenu.tab.weapons" ), Order( 2010 ), Icon( "🔫" )]
public class WeaponsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "📦", "#spawnmenu.entity.all", () => new EntityListCloud { LocalCategory = "Weapon", Query = "+weapon" } );
		AddOption( "🗡️", "#spawnmenu.weapon.melee", () => new EntityListCloud { LocalCategory = "Weapon/Melee", Query = "+weapon +melee" } );
		AddOption( "🛠️", "#spawnmenu.weapon.utility", () => new EntityListCloud { LocalCategory = "Weapon/Utility", Query = "+weapon +utility" } );
		AddOption( "🔫", "#spawnmenu.weapon.weapons", () => new EntityListCloud { LocalCategory = "Weapon", IncludeLocalSubcategories = false, Query = "+weapon +ranged" } );
	}
}
