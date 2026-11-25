namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Task that waits for a specified duration
/// </summary>
public class WaitTask : BaseTask
{
	/// <summary>
	/// How long to wait, in seconds.
	/// </summary>
	public float Duration { get; }
	private TimeUntil _endTime;

	public WaitTask( float duration )
	{
		Duration = duration;
	}

	protected override void OnStart()
	{
		_endTime = Duration;
	}

	protected override TaskResult OnUpdate()
	{
		return _endTime ? TaskResult.Success : TaskResult.Running;
	}
}
