namespace Sandbox.Npcs;

/// <summary>
/// What an NPC perceives this tick. Schedules declare which of these interrupt them via
/// <see cref="ScheduleBase.InterruptedBy"/>, so an idle schedule yields the moment a threat
/// or disturbance appears without having to poll for it itself.
/// </summary>
[Flags]
public enum NpcAwareness
{
	None = 0,

	/// <summary>A hostile target is visible.</summary>
	SeesHostile = 1 << 0,

	/// <summary>A visible entity the NPC is afraid of -- something it wants to flee.</summary>
	SeesThreat = 1 << 1,

	/// <summary>A notable disturbance was sensed nearby -- a gunshot, a death, a scream.</summary>
	HeardDisturbance = 1 << 2,

	/// <summary>A player is walking into us -- they want to get past and we're in the way.</summary>
	PlayerPushing = 1 << 3,

	/// <summary>
	/// Learned where a hostile is without seeing it -- heard it moving or shooting, or an ally called it out.
	/// </summary>
	HostileIntel = 1 << 4,
}
