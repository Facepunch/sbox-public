public struct PlayerDiedParams
{
	public GameObject Attacker { get; set; }
}

public struct PlayerDamageParams
{
	public float Damage { get; set; }
	public GameObject Attacker { get; set; }
	public GameObject Weapon { get; set; }
	public TagSet Tags { get; set; }
	public Vector3 Position { get; set; }
	public Vector3 Origin { get; set; }
}

/// <summary>
/// Data passed to pickup events. Set <see cref="Cancelled"/> to true to prevent the pickup.
/// </summary>
public class PlayerPickupEvent
{
	public Player Player { get; init; }
	public BaseSandboxWeapon Weapon { get; init; }
	public int Slot { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Data passed to drop events. Set <see cref="Cancelled"/> to true to prevent the drop.
/// </summary>
public class PlayerDropEvent
{
	public Player Player { get; init; }
	public BaseSandboxWeapon Weapon { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Fired from player's client when they try to pick up an object, and again on the instance that simulates it before it's moved for them. Set <see cref="Cancelled"/> to true to refuse, or change <see cref="MaxMass"/> to let them lift more or less. See <see cref="PlayerCarry"/>.
/// </summary>
public class PlayerCarryEvent
{
	public Player Player { get; init; }
	public GameObject Target { get; init; }
	public Rigidbody Body { get; init; }
	public float Mass { get; init; }
	public float MaxMass { get; set; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Data passed to switch weapon events. Set <see cref="Cancelled"/> to true to prevent the switch.
/// </summary>
public class PlayerSwitchWeaponEvent
{
	public Player Player { get; init; }
	public BaseSandboxWeapon From { get; init; }
	public BaseSandboxWeapon To { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Data passed to remove weapon events. Set <see cref="Cancelled"/> to true to prevent the removal.
/// </summary>
public class PlayerRemoveWeaponEvent
{
	public Player Player { get; init; }
	public BaseSandboxWeapon Weapon { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Data passed to slot move events. Set <see cref="Cancelled"/> to true to prevent the move.
/// </summary>
public class PlayerMoveSlotEvent
{
	public Player Player { get; init; }
	public int FromSlot { get; init; }
	public int ToSlot { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Fired on the host when a player sits in a seat, before their weapon is put away. 
/// Set <see cref="Cancelled"/> to true to let them keep weapons out in it.
/// </summary>
public class PlayerSeatHolsterEvent
{
	public Player Player { get; init; }
	public GameObject Seat { get; init; }
	public BaseSandboxWeapon Weapon { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Fired on the player's own machine when they press the flashlight key, before the flashlight toggles. Set
/// <see cref="Cancelled"/> to true to keep the key for yourself - a vehicle uses it for its headlights.
/// </summary>
public class PlayerFlashlightEvent
{
	public Player Player { get; init; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Pre-damage event. Fired before damage is applied. Listeners can modify <see cref="Damage"/>
/// or set <see cref="Cancelled"/> to block damage entirely.
/// </summary>
public class PlayerDamageEvent
{
	public Player Player { get; init; }
	public DamageInfo DamageInfo { get; init; }

	/// <summary>
	/// The player behind the damage, can be the victim. Null for the world and NPCs.
	/// </summary>
	public Player BlamedPlayer { get; init; }

	public float Damage { get; set; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Pre-respawn event. Fired on the host before the player is spawned. Listeners can modify
/// <see cref="SpawnLocation"/> to control where the player appears, or set <see cref="Cancelled"/>
/// to stop the spawn entirely - for example an addon running its own round loop that wants dead
/// players to wait. A player cancelled after death stays in the observer state; spawn them later
/// with <see cref="GameManager.Respawn"/>, which skips this event. This also fires for the first
/// spawn on join, so a listener that cancels there is responsible for giving that connection
/// something to look at.
/// </summary>
public class PlayerRespawnEvent
{
	public PlayerData PlayerData { get; init; }
	public Transform SpawnLocation { get; set; }
	public bool Cancelled { get; set; }
}

/// <summary>
/// Fired on the host before a player is given their starter weapons, on spawning without a saved hotbar to restore.
/// Edit <see cref="Items"/> to change what they get, or set <see cref="Cancelled"/> to true to give nothing.
/// </summary>
public class PlayerLoadoutEvent
{
	public Player Player { get; init; }

	/// <summary>
	/// The weapon prefabs to give, in order. Starts as the player prefab's starting items.
	/// </summary>
	public List<GameObject> Items { get; set; }

	public bool Cancelled { get; set; }
}

/// <summary>
/// Fired when a player kills another player or NPC.
/// </summary>
public class PlayerKillEvent
{
	public Player Player { get; init; }
	public GameObject Victim { get; init; }
	public DamageInfo DamageInfo { get; init; }
}

/// <summary>
/// Events fired only to the Player's own GameObject hierarchy, except <see cref="IPlayerEvents.OnMove"/> and the
/// camera events, which go to the whole scene on the machine controlling the local player so UI can listen too.
/// </summary>
public static partial class Local
{
	public interface IPlayerEvents : ISceneEvent<IPlayerEvents>
	{
		void OnSpawned() { }
		void OnDied( PlayerDiedParams args ) { }
		void OnDamage( PlayerDamageParams args ) { }
		void OnJump() { }
		void OnLand( float distance, Vector3 velocity ) { }
		void OnSuicide() { }
		void OnPickup( PlayerPickupEvent e ) { }
		void OnDrop( PlayerDropEvent e ) { }
		void OnSwitchWeapon( PlayerSwitchWeaponEvent e ) { }
		void OnRemoveWeapon( PlayerRemoveWeaponEvent e ) { }
		void OnMoveSlot( PlayerMoveSlotEvent e ) { }
		void OnSeatHolster( PlayerSeatHolsterEvent e ) { }
		void OnFlashlight( PlayerFlashlightEvent e ) { }
		void OnCarry( PlayerCarryEvent e ) { }
		void OnDamaging( PlayerDamageEvent e ) { }
		void OnKill( PlayerKillEvent e ) { }
		/// <summary>
		/// Called on the local player's machine as they look around, sent to the whole scene.
		/// </summary>
		void OnCameraMove( ref Angles angles ) { }

		/// <summary>
		/// Called on the local player's machine while their camera is set up, sent to the whole scene.
		/// </summary>
		void OnCameraSetup( CameraComponent camera ) { }

		/// <summary>
		/// Called on the local player's machine after their camera is set up, sent to the whole scene.
		/// </summary>
		void OnCameraPostSetup( CameraComponent camera ) { }

		/// <summary>
		/// Called every tick on the local player's machine before movement input is applied, sent to the whole scene.
		/// Set <paramref name="allowMovement"/> to false to prevent the player from moving with WASD this tick
		/// (e.g. while spinning a physgun-held object).
		/// </summary>
		void OnMove( ref bool allowMovement ) { }
	}
}

/// <summary>
/// Events broadcasted to the entire scene for any player action.
/// </summary>
public static partial class Global
{
	public interface IPlayerEvents : ISceneEvent<IPlayerEvents>
	{
		void OnPlayerSpawned( Player player ) { }
		void OnPlayerDied( Player player, PlayerDiedParams args ) { }
		void OnPlayerDamage( Player player, PlayerDamageParams args ) { }
		void OnPlayerJumped( Player player ) { }
		void OnPlayerLanded( Player player, float distance, Vector3 velocity ) { }
		void OnPlayerSuicide( Player player ) { }
		void OnPlayerPickup( PlayerPickupEvent e ) { }
		void OnPlayerDrop( PlayerDropEvent e ) { }
		void OnPlayerSwitchWeapon( PlayerSwitchWeaponEvent e ) { }
		void OnPlayerRemoveWeapon( PlayerRemoveWeaponEvent e ) { }
		void OnPlayerMoveSlot( PlayerMoveSlotEvent e ) { }
		void OnPlayerSeatHolster( PlayerSeatHolsterEvent e ) { }
		void OnPlayerFlashlight( PlayerFlashlightEvent e ) { }
		void OnPlayerCarry( PlayerCarryEvent e ) { }
		[Obsolete( "Use Global.IDamageEvents, it covers players and NPCs" )]
		void OnPlayerDamaging( PlayerDamageEvent e ) { }
		void OnPlayerRespawning( PlayerRespawnEvent e ) { }
		void OnPlayerLoadout( PlayerLoadoutEvent e ) { }
		void OnPlayerKill( PlayerKillEvent e ) { }
	}
}
