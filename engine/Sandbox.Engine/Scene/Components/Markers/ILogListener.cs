namespace Sandbox;

public partial class Component
{
	/// <summary>
	/// Receives diagnostics emitted while this runtime scene is active in the emitting context.
	/// Callbacks run on the scene update thread. Disabled components do not receive events.
	/// Delivery is bounded to the newest 512 pending events; this is not a durable log archive.
	/// Messages produced by a listener are logged normally but are not delivered to listeners again.
	/// </summary>
	public interface ILogListener
	{
		/// <summary>
		/// Called for a queued log event. The event contains text and weak arguments, not a retained exception.
		/// Engine diagnostics emitted in the scene's context are included; unrelated menu/editor scenes are excluded.
		/// </summary>
		void OnLog( LogEvent entry );
	}
}
