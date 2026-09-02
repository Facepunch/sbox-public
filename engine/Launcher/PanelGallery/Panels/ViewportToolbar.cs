namespace Sandbox.PanelGallery;

/// <summary>
/// The row above the scene view. Everything on it drives the editor itself - the snap settings are
/// the ones the gizmos use, and the play buttons start and stop the game - so it stays in step with
/// the editor's own toolbar.
/// </summary>
public class ViewportToolbar : Panel
{
	Panel playButton;
	Sandbox.UI.Label playIcon;
	Panel pauseButton;

	Panel gridSnap;
	Panel angleSnap;

	public ViewportToolbar()
	{
		AddClass( "viewporttoolbar" );

		BuildLeft();
		Add.Panel( "grow" );
		BuildRight();

		// Over the top of the row, so it's centred on the bar rather than on what's left of it
		BuildPlay();
	}

	/// <summary>
	/// The editor's gizmo settings - the same object its own toolbar edits, so snapping changes
	/// here apply to the editor's viewport too. Gizmo.Settings is only valid inside a gizmo scope.
	/// </summary>
	static Gizmo.SceneSettings Settings => EditorScene.GizmoSettings;

	void BuildLeft()
	{
		this.Segmented( ["Local", "World"], Settings.GlobalSpace ? 1 : 0, index => Settings.GlobalSpace = index == 1 );

		Add.Panel( "divider" );

		// Angle snapping, and how far each notch turns
		angleSnap = Toggle( "360", () => Settings.SnapToAngles, value => Settings.SnapToAngles = value );

		var angle = AddChild( new NumberBox( null, Settings.AngleSpacing, 1.0f ) );
		angle.OnChange = value => Settings.AngleSpacing = value.Clamp( 0.25f, 180.0f );
		angle.Add.Label( "°", "unit" );

		Add.Panel( "divider" );

		// Grid snapping, and how big the squares are
		gridSnap = Toggle( "grid_on", () => Settings.SnapToGrid, value => Settings.SnapToGrid = value );

		var grid = AddChild( new NumberBox( null, Settings.GridSpacing, 1.0f ) );
		grid.OnChange = value => Settings.GridSpacing = value.Clamp( 0.125f, 128.0f );
	}

	Panel Toggle( string icon, Func<bool> get, Action<bool> set )
	{
		var button = this.IconButton( icon, () => set( !get() ) );
		button.SetClass( "active", get() );

		return button;
	}

	void BuildPlay()
	{
		var group = Add.Panel( "playcontrols" );

		playButton = group.IconButton( "play_arrow", PlayStop );
		playIcon = playButton.GetChild( 0 ) as Sandbox.UI.Label;
		playButton.AddClass( "play" );

		pauseButton = group.IconButton( "pause", Pause );
	}

	void BuildRight()
	{
		this.IconButton( "wb_sunny", () => { } );
		this.IconButton( "visibility", () => { } );
		this.IconButton( "fullscreen", () => { } );
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
		if ( timeSinceUpdate < 0.2f ) return;
		timeSinceUpdate = 0;

		var playing = Game.IsPlaying;

		playIcon.Text = playing ? "stop" : "play_arrow";
		playButton.SetClass( "playing", playing );
		pauseButton.SetClass( "disabled", !playing );
		pauseButton.SetClass( "active", playing && Game.IsPaused );

		gridSnap.SetClass( "active", Settings.SnapToGrid );
		angleSnap.SetClass( "active", Settings.SnapToAngles );
	}
}
