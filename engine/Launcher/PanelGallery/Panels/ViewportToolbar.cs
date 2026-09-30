namespace Sandbox.PanelGallery;

/// <summary>
/// The row above the scene view. Everything on it drives the editor itself - the snap settings are
/// the ones the gizmos use, and the play buttons start and stop the game - so it stays in step with
/// the editor's own toolbar.
/// </summary>
public class ViewportToolbar : Toolbar
{
	Sandbox.UI.Button playButton;
	Sandbox.UI.Button pauseButton;

	Sandbox.UI.Button gridSnap;
	Sandbox.UI.Button angleSnap;
	Sandbox.UI.ButtonGroup space;

	public ViewportToolbar()
	{
		AddClass( "viewporttoolbar" );

		BuildLeft();
		AddSpacer();
		BuildPlay();
		AddSpacer();
		BuildRight();
	}

	/// <summary>
	/// The editor's gizmo settings - the same object its own toolbar edits, so snapping changes
	/// here apply to the editor's viewport too. Gizmo.Settings is only valid inside a gizmo scope.
	/// </summary>
	static Gizmo.SceneSettings Settings => EditorScene.GizmoSettings;

	void BuildLeft()
	{
		space = AddChild( new Sandbox.UI.ButtonGroup
		{
			Options = [new Sandbox.UI.Option( "Local", "local" ), new Sandbox.UI.Option( "World", "world" )],
			Value = Settings.GlobalSpace ? "world" : "local",
			ValueChanged = value => Settings.GlobalSpace = Equals( value, "world" )
		} );

		AddSeparator();

		// Angle snapping, and how far each notch turns
		angleSnap = AddToggle( null, "360", Settings.SnapToAngles, value => Settings.SnapToAngles = value );
		angleSnap.Tooltip = "Snap to angles";

		var angle = AddChild( new NumberBox( null, Settings.AngleSpacing, 1.0f ) );
		angle.OnChange = value => Settings.AngleSpacing = value.Clamp( 0.25f, 180.0f );
		angle.Add.Label( "°", "unit" );

		AddSeparator();

		// Grid snapping, and how big the squares are
		gridSnap = AddToggle( null, "grid_on", Settings.SnapToGrid, value => Settings.SnapToGrid = value );
		gridSnap.Tooltip = "Snap to grid";

		var grid = AddChild( new NumberBox( null, Settings.GridSpacing, 1.0f ) );
		grid.OnChange = value => Settings.GridSpacing = value.Clamp( 0.125f, 128.0f );
	}

	void BuildPlay()
	{
		playButton = AddButton( null, "play_arrow", PlayStop );
		playButton.AddClass( "play" );
		playButton.Tooltip = "Play";

		pauseButton = AddButton( null, "pause", Pause );
		pauseButton.Tooltip = "Pause";
	}

	void BuildRight()
	{
		AddButton( null, "wb_sunny", () => { } ).Tooltip = "Lighting";
		AddButton( null, "visibility", () => { } ).Tooltip = "Visibility";
		AddButton( null, "fullscreen", () => { } ).Tooltip = "Full screen";
	}

	static SceneEditorSession Session => SceneEditorSession.Active;

	static void PlayStop()
	{
		if ( Game.IsPlaying )
		{
			EditorScene.Stop();
			return;
		}

		if ( Session is { } session ) EditorScene.Play( session );
	}

	static void Pause()
	{
		if ( !Game.IsPlaying ) return;

		Game.IsPaused = !Game.IsPaused;
	}

	RealTimeSince timeSinceUpdate;

	public override void Tick()
	{
		base.Tick();
		if ( timeSinceUpdate < 0.2f ) return;
		timeSinceUpdate = 0;

		var playing = Game.IsPlaying;

		playButton.Icon = playing ? "stop" : "play_arrow";
		playButton.Tooltip = playing ? "Stop" : "Play";
		playButton.SetClass( "playing", playing );
		pauseButton.Disabled = !playing;
		pauseButton.Active = playing && Game.IsPaused;

		gridSnap.Active = Settings.SnapToGrid;
		angleSnap.Active = Settings.SnapToAngles;
		space.Value = Settings.GlobalSpace ? "world" : "local";
	}
}
