namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Task that waits for a specified duration
/// </summary>
public class Wait : TaskBase
{
	public float Duration { get; set; }
	private TimeUntil _endTime;

	public Wait( float duration )
	{
		Duration = duration;
	}

	protected override void OnStart()
	{
		_endTime = Duration;
	}

	protected override TaskStatus OnUpdate()
	{
		return _endTime ? TaskStatus.Success : TaskStatus.Running;
	}
}
