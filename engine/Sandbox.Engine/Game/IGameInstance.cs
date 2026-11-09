namespace Sandbox;

/// <summary>
/// Todo: make internal - the only thing using ir right now is the binds system
/// </summary>
public interface IGameInstance
{
	public static IGameInstance Current { get; internal set; }

	internal Package Package { get; }
	internal void Close();

	/// <summary>
	/// On its menu - loaded with <see cref="Sandbox.Engine.GameLoadingFlags.Menu"/>, or back from a game, and not started
	/// </summary>
	internal bool IsOnMenu { get; }

	/// <summary>
	/// On its menu - load what the menu didn't need, then open the startup scene. False if it wasn't on its menu, or the
	/// startup scene didn't open - then it's back on its menu if that opened.
	/// </summary>
	internal Task<bool> StartFromMenu();

	/// <summary>
	/// Back to its menu, still loaded. False if it can't be - its code is a server's, or it has no menu scene and the
	/// platform menu isn't up to start it from.
	/// </summary>
	internal bool ReturnToMenu();

	internal InputSettings InputSettings { get; }

	public void ResetBinds();
	public void SaveBinds();
	public string GetBind( string actionName, out bool isDefault, out bool isCommon );
	public void SetBind( string actionName, string buttonName );
	public void TrapButtons( Action<string[]> callback );

	/// <summary>
	/// Called after all loading is done, right before we enter the game
	/// </summary>
	internal void OnLoadingFinished();

	/// <summary>
	/// True after the game is fully loaded
	/// </summary>
	public bool IsLoading { get; }

	public Scene Scene { get; }
}
