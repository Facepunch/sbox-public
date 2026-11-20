namespace Sandbox.Npcs.Tasks;

/// <summary>
/// Status returned by task execution
/// </summary>
[Expose]
public enum TaskResult
{
	/// <summary>
	/// Task is still running, continue executing
	/// </summary>
	Running,

	/// <summary>
	/// Task completed successfully
	/// </summary>
	Success,

	/// <summary>
	/// Task failed or was cancelled
	/// </summary>
	Failed
}
