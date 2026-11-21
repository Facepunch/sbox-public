using Sandbox.Npcs;

/// <summary>
/// The weapons players can pick for the NPCs they spawn, see <see cref="Player.NpcWeapon"/>.
/// </summary>
public static class NpcWeapons
{
	/// <summary>
	/// A weapon players can pick, by its title and prefab path.
	/// </summary>
	public record Pickable( string Title, string PrefabPath );

	/// <summary>
	/// The weapons players can pick for the NPCs they spawn: scripted entities and the weapons mounted games build.
	/// Mounted weapons are built to find out whether NPCs can use them.
	/// </summary>
	public static IEnumerable<Pickable> LoadPickableWeapons() => ResourceLibrary.GetAll<ScriptedEntity>()
		.Where( IsPickableWeapon )
		.Select( entity => new Pickable( entity.Title, entity.Prefab.ResourcePath ) )
		.Concat( MountedPrefabs.WithTag( MountedPrefabs.WeaponTag )
			.Where( x => IsPickableMountedWeapon( x.Path ) )
			.Select( x => new Pickable( MountedPrefabs.Title( x.Path ), x.Path ) ) );

	/// <summary>
	/// Installed weapons NPCs can fight with: melee and utility weapons have no NPC use set up
	/// </summary>
	static bool IsPickableWeapon( ScriptedEntity entity )
	{
		if ( !entity.IsMountAvailable || entity.IsRemote || entity.Prefab is null || (entity.Developer && !ServerSettings.ShowDeveloperEntities) )
			return false;

		if ( !SpawnMenuCategory.Matches( entity.Category, "Weapon" ) || SpawnMenuCategory.Matches( entity.Category, "Weapon/Melee" ) || SpawnMenuCategory.Matches( entity.Category, "Weapon/Utility" ) )
			return false;

		return SceneUtility.GetPrefabScene( entity.Prefab )?.GetComponent<BaseCombatWeapon>( true ) is { UsableByNpcs: true };
	}

	static bool IsPickableMountedWeapon( string path ) =>
		MountedPrefabs.HasTag( path, MountedPrefabs.WeaponTag ) && MountedPrefabs.Load( path ) is { } prefab
		&& SceneUtility.GetPrefabScene( prefab )?.GetComponent<BaseCombatWeapon>( true ) is { UsableByNpcs: true };

	/// <summary>
	/// Arm with the weapon a player picked instead of a random one from <see cref="BaseCombatNpc.WeaponChoices"/>, NPCs with
	/// a fixed weapon keep it. See <see cref="Player.NpcWeapon"/>. Host only, before the NPC is network spawned.
	/// </summary>
	public static void ArmWithPicked( this BaseCombatNpc npc, Player player )
	{
		var path = player?.NpcWeapon;
		if ( string.IsNullOrEmpty( path ) || npc.WeaponChoices is not { Count: > 0 } )
			return;

		var pickable = MountedPrefabs.IsListed( path )
			? IsPickableMountedWeapon( path )
			: ResourceLibrary.GetAll<ScriptedEntity>().Any( entity => entity.Prefab?.ResourcePath == path && IsPickableWeapon( entity ) );

		if ( pickable )
			npc.ArmWith( GameObject.GetPrefab( path ) );
	}
}
