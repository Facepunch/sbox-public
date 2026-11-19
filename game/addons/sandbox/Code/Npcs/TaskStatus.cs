namespace Sandbox.Npcs;

/// <summary>
/// Status returned by task execution
/// </summary>
public enum TaskStatus
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
	Failed,
	
	/// <summary>
	/// Task was interrupted by conditions
	/// </summary>
	Interrupted
}
