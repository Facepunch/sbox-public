namespace Sandbox.Npcs;

/// <summary>
/// A task
/// </summary>
public abstract class TaskBase
{
	protected ScheduleBase Schedule { get; private set; }
	protected Npc Npc => Schedule.Npc;

	/// <summary>
	/// What is the current status of this task?
	/// </summary>
	protected TaskStatus Status { get; private set; }

	internal void Initialize( ScheduleBase schedule )
	{
		Schedule = schedule;
		InternalStart();
	}

	private void InternalStart()
	{
		Status = TaskStatus.Running;
		OnStart();
	}

	internal TaskStatus InternalUpdate()
	{
		Status = OnUpdate();
		return Status;
	}

	internal void InternalEnd()
	{
		if ( Status == TaskStatus.Running )
		{
			Status = TaskStatus.Success;
		}

		OnEnd();
		Reset();
	}

	protected virtual void OnStart() { }
	protected abstract TaskStatus OnUpdate();
	protected virtual void OnEnd() { }
	protected virtual void Reset() { }
}
