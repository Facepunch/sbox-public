namespace Sandbox.Npcs;

/// <summary>
/// A schedule -- can be understood as a way to execute a sequence of tasks
/// </summary>
public abstract class ScheduleBase
{
	public Npc Npc { get; private set; }
	protected GameObject GameObject => Npc.GameObject;

	/// <summary>
	/// Selection priority. A running schedule is only preempted by one with a strictly
	/// higher priority. Use <see cref="SchedulePriority"/> for the common tiers.
	/// </summary>
	public virtual int Priority => SchedulePriority.Idle;

	/// <summary>
	/// Stimuli that should make this schedule reconsider mid-run. When one of these newly
	/// appears, the NPC re-selects and a higher-priority schedule can take over. Defaults
	/// to the "drop everything and re-think" stimuli so ambient schedules yield to threats
	/// for free; combat and flee schedules override this to stay focused.
	/// </summary>
	public virtual NpcAwareness InterruptedBy =>
		NpcAwareness.SeesHostile | NpcAwareness.SeesThreat | NpcAwareness.HeardDisturbance
		| NpcAwareness.PlayerPushing | NpcAwareness.HostileIntel;

	private List<TaskBase> _tasks = new();
	private int _currentTaskIndex = 0;

	/// <summary>
	/// Initialize the schedule with the Behavior context
	/// </summary>
	internal void InternalInit( Npc npc )
	{
		Npc = npc;
		_tasks.Clear();
		_currentTaskIndex = 0;

		// Build task sequence
		OnStart();

		// Start first task
		StartCurrentTask();
	}

	protected virtual void OnUpdate()
	{
		//
	}

	protected virtual bool ShouldCancel()
	{
		return false;
	}

	/// <summary>
	/// Called when task is ended because of ShouldCancel returning true
	/// </summary>
	protected virtual void OnCancelled()
	{

	}

	/// <summary>
	/// Called every frame while schedule is running
	/// </summary>
	internal TaskStatus InternalUpdate()
	{
		if ( _tasks.Count == 0 ) return TaskStatus.Failed;
		if ( _currentTaskIndex >= _tasks.Count ) return TaskStatus.Success;

		// Give schedule a chance to cancel itself, based on interuptions
		if ( ShouldCancel() )
		{
			OnCancelled();
			return TaskStatus.Interrupted;
		}

		var currentTask = _tasks[_currentTaskIndex];
		var status = currentTask.InternalUpdate();

		if ( status is TaskStatus.Success )
		{
			currentTask.InternalEnd();
			_currentTaskIndex++;
			StartCurrentTask();
			return TaskStatus.Running;
		}

		// Running keeps going; Failed/Interrupted end the schedule. Either way don't end the
		// task here -- InternalEnd() (via EndCurrentSchedule) closes it out once, so OnEnd/Reset
		// don't run twice.
		return status;
	}

	/// <summary>
	/// Called once when schedule ends
	/// </summary>
	internal void InternalEnd()
	{
		// End current task if running
		if ( _currentTaskIndex < _tasks.Count )
		{
			_tasks[_currentTaskIndex].InternalEnd();
		}

		_currentTaskIndex = 0;

		OnEnd();
	}

	/// <summary>
	/// Called when this schedule starts -- this is where you can add tasks to run
	/// </summary>
	protected virtual void OnStart() { }

	/// <summary>
	/// Called when this schedule ends -- this is where you can clean stuff up
	/// </summary>
	protected virtual void OnEnd() { }

	/// <summary>
	/// Add a task to the sequence
	/// </summary>
	protected void AddTask( TaskBase task )
	{
		_tasks.Add( task );
	}

	/// <summary>
	/// Start the current task in sequence
	/// </summary>
	private void StartCurrentTask()
	{
		if ( _currentTaskIndex < _tasks.Count )
		{
			var task = _tasks[_currentTaskIndex];
			task.Initialize( this );
		}
	}

	/// <summary>
	/// Information about this schedule for debugging purposes
	/// </summary>
	public string GetDebugString()
	{
		if ( _currentTaskIndex >= _tasks.Count )
			return $"{GetType().Name} [P{Priority}]/(none)";

		var task = _tasks[_currentTaskIndex];

		return $"{GetType().Name} [P{Priority}]/{task.GetType().Name}";
	}
}
