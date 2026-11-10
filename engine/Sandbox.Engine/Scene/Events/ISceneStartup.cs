namespace Sandbox;

/// <summary>
/// Allows listening to events related to scene startup. This should really only apply to GameObjectSystem's
/// because components won't have been spawned/created when most of this is invoked.
/// <para>
/// A game with a menu scene gets these twice: on its menu scene, then on the scene it changes to when it starts.
/// Check <see cref="Game.IsOnMenu"/> before doing what starts the game, like hosting a lobby.
/// </para>
/// </summary>
public interface ISceneStartup : ISceneEvent<ISceneStartup>
{
	/// <summary>
	/// Called before the scene is loaded. In game only, on host only.
	/// </summary>
	void OnHostPreInitialize( SceneFile scene ) { }

	/// <summary>
	/// Called after the scene is loaded. In game only, on the host only.
	/// </summary>
	void OnHostInitialize() { }

	/// <summary>
	/// Called in game after the client has loaded the initial scene from the server, or after OnHostInitialize. 
	/// This is not called on the dedicated server.
	/// </summary>
	void OnClientInitialize() { }
}
