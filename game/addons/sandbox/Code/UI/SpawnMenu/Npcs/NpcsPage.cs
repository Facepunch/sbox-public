using Sandbox.UI;

[Title( "#spawnmenu.tab.npcs" ), Order( 2005 ), Icon( "person_outline" )]
public class NpcsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListCloud { LocalCategory = "Npc", Query = "+npc" } );
		AddOption( "person_outline", "#spawnmenu.npc.humanoid", () => new EntityListCloud { LocalCategory = "Npc/Humanoid", Query = "+npc +humanoid" } );
		AddOption( "smart_toy", "#spawnmenu.npc.other", () => new EntityListCloud { LocalCategory = "Npc/Other", Query = "+npc -humanoid" } );

		AddMountedSections( MountedPrefabs.NpcTag, entity => SpawnMenuCategory.Matches( entity.Category, "Npc" ) );
	}

	protected override void OnMenuHeader( Panel header )
	{
		header.AddChild<NpcWeaponPicker>();
	}
}
