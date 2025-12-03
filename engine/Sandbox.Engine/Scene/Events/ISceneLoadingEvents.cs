namespace Sandbox;

/// <summary>
/// Allows listening to events related to scene loading
/// </summary>
public interface ISceneLoadingEvents : ISceneEvent<ISceneLoadingEvents>
{
	/// <summary>
	/// Called before the loading starts
	/// </summary>
	void BeforeLoad( Scene scene, SceneLoadOptions options ) { }

	/// <summary>
	/// Called during loading. The game will wait for your task to finish
	/// </summary>
	Task OnLoad( Scene scene, SceneLoadOptions options ) { return Task.CompletedTask; }

	/// <summary>
	/// Loading has finished
	/// </summary>
	void AfterLoad( Scene scene ) { }
}
