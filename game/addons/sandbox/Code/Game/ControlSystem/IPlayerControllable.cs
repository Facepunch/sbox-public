/// <summary>
/// A component that can be driven by the player occupying a linked seat.
/// <see cref="OnControl"/> runs once per fixed update with the player's input connection active.
/// </summary>
public interface IPlayerControllable
{
	/// <summary>
	/// Whether this component accepts input from the seated player.
	/// </summary>
	bool CanControl( Player player ) => true;

	/// <summary>
	/// Apply the driver's input during the host's fixed update.
	/// </summary>
	void OnControl();
}
