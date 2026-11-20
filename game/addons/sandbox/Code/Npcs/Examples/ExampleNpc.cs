using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Examples;

/// <summary>
/// A minimal NPC to copy when making your own. This "curious villager" wanders around, and
/// goes to investigate any gunshot or disturbance it hears nearby.
///
/// Everything an NPC needs is here:
///   1. A <see cref="Npc.Faction"/> -- who it is, so others know how to treat it.
///   2. Relationships -- who it fights / fears / likes (see <see cref="SetupRelationships"/>).
///   3. A <see cref="GetSchedule"/> -- what it wants to do right now.
///
/// To put one in the world, make a prefab with this component plus a NavMeshAgent, a
/// SkinnedModelRenderer (wire it to the Renderer field), and a Collider. The four AI layers
/// (Senses, Navigation, Animation, Speech) are added for you automatically.
/// </summary>
public sealed class ExampleNpc : Npc
{
	// This NPC is a peaceful citizen. It declares no hostilities, so it fights nobody;
	// enemies/monsters, which are hostile to citizens, will still come after it.
	public override string Faction => Factions.Citizen;

	protected override void SetupRelationships()
	{
		// It's wary of danger -- it flees anything hostile-flavoured on sight.
		Fears( Factions.Enemy, Factions.Monster );
	}

	// Called whenever the NPC needs to decide what to do. Return the schedule it should run;
	// list the most important options first. A higher-priority schedule automatically
	// interrupts a lower one when something changes (e.g. hearing a gunshot mid-wander).
	public override ScheduleBase GetSchedule()
	{
		// Heard a gunshot, death, or other disturbance? Go take a look.
		if ( Senses.Disturbance is { } disturbance )
		{
			var investigate = GetSchedule<InvestigateSchedule>();
			investigate.Target = disturbance.Position;
			return investigate;
		}

		// Nothing going on -- just wander around.
		return GetSchedule<WanderSchedule>();
	}
}
