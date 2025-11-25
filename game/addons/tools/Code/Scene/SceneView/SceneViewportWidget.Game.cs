namespace Editor;

public partial class SceneViewportWidget
{
	public void StartPlay()
	{
		Editor.GameMode.SetPlayWidget( Renderer );
		Renderer.Scene = Session.ActiveGameScene;
		Renderer.Camera = null;
		Renderer.EnableEngineOverlays = true;
		ViewportOptions.Visible = false;
	}

	public void StopPlay()
	{
		Editor.GameMode.ClearPlayMode();
		Renderer.Scene = Session.Scene;
		_activeCamera = _editorCamera;
		Renderer.Camera = _activeCamera;
		Renderer.EnableEngineOverlays = false;
		ViewportOptions.Visible = true;
		SetDefaultSize();
	}

	public void EjectGameCamera()
	{
		Editor.GameMode.ClearPlayMode();

		var gameCamera = Renderer.Scene.Camera;
		if ( gameCamera.IsValid() )
		{
			// put the scene camera at the game cam's transform
			State.CameraPosition = gameCamera.WorldPosition;
			State.CameraRotation = gameCamera.WorldRotation;
		}

		if ( !_ejectCamera.IsValid() )
			_ejectCamera = Renderer.CreateSceneEditorCamera();

		_activeCamera = _ejectCamera;
		Renderer.Camera = _activeCamera;
		Renderer.EnableEngineOverlays = false;
		ViewportOptions.Visible = true;
		SetDefaultSize();
	}

	public void PossesGameCamera()
	{
		Editor.GameMode.SetPlayWidget( Renderer );
		_activeCamera = null;
		Renderer.Camera = null;
		Renderer.EnableEngineOverlays = true;
		ViewportOptions.Visible = false;
	}
}
