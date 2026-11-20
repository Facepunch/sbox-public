using Sandbox.Npcs.Tasks;

namespace Sandbox.Npcs.Schedules;

/// <summary>
/// A schedule -- can be understood as a way to execute a sequence of tasks
/// </summary>
[Expose]
public abstract class BaseSchedule
{
	/// <summary>
	/// The NPC running this schedule.
	/// </summary>
	public BaseNpc Npc { get; private set; }
	/// <summary>
	/// The GameObject of the NPC running this schedule.
	/// </summary>
	protected GameObject GameObject => Npc.GameObject;

	/// <summary>
	/// Selection priority. A running schedule is only preempted by one with a strictly
	/// higher priority. Use <see cref="SchedulePriorities"/> for the common tiers.
	/// </summary>
	public virtual int Priority => SchedulePriorities.Idle;

	/// <summary>
	/// Whether something the NPC newly notices mid-run, such as a hostile coming into view, a threat, a disturbance
	/// or a player pushing past, makes it re-select so a higher-priority schedule can take over. True by default,
	/// so ambient schedules yield to threats for free; combat and flee schedules turn it off to stay focused.
	/// </summary>
	public virtual bool Interruptible => true;

	private List<BaseTask> _tasks = new();
	private int _currentTaskIndex = 0;

	// Running, and which run: callbacks can end or restart the schedule, and the runner must notice.
	private bool _active;
	private int _run;

	// The task that was started and has not been ended yet.
	private BaseTask _openTask;

	/// <summary>
	/// Initialize the schedule with the Behavior context
	/// </summary>
	internal void InternalInit( BaseNpc npc )
	{
		Npc = npc;
		_tasks.Clear();
		_currentTaskIndex = 0;
		_openTask = null;
		_active = true;
		var run = ++_run;

		OnStart();

		if ( IsRun( run ) )
			StartCurrentTask();
	}

	// Still this run, on an NPC that is still switched on, alive and not on its way out
	private bool IsRun( int run ) => _active && _run == run && Npc.IsValid() && Npc.CanThink;

	/// <summary>
	/// Asked every update while the schedule runs: return true to end it early, after which <see cref="OnCancelled"/> is called.
	/// </summary>
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
	internal TaskResult InternalUpdate()
	{
		if ( !_active || _tasks.Count == 0 ) return TaskResult.Failed;
		if ( _currentTaskIndex >= _tasks.Count ) return TaskResult.Success;

		var run = _run;

		if ( ShouldCancel() )
		{
			if ( IsRun( run ) ) OnCancelled();
			return TaskResult.Failed;
		}

		// Every callback can end this run, or disable the NPC, which ends it too. Nothing carries on after that.
		if ( !IsRun( run ) ) return TaskResult.Failed;

		var currentTask = _tasks[_currentTaskIndex];
		var status = currentTask.InternalUpdate();

		if ( !IsRun( run ) ) return TaskResult.Failed;

		if ( status is TaskResult.Success )
		{
			_openTask = null;
			currentTask.InternalEnd( TaskResult.Success );
			if ( !IsRun( run ) ) return TaskResult.Failed;

			_currentTaskIndex++;
			StartCurrentTask();
			return IsRun( run ) ? TaskResult.Running : TaskResult.Failed;
		}

		// Running keeps going; Failed ends the schedule. Either way don't end the
		// task here -- InternalEnd() (via EndCurrentSchedule) closes it out once, so OnEnd/Reset
		// don't run twice.
		return status;
	}

	/// <summary>
	/// Ends the schedule once, however often it is called. A task still open is ended with <paramref name="reason"/>.
	/// The schedule is over before any callback runs, and stays over if one throws.
	/// </summary>
	internal void InternalEnd( TaskResult reason )
	{
		if ( !_active ) return;
		_active = false;

		var task = _openTask;
		_openTask = null;
		_currentTaskIndex = 0;

		try
		{
			task?.InternalEnd( reason );
		}
		finally
		{
			OnEnd();
		}
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
	protected void AddTask( BaseTask task )
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
			_openTask = task;
			task.Initialize( this );
		}
	}

	/// <summary>
	/// Information about this schedule for debugging purposes
	/// </summary>
	internal string GetDebugString()
	{
		if ( _currentTaskIndex >= _tasks.Count )
			return $"{GetType().Name} [P{Priority}]/(none)";

		var task = _tasks[_currentTaskIndex];

		return $"{GetType().Name} [P{Priority}]/{task.GetType().Name}";
	}
}
