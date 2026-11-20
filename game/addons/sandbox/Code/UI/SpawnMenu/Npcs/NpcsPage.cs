using Sandbox.UI;

[Title( "#spawnmenu.tab.npcs" ), Order( 2005 ), Icon( "🤖" )]
public class NpcsPage : BaseSpawnMenu
{
	protected override void Rebuild()
	{
		AddOption( "📦", "#spawnmenu.entity.all", () => new EntityListCloud { LocalCategory = "Npc", Query = "+npc" } );
		AddOption( "🧍", "#spawnmenu.npc.humanoid", () => new EntityListCloud { LocalCategory = "Npc/Humanoid", Query = "+npc +humanoid" } );
		AddOption( "🤖", "#spawnmenu.npc.other", () => new EntityListCloud { LocalCategory = "Npc/Other", Query = "+npc -humanoid" } );

		AddMountedSections( MountedPrefabs.NpcTag, entity => SpawnMenuCategory.Matches( entity.Category, "Npc" ) );
	}

	protected override void OnMenuHeader( Panel header )
	{
		header.AddChild<NpcWeaponPicker>();
	}
}
