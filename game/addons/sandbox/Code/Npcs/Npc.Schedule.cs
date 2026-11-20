namespace Sandbox.Npcs;

public partial class Npc : Component
{
	/// <summary>
	/// The current running schedule for this NPC.
	/// </summary>
	public ScheduleBase ActiveSchedule { get; private set; }

	/// <summary>
	/// What the NPC is aware of this tick, refreshed before selection.
	/// Schedules declare which of these interrupt them via <see cref="ScheduleBase.InterruptedBy"/>.
	/// </summary>
	public NpcAwareness Awareness { get; private set; }

	private NpcAwareness _prevAwareness;

	readonly Dictionary<Type, ScheduleBase> _schedules = [];

	/// <summary>
	/// Get a schedule -- if it doesn't exist, one will be created. The instance is reused
	/// across selections, so set its inputs each time you return it; its runtime state is
	/// reset when it starts.
	/// </summary>
	protected T GetSchedule<T>() where T : ScheduleBase, new()
	{
		var type = typeof( T );
		if ( !_schedules.TryGetValue( type, out var schedule ) )
		{
			schedule = new T();
			_schedules[type] = schedule;
		}

		return (T)schedule;
	}

	/// <summary>
	/// Pick the schedule the NPC most wants to run right now -- return the highest-priority
	/// applicable one. Only called when (re)selecting, so a little work is fine, but keep it
	/// free of side effects and avoid randomness that shouldn't re-roll each selection.
	/// </summary>
	public virtual ScheduleBase GetSchedule()
	{
		return null;
	}

	/// <summary>
	/// Build what the NPC is aware of this tick. Override to add NPC-specific
	/// awareness -- call base first.
	/// </summary>
	protected virtual NpcAwareness GatherAwareness()
	{
		var a = NpcAwareness.None;

		if ( Senses.IsValid() )
		{
			if ( Senses.VisibleTargets.Count > 0 )
				a |= NpcAwareness.SeesHostile;

			if ( Senses.VisibleThreats.Count > 0 )
				a |= NpcAwareness.SeesThreat;

			if ( Senses.Disturbance.HasValue )
				a |= NpcAwareness.HeardDisturbance;

			if ( Senses.GetPushingPlayer().IsValid() )
				a |= NpcAwareness.PlayerPushing;

			if ( Senses.GetHeardHostile().IsValid() )
				a |= NpcAwareness.HostileIntel;
		}

		return a;
	}

	/// <summary>
	/// Runs once per tick: refresh conditions, then preempt the active schedule with
	/// something more important, run it, or select a new one when it finishes.
	/// </summary>
	void TickSchedule()
	{
		if ( !NpcConVars.Enabled )
			return;

		Awareness = GatherAwareness();

		// Edge-triggered: only awareness that newly appeared this tick can preempt.
		var rising = Awareness & ~_prevAwareness;
		_prevAwareness = Awareness;

		if ( ActiveSchedule is null )
		{
			SelectNewSchedule();
			return;
		}

		// A stimulus the active schedule reacts to just appeared -- preempt only if
		// something genuinely more important wants to run. This is what lets an idle
		// schedule yield to combat without polling for it itself.
		if ( (ActiveSchedule.InterruptedBy & rising) != 0 )
		{
			var desired = GetSchedule();
			if ( desired is not null
				&& !ReferenceEquals( desired, ActiveSchedule )
				&& desired.Priority > ActiveSchedule.Priority )
			{
				EndCurrentSchedule();
				StartSchedule( desired );
				return;
			}
		}

		// Run the active schedule. It ends itself by completing, failing, or cancelling.
		var status = ActiveSchedule.InternalUpdate();
		if ( status != TaskStatus.Running )
		{
			SelectNewSchedule();
		}
	}

	/// <summary>
	/// End the active schedule and start whatever the NPC wants next.
	/// </summary>
	private void SelectNewSchedule()
	{
		EndCurrentSchedule();

		var next = GetSchedule();
		if ( next is null )
			return;

		StartSchedule( next );
	}

	private void StartSchedule( ScheduleBase schedule )
	{
		ActiveSchedule = schedule;
		ActiveSchedule.InternalInit( this );
	}

	protected override void OnDisabled()
	{
		EndCurrentSchedule();
	}

	/// <summary>
	/// End the current schedule cleanly. Can be called by subclasses to interrupt
	/// the active schedule (e.g. when damaged).
	/// </summary>
	protected void EndCurrentSchedule()
	{
		ActiveSchedule?.InternalEnd();
		ActiveSchedule = null;
	}
}
