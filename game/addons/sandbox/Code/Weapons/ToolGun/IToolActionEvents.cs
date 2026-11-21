/// <summary>
/// Allows listening to tool actions across the scene.
/// Implement this on a <see cref="Component"/> to receive callbacks before and after any tool action fires.
/// Use this to enforce limits (e.g. max balloons, max constraints) or react to tool usage.
/// Runs on the host, from the RPC that carries the action out, so a cancel can't be skipped by the client.
/// Actions that never reach the host, like cycling a tool's local settings, don't fire it.
/// </summary>
public interface IToolActionEvents : ISceneEvent<IToolActionEvents>
{
	/// <summary>
	/// Data passed to <see cref="OnToolAction"/>. Set <see cref="Cancelled"/> to true to prevent the action.
	/// </summary>
	public class ActionData
	{
		/// <summary>
		/// The tool mode that is about to execute.
		/// Check <c>e.Tool is BalloonTool</c>, <c>e.Tool is WeldTool</c>, etc. for tool-specific logic.
		/// </summary>
		public ToolMode Tool { get; init; }

		/// <summary>
		/// Which input triggered this action.
		/// </summary>
		public ToolInput Input { get; init; }

		/// <summary>
		/// The connection of the player performing the action.
		/// </summary>
		public Connection Player { get; init; }

		/// <summary>
		/// Set to true to cancel the action.
		/// </summary>
		public bool Cancelled { get; set; }
	}

	/// <summary>
	/// Data passed to <see cref="OnPostToolAction"/> after a successful action.
	/// </summary>
	public class PostActionData
	{
		/// <summary>
		/// The tool mode that executed the action.
		/// </summary>
		public ToolMode Tool { get; init; }

		/// <summary>
		/// Which input triggered this action.
		/// </summary>
		public ToolInput Input { get; init; }

		/// <summary>
		/// The connection of the player who performed the action.
		/// </summary>
		public Connection Player { get; init; }

		/// <summary>
		/// GameObjects created by this action, if any.
		/// </summary>
		public List<GameObject> CreatedObjects { get; init; }
	}

	/// <summary>
	/// Called before a tool action executes.
	/// Set <see cref="ActionData.Cancelled"/> to true to reject the action.
	/// </summary>
	void OnToolAction( ActionData e ) { }

	/// <summary>
	/// Called after a tool action has executed successfully.
	/// </summary>
	void OnPostToolAction( PostActionData e ) { }
}
