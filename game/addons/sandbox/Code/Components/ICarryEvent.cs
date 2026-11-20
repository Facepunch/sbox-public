/// <summary>
/// Lets an object react to being picked up with use by <see cref="PlayerCarry"/>, or refuse it.
/// Runs on the root of the object.
/// </summary>
public interface ICarryEvent : ISceneEvent<ICarryEvent>
{
	/// <summary>
	/// Called when a player tries to pick this object up. Runs on the machine of the player picking it up,
	/// and again on the instance that simulates the object before it starts moving it for them.
	/// </summary>
	void OnCarryGrab( PlayerCarryEvent e ) { }

	/// <summary>
	/// Called when a player starts carrying this object.
	/// Runs on the instance that simulates the object, the only one that can change its body.
	/// </summary>
	void OnCarryPickup( Player player ) { }

	/// <summary>
	/// Called when a player stops carrying this object, because it was let go, thrown or got out of reach.
	/// Runs on the instance that simulates the object, the only one that can change its body.
	/// </summary>
	void OnCarryDrop( Player player, bool thrown ) { }
}
