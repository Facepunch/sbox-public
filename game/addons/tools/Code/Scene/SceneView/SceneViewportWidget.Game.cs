namespace Editor;

public partial class SceneViewportWidget
{
	/// <summary>
	/// Is this viewport the game view?
	/// </summary>
	public bool IsGameView { get; private set; }

	/// <summary>
	/// Called when the SceneView's view mode changes.
	/// </summary>
	public void OnViewModeChanged( SceneViewWidget.ViewMode viewMode )
	{
		Renderer.Scene = Session.Scene;
		GizmoInstance.Selection = Session.Selection;

		if ( _editorCamera.IsValid() && _editorCamera.Scene != Session.Scene )
		{
			// make sure the editor camera exists in the correct scene
			_editorCamera.DestroyGameObject();
			_editorCamera = Renderer.CreateSceneEditorCamera();
		}

		_activeCamera = viewMode switch
		{
			SceneViewWidget.ViewMode.Game => null,
			SceneViewWidget.ViewMode.GameEjected => _ejectCamera,
			_ => _editorCamera,
		};

		Renderer.Camera = _activeCamera;
		Renderer.EnableEngineOverlays = IsGameView;
		ViewportOptions.Visible = !IsGameView;
	}

	/// <summary>
	/// Set this viewport as the game view.
	/// </summary>
	public void SetGameView()
	{
		GameMode.SetPlayWidget( Renderer );
		IsGameView = true;
	}

	/// <summary>
	/// Clear this viewport as the game view.
	/// </summary>
	public void ClearGameView()
	{
		GameMode.ClearPlayMode();
		IsGameView = false;

		SetDefaultSize();
	}

	/// <summary>
	/// Called when ejecting from the game state.
	/// </summary>
	public void OnEject()
	{
		GameMode.ClearPlayMode();
		IsGameView = false;

		SetDefaultSize();

		var gameCamera = Renderer.Scene.Camera;
		if ( gameCamera.IsValid() )
		{
			// put the scene camera at the game cam's transform
			State.CameraPosition = gameCamera.WorldPosition;
			State.CameraRotation = gameCamera.WorldRotation;
		}

		if ( !_ejectCamera.IsValid() )
			_ejectCamera = Renderer.CreateSceneEditorCamera();
	}

	/// <summary>
	/// Called when possessing back into the game state.
	/// </summary>
	public void OnPossessGame()
	{
		GameMode.SetPlayWidget( Renderer );
		IsGameView = true;
	}
}
