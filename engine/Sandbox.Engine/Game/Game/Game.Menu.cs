namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// True while the game is on its <c>MenuScene</c> - opened there by the s&amp;box menu, and not started yet.
	/// Start it with <see cref="StartGame"/>.
	/// </summary>
	public static bool IsOnMenu => IGameInstance.Current?.IsOnMenu ?? false;

	/// <summary>
	/// Start the game from its menu - its <c>StartupScene</c>, as if it had been launched without a menu. Does
	/// nothing unless <see cref="IsOnMenu"/>.
	/// </summary>
	public static async void StartGame()
	{
		// Not from inside whatever called us - that's usually a button in the menu scene this throws away
		await Task.Yield();

		if ( IGameInstance.Current is not { IsOnMenu: true } instance )
			return;

		// Couldn't open its startup scene, nor its menu again
		if ( !await instance.StartFromMenu() && !instance.IsOnMenu )
			Close();
	}

	/// <summary>
	/// Leave the game for its <c>MenuScene</c> - still loaded, so it's quick. Out of any session first. A game
	/// joined on someone else's server is closed instead (<see cref="Close"/>), as is one without a <c>MenuScene</c>
	/// unless the platform menu's up to start it again from.
	/// </summary>
	public static async void ReturnToMenu()
	{
		// Not from inside whatever called us - that's usually a button in the scene this throws away
		await Task.Yield();

		if ( IGameInstance.Current is { } instance && !instance.ReturnToMenu() )
			Close();
	}
}
