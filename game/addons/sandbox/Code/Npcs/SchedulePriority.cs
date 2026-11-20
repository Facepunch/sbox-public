namespace Sandbox.Npcs;

/// <summary>
/// Common schedule priority tiers. A running schedule is only preempted by one with a
/// strictly higher priority, so these tiers decide who interrupts whom. Authors are free
/// to use their own numbers — these are just sensible defaults.
/// </summary>
public static class SchedulePriority
{
	/// <summary>Doing nothing in particular. Yields to everything.</summary>
	public const int Idle = 0;

	/// <summary>Ambient behaviour — wandering, patrolling, following, inspecting.</summary>
	public const int Ambient = 10;

	/// <summary>Reacting to something — searching a last-known position, investigating.</summary>
	public const int Alert = 20;

	/// <summary>Actively fighting a target.</summary>
	public const int Combat = 30;

	/// <summary>Self-preservation — fleeing, panicking. Overrides combat.</summary>
	public const int Survival = 40;
}
