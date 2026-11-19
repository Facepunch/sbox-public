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
	/// The scene it's on is its menu scene
	/// </summary>
	internal bool IsOnMenu { get; }

	/// <summary>
	/// Back to its menu scene, out of any session. False if it can't be - its code is a server's, or it has no menu
	/// scene.
	/// </summary>
	internal bool ReturnToMenu();

	/// <summary>
	/// The active scene is being replaced by <paramref name="scene"/>. True if that's the game leaving its menu scene -
	/// it's starting. Its menu scene takes it out of any session.
	/// </summary>
	internal bool OnSceneChange( SceneFile scene );

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
