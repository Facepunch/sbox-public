namespace Sandbox.UI;

public partial class Panel
{
	// The scene we're currently registered in (if any), so we know what to remove ourselves from.
	Scene _indexedScene;

	/// <summary>
	/// Keep this panel registered in its scene's object index, so it can be found by
	/// <c>Scene.GetAll&lt;T&gt;</c> / <c>Scene.RunEvent&lt;T&gt;</c> / <c>ISceneEvent&lt;T&gt;</c> exactly
	/// like a component. Idempotent and cheap - a no-op while our scene hasn't changed.
	/// </summary>
	void UpdateSceneIndex()
	{
		var scene = Scene;
		if ( scene == _indexedScene )
			return;

		_indexedScene?.RemoveObjectFromDirectory( this );
		_indexedScene = scene;
		_indexedScene?.AddObjectToDirectory( this );
	}

	/// <summary>
	/// Drop ourselves from the scene object index. Called when the panel is deleted.
	/// </summary>
	void RemoveFromSceneIndex()
	{
		if ( _indexedScene is null )
			return;

		_indexedScene.RemoveObjectFromDirectory( this );
		_indexedScene = null;
	}
}
