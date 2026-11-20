using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs;

public partial class BaseNpc : Component
{
	/// <summary>
	/// The current running schedule for this NPC.
	/// </summary>
	public BaseSchedule ActiveSchedule { get; private set; }

	/// <summary>
	/// What the NPC is aware of this tick, refreshed before selection.
	/// What newly appears in it interrupts a schedule that is <see cref="BaseSchedule.Interruptible"/>.
	/// </summary>
	internal NpcAwareness Awareness { get; private set; }

	private NpcAwareness _prevAwareness;

	readonly Dictionary<Type, BaseSchedule> _schedules = [];

	/// <summary>
	/// Get a schedule -- if it doesn't exist, one will be created. The instance is reused
	/// across selections, so set its inputs each time you return it; its runtime state is
	/// reset when it starts.
	/// </summary>
	protected T GetSchedule<T>() where T : BaseSchedule, new()
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
	protected virtual BaseSchedule SelectSchedule()
	{
		return null;
	}

	/// <summary>
	/// Build what the NPC is aware of this tick. Override to add NPC-specific
	/// awareness -- call base first.
	/// </summary>
	internal virtual NpcAwareness GatherAwareness()
	{
		var a = NpcAwareness.None;

		if ( Senses.IsValid() )
		{
			if ( Senses.VisibleHostiles.Count > 0 )
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
		if ( ActiveSchedule.Interruptible && rising != NpcAwareness.None )
		{
			var desired = SelectSchedule();
			if ( desired is not null
				&& !ReferenceEquals( desired, ActiveSchedule )
				&& desired.Priority > ActiveSchedule.Priority )
			{
				EndCurrentSchedule();

				if ( CanThink )
					StartSchedule( desired );

				return;
			}
		}

		var schedule = ActiveSchedule;
		TaskResult status;

		try
		{
			status = schedule.InternalUpdate();
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Exception in schedule {schedule.GetType().Name} on {this}" );
			status = TaskResult.Failed;
		}

		// Ended or replaced from inside its own update: whoever did that has taken over.
		if ( ActiveSchedule != schedule )
			return;

		if ( status != TaskResult.Running )
		{
			EndSchedule( status );
			SelectNewSchedule();
		}
	}

	/// <summary>
	/// End the active schedule and start whatever the NPC wants next.
	/// </summary>
	private void SelectNewSchedule()
	{
		EndCurrentSchedule();

		// Ending it can disable or kill us
		if ( !CanThink )
			return;

		var next = SelectSchedule();
		if ( next is null )
			return;

		StartSchedule( next );
	}

	private void StartSchedule( BaseSchedule schedule )
	{
		EndCurrentSchedule();

		ActiveSchedule = schedule;

		try
		{
			schedule.InternalInit( this );
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Exception starting schedule {schedule.GetType().Name} on {this}" );

			if ( ActiveSchedule == schedule )
				EndSchedule( TaskResult.Failed );
		}
	}

	protected override void OnDisabled()
	{
		if ( Senses.IsValid() )
			Senses.ClearScan();

		EndCurrentSchedule();
	}

	/// <summary>
	/// End the current schedule: its open task ends as <see cref="TaskResult.Failed"/>, then the schedule's
	/// OnEnd runs. Can be called by subclasses to interrupt the active schedule (e.g. when damaged), also
	/// from inside a schedule or task callback. Does nothing when no schedule is running.
	/// </summary>
	protected void EndCurrentSchedule() => EndSchedule( TaskResult.Failed );

	// The schedule is detached before its callbacks run, so one that ends or starts a schedule finds none active.
	private void EndSchedule( TaskResult reason )
	{
		var schedule = ActiveSchedule;
		if ( schedule is null )
			return;

		ActiveSchedule = null;

		try
		{
			schedule.InternalEnd( reason );
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Exception ending schedule {schedule.GetType().Name} on {this}" );
		}
	}
}
