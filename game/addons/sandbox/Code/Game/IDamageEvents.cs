/// <summary>
/// Who got hurt and by whom, shared by damage and death events for players and NPCs alike.
/// Use <see cref="Target.IsPlayer"/>, <see cref="Target.IsNpc"/> and <see cref="Target.Get{T}"/> to tell them apart.
/// </summary>
public abstract class CombatEvent
{
	/// <summary>
	/// The player or NPC being hurt.
	/// </summary>
	public Target Victim { get; init; }

	public DamageInfo DamageInfo { get; init; }

	/// <summary>
	/// Who's blamed for the damage - the player behind it, like whoever threw the prop or drove the car,
	/// otherwise the attacker. Not valid for the world.
	/// </summary>
	public Target Attacker { get; init; }
}

/// <summary>
/// Data passed to <see cref="Global.IDamageEvents"/>. In <see cref="Global.IDamageEvents.OnDamaging"/>,
/// change <see cref="Damage"/> or set <see cref="Cancelled"/> to block it.
/// </summary>
public class DamageEvent : CombatEvent
{
	/// <summary>
	/// The damage to deal, before the victim's own modifiers like headshots and armour.
	/// </summary>
	public float Damage { get; set; }

	/// <summary>
	/// Set to true to block the damage. Can start out true, when PvP is off and another player is to blame - set it
	/// back to false to allow the hit.
	/// </summary>
	public bool Cancelled { get; set; }

	/// <summary>
	/// The health the victim actually lost, after headshots and armour. Only set in <see cref="Global.IDamageEvents.OnDamaged"/>.
	/// </summary>
	public float HealthLost { get; set; }
}

/// <summary>
/// Allows listening to and changing damage dealt to players and NPCs. Implement this on a <see cref="Component"/>
/// to scale damage, add friendly fire rules, or react to hits.
/// </summary>
public static partial class Global
{
	public interface IDamageEvents : ISceneEvent<IDamageEvents>
	{
		/// <summary>
		/// Called on the host before a player or NPC loses health.
		/// </summary>
		void OnDamaging( DamageEvent e ) { }

		/// <summary>
		/// Called on the host after a player or NPC has lost health, before they die if it killed them.
		/// </summary>
		void OnDamaged( DamageEvent e ) { }
	}
}
