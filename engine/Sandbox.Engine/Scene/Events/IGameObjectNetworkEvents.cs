namespace Sandbox;

/// <summary>
/// Allows listening to network events on a specific GameObject
/// </summary>
public interface IGameObjectNetworkEvents : ISceneEvent<IGameObjectNetworkEvents>
{
	/// <summary>
	/// Called before we are about to drop ownership of a network GameObject
	/// </summary>
	internal void BeforeDropOwnership() { }

	/// <summary>
	/// Called when the owner of a network GameObject is changed
	/// </summary>
	void NetworkOwnerChanged( Connection newOwner, Connection previousOwner ) { }

	/// <summary>
	/// We have become the controller of this object, we are no longer a proxy
	/// </summary>
	void StartControl() { }

	/// <summary>
	/// This object has become a proxy, controlled by someone else
	/// </summary>
	void StopControl() { }
}
