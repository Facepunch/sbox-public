namespace Sandbox.PanelGallery;

/// <summary>The scene sessions and viewport, retained as one dockable pane.</summary>
public class SceneContent : Panel
{
	Panel sceneTabs;
	int openScenes = -1;
	RealTimeSince timeSinceScenesChecked;

	/// <summary>Something was clicked in the scene view.</summary>
	public Action<GameObject> OnPicked { get; set; }

	public SceneContent()
	{
		AddClass( "editor-scene-content" );
		BuildSceneTabs();
		AddChild( new ViewportToolbar() );
		var viewport = AddChild( new Viewport() );
		viewport.OnPicked = x => OnPicked?.Invoke( x );
	}

	public override void Tick()
	{
		base.Tick();
		if ( timeSinceScenesChecked < 0.5f ) return;
		timeSinceScenesChecked = 0;
		var hash = SceneEditorSession.All.Count * 31 + (SceneEditorSession.Active?.GetHashCode() ?? 0);
		if ( hash == openScenes ) return;
		openScenes = hash;
		RebuildSceneTabs();
	}

	void BuildSceneTabs()
	{
		sceneTabs = Add.Panel( "scenetabs" );
		RebuildSceneTabs();
	}

	void RebuildSceneTabs()
	{
		sceneTabs.DeleteChildren( true );
		sceneTabs.SetClass( "empty", SceneEditorSession.All.Count == 0 );

		foreach ( var session in SceneEditorSession.All )
		{
			var tab = sceneTabs.Add.Panel( "scenetab" );
			tab.SetClass( "active", session == SceneEditorSession.Active );
			tab.Icon( session.Scene?.IsEditor == true ? "grid_on" : "movie" );
			tab.Add.Label( session.Scene?.Name ?? "Untitled" );

			if ( session.HasUnsavedChanges ) tab.Add.Label( "*", "dirty" );

			tab.AddEventListener( "onclick", () =>
			{
				session.MakeActive();
				RebuildSceneTabs();
			} );
		}

		sceneTabs.Add.Panel( "grow" );
	}

}
