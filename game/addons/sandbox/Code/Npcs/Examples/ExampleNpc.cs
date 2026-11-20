using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Examples;

/// <summary>
/// A minimal NPC to copy when making your own. This "curious villager" wanders around, and
/// goes to investigate any gunshot or disturbance it hears nearby.
///
/// Everything an NPC needs is here:
///   1. A <see cref="BaseNpc.Faction"/> -- who it is, so others know how to treat it.
///   2. Relationships -- who it fights / fears / likes (see <see cref="SetupRelationships"/>).
///   3. A <see cref="SelectSchedule"/> -- what it wants to do right now.
///
/// To put one in the world, make a prefab with this component plus a NavMeshAgent, a
/// SkinnedModelRenderer (wire it to the Renderer field), and a Collider. The Animation layer
/// is added for you; add the Senses and Navigation layers it uses (and Speech if it talks)
/// yourself, plus a <see cref="CitizenNpcAnimator"/> if the model uses the citizen animgraph.
/// </summary>
public sealed class ExampleNpc : BaseNpc
{
	// This NPC is a peaceful citizen. It declares no hostilities, so it fights nobody;
	// enemies/monsters, which are hostile to citizens, will still come after it.
	public override string Faction => Factions.Citizen;

	protected override void SetupRelationships()
	{
		// It's wary of danger -- enemies and monsters show up in Senses.VisibleThreats.
		SetRelationship( Factions.Enemy, Disposition.Fearful );
		SetRelationship( Factions.Monster, Disposition.Fearful );
	}

	// Called whenever the NPC needs to decide what to do. Return the schedule it should run;
	// list the most important options first. A higher-priority schedule automatically
	// interrupts a lower one when something changes (e.g. hearing a gunshot mid-wander).
	protected override BaseSchedule SelectSchedule()
	{
		if ( Senses?.Disturbance is { } disturbance )
		{
			var investigate = GetSchedule<InvestigateSchedule>();
			investigate.TargetPosition = disturbance.Position;
			return investigate;
		}

		return GetSchedule<WanderSchedule>();
	}
}
