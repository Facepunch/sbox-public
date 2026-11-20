using Sandbox.Npcs.Schedules;

namespace Sandbox.Npcs.Tasks;

/// <summary>
/// One step of a <see cref="BaseSchedule"/>.
/// </summary>
[Expose]
public abstract class BaseTask
{
	/// <summary>
	/// The schedule this task belongs to.
	/// </summary>
	private BaseSchedule Schedule { get; set; }
	/// <summary>
	/// The NPC running this task.
	/// </summary>
	protected BaseNpc Npc => Schedule.Npc;

	/// <summary>
	/// What is the current status of this task? In <see cref="OnEnd"/> it is how the task ended:
	/// <see cref="TaskResult.Failed"/> when it was cut short while still running.
	/// </summary>
	protected TaskResult Status { get; private set; }

	internal void Initialize( BaseSchedule schedule )
	{
		Schedule = schedule;
		Status = TaskResult.Running;
		OnStart();
	}

	internal TaskResult InternalUpdate()
	{
		Status = OnUpdate();
		return Status;
	}

	// Ends the task once. A task still running is cut short, and is told why.
	internal void InternalEnd( TaskResult reason )
	{
		if ( Status == TaskResult.Running )
			Status = reason == TaskResult.Running ? TaskResult.Failed : reason;

		try
		{
			OnEnd();
		}
		finally
		{
			Reset();
		}
	}

	/// <summary>
	/// Called once when the task becomes its schedule's current task.
	/// </summary>
	protected virtual void OnStart() { }
	/// <summary>
	/// Called every update while the task runs: return <see cref="TaskResult.Running"/> to keep going, or how it ended.
	/// </summary>
	protected abstract TaskResult OnUpdate();

	/// <summary>
	/// The task is over, whether it finished or was cut short. <see cref="Status"/> says which.
	/// </summary>
	protected virtual void OnEnd() { }
	/// <summary>
	/// Called right after <see cref="OnEnd"/>, to clear whatever state the task kept while it ran.
	/// </summary>
	protected virtual void Reset() { }
}
