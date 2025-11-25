namespace Editor;

public partial class SceneViewWidget
{
	public enum ViewMode
	{
		Scene,
		Game,
		GameEjected
	}

	public ViewMode CurrentView { get; private set; }
	private ViewMode lastView;

	private SceneViewportWidget _gameViewport;

	[Event( "scene.play" )]
	public void OnScenePlay()
	{
		if ( !Session.HasActiveGameScene ) return;
		CurrentView = ViewMode.Game;

		OnViewModeChanged();

		_gameViewport = _viewports.FirstOrDefault().Value;
		_gameViewport.StartPlay();

		_viewportTools.UpdateViewportFromCookie();
	}

	[Event( "scene.stop" )]
	public void OnSceneStop()
	{
		if ( !_gameViewport.IsValid() ) return;
		CurrentView = ViewMode.Scene;

		_gameViewport.StopPlay();
		_gameViewport = null;

		OnViewModeChanged();
	}

	public void ToggleEject()
	{
		if ( !Session.HasActiveGameScene ) return;

		CurrentView = CurrentView == ViewMode.Game ? ViewMode.GameEjected : ViewMode.Game;

		if ( CurrentView == ViewMode.Game )
		{
			_gameViewport.PossesGameCamera();
		}
		else if ( CurrentView == ViewMode.GameEjected )
		{
			_gameViewport.EjectGameCamera();
		}

		OnViewModeChanged();
	}

	/// <summary>
	/// Current view mode changed, we need to hide or show some UI things.
	/// </summary>
	void OnViewModeChanged()
	{
		_viewportTools.Rebuild();
		_sidePanel?.Visible = CurrentView != ViewMode.Game;
	}

	public SceneViewportWidget GetGameTarget()
	{
		return _gameViewport;
	}

	/// <summary>
	/// Set the game viewport to free sizing mode
	/// </summary>
	public void SetFreeSize()
	{
		var viewport = GetGameTarget();
		if ( viewport.IsValid() )
		{
			viewport.SetDefaultSize();
		}
	}

	/// <summary>
	/// Set the game viewport to a specific aspect ratio
	/// </summary>
	public void SetForceAspect( float aspect )
	{
		var viewport = GetGameTarget();
		if ( viewport.IsValid() )
		{
			viewport.SetAspectRatio( aspect );
		}
	}

	/// <summary>
	/// Set the game viewport to a specific resolution
	/// </summary>
	public void SetForceResolution( Vector2 resolution )
	{
		var viewport = GetGameTarget();
		if ( viewport.IsValid() )
		{
			viewport.SetResolution( resolution );
		}
	}
}
