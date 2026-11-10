namespace Editor;

/// <summary>
/// The scene dock is the actual tab that is shown in the editor. Its main
/// job is to host the SceneViewWidget and to switch the active session when
/// the dock is hovered or focused. It also destroys the session when the dock
/// is closed.
/// </summary>
/// Sol: does this need to exist? can't we just dock the view widget directly?
public partial class SceneDock : Widget
{
	public SceneEditorSession Session => _editorSession.GameSession ?? _editorSession;
	private SceneEditorSession _editorSession;
	private SceneViewWidget _sceneView;

	public SceneDock( SceneEditorSession session ) : base( null )
	{
		_editorSession = session;

		Layout = Layout.Row();
		_sceneView = Layout.Add( new SceneViewWidget( session, this ) );
		DeleteOnClose = true;

		Name = session.Scene.Source?.ResourcePath;
	}

	protected override bool OnClose()
	{
		if ( _editorSession.HasUnsavedChanges )
		{
			this.ShowUnsavedChangesDialog(
				assetName: _editorSession.Scene.Name,
				assetType: _editorSession.IsPrefabSession ? "prefab" : "scene",
				onSave: () => _editorSession.Save( false ) );

			return false;
		}

		return true;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		// Stopping the session sends scene.stop, which must not rebuild the view while Qt is tearing us down
		_sceneView?.Destroy();
		_sceneView = null;

		_editorSession.Destroy();
		_editorSession = null;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		if ( visible )
		{
			Session.MakeActive();

			// Focus the viewport for keybinds without activating an editor whose tabs changed in the background.
			var viewport = _sceneView?.LastSelectedViewportWidget;
			if ( viewport.IsValid() )
				viewport.Focus( activateWindow: false );
		}
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		base.OnFocus( reason );

		Session.MakeActive();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		Session.MakeActive();
	}
}
