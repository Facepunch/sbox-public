/// <summary>
/// Data passed to <see cref="Global.IDeathEvents"/>. In <see cref="Global.IDeathEvents.OnDying"/>, set
/// <see cref="Cancelled"/> to true to keep the victim alive on 1 health (or more, if you heal them),
/// or turn off <see cref="CreateRagdoll"/>.
/// </summary>
public class DeathEvent : CombatEvent
{
	/// <summary>
	/// Whether to leave a ragdoll behind.
	/// </summary>
	public bool CreateRagdoll { get; set; } = true;

	public bool Cancelled { get; set; }
}

/// <summary>
/// Allows listening to and stopping the deaths of players and NPCs. Implement this on a <see cref="Component"/>.
/// </summary>
public static partial class Global
{
	public interface IDeathEvents : ISceneEvent<IDeathEvents>
	{
		/// <summary>
		/// Called on the host when damage has taken a player or NPC below 1 health, before they die.
		/// </summary>
		void OnDying( DeathEvent e ) { }

		/// <summary>
		/// Called on the host after a player or NPC has died, the kill has been credited and their ragdoll made.
		/// </summary>
		void OnDied( DeathEvent e ) { }
	}
}
