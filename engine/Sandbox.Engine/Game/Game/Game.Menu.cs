namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// True while the game is on its <c>MenuScene</c>. Changing to any other scene starts the game
	/// (<see cref="ChangeScene(SceneFile)"/>), and changing back to it leaves any session.
	/// <see cref="ISceneStartup"/> fires on the menu scene too, so check this before hosting or spawning there.
	/// </summary>
	public static bool IsOnMenu => IGameInstance.Current?.IsOnMenu ?? false;

	/// <summary>
	/// Leave the game for its <c>MenuScene</c>, out of any session - the same as changing to it. A game without one,
	/// or joined on someone else's server, is closed instead (<see cref="Close"/>).
	/// </summary>
	public static async void ReturnToMenu()
	{
		// Not from inside whatever called us - that's usually a button in the scene this throws away
		await Task.Yield();

		if ( IGameInstance.Current is { } instance && !instance.ReturnToMenu() )
			CloseGame();
	}
}
