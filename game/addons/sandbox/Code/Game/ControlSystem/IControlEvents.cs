/// <summary>
/// Data passed to <see cref="Global.IControlEvents"/>.
/// </summary>
public class ControlEvent
{
	public Player Player { get; init; }

	/// <summary>
	/// The seat they sat in, or the prop they drive.
	/// </summary>
	public GameObject Target { get; init; }
}

/// <summary>
/// Allows listening to players taking control of things - sitting in seats, which drives whatever the seat is
/// built into, and driving props. Use it for vehicle HUDs or tracking passengers. Runs on the host.
/// </summary>
public static partial class Global
{
	public interface IControlEvents : ISceneEvent<IControlEvents>
	{
		void OnControlStarted( ControlEvent e ) { }
		void OnControlStopped( ControlEvent e ) { }
	}
}
