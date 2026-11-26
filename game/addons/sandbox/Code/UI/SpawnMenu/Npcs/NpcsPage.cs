using Sandbox.UI;

[Title( "#spawnmenu.tab.npcs" ), Order( 2005 ), Icon( "person" )]
public class NpcsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "apps", "#spawnmenu.entity.all", () => new EntityListLocal { Category = "Npc" } );
		AddOption( "person", "#spawnmenu.npc.humanoid", () => new EntityListLocal { Category = "Npc/Humanoid" } );
		AddOption( "smart_toy", "#spawnmenu.npc.other", () => new EntityListLocal { Category = "Npc/Other" } );

		AddMountedSections( MountedPrefabs.NpcTag, entity => SpawnMenuCategory.Matches( entity.Category, "Npc" ) );
	}

	protected override void OnMenuHeader( Panel header )
	{
		header.AddChild<NpcWeaponPicker>();
	}
}
