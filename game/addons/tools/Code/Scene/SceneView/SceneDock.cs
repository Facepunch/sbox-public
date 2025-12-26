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

	public SceneDock( SceneEditorSession session ) : base( null )
	{
		_editorSession = session;

		Layout = Layout.Row();
		Layout.Add( new SceneViewWidget( session, this ) );
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

		_editorSession.Destroy();
		_editorSession = null;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );
		if ( visible )
		{
			Log.Info($"SceneDock.OnVisibilityChanged.l52 visible {visible} {Session.Scene.Name}");
			Session.MakeActive();
		}
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		base.OnFocus( reason );
		Log.Info($"SceneDock.OnFocus.l62 {Session.Scene.Name}");

		Session.MakeActive();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		Session.MakeActive();
	}
}
