/// <summary>
/// A component that can be driven by the player occupying a linked seat.
/// <see cref="OnControl"/> runs once per fixed update with the player's input connection active.
/// </summary>
public interface IPlayerControllable
{
	bool CanControl( Player player ) => true;
	void OnControl();
}
