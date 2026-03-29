using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Panel containing the timeline.
/// </summary>
public sealed class TimelinePanel : MovieEditorPanel
{
	public Timeline Timeline { get; }

	public TimelinePanel( MovieEditor parent, Session session )
		: base( parent )
	{
		Timeline = new Timeline( session );

		MouseTracking = true;

		Layout.Add( Timeline );

		var playPauseGroup = ToolBar.AddGroup( true );

		var navToStartDisplay = new ToolBarItemDisplay( "Navigate to Start", "first_page",
			"Move the playhead to the start of the movie.",
			ShortcutIdentifier: "timeline.navtostart",
			Background: false );

		playPauseGroup.AddAction( navToStartDisplay, parent.NavigateToStart, () => session.PlayheadTime != 0d );

		var recordDisplay = new ToolBarItemDisplay( "Toggle Record", "radio_button_checked",
			"Start or stop recording live changes to tracks in the track list. Records the entire scene if the track list is empty.",
			ShortcutIdentifier: "timeline.recordtoggle",
			Background: false );

		playPauseGroup.AddToggle( recordDisplay, () => session.IsRecording, x => session.IsRecording = x )
			.ForegroundActive = Theme.Red;

		var playDisplay = new ToolBarItemDisplay( "Toggle Play", "play_arrow",
			"Start or stop playback.",
			ShortcutIdentifier: "timeline.playtoggle",
			Background: false );

		playPauseGroup.AddToggle( playDisplay, () => session.IsPlaying, x => session.IsPlaying = x );

		var navToEndDisplay = new ToolBarItemDisplay( "Navigate to End", "last_page",
			"Move the playhead to the end of the movie.",
			ShortcutIdentifier: "timeline.navtoend",
			Background: false );

		playPauseGroup.AddAction( navToEndDisplay, parent.NavigateToEnd, () => session.PlayheadTime != session.Duration );

		var playbackGroup = ToolBar.AddGroup( true );

		var loopDisplay = new ToolBarItemDisplay( "Toggle Looping", "repeat",
			"When enabled, playback will jump back to the start when reaching the end of the clip.",
			Background: false );

		playbackGroup.AddToggle( loopDisplay, () => session.IsLooping, x => session.IsLooping = x );

		var slider = new FloatSlider( null )
		{
			ToolTip = "Playback Rate",
			FixedWidth = 80f,
			Minimum = 0f,
			Maximum = 2f,
			Step = 0.1f
		};

		slider.Bind( nameof( FloatSlider.Value ) )
			.From( () => session.TimeScale, value => session.TimeScale = value );

		playbackGroup.Layout.Add( slider );
		var speed = new Label( null )
		{
			Color = Color.White.Darken( 0.5f ),
			FixedWidth = 30f,
			Margin = 4f,
			Alignment = TextFlag.Center
		};

		speed.Bind( nameof( Label.Text ) )
			.ReadOnly()
			.From( () => $"x{session.TimeScale:F1}", null );

		slider.MouseRightClick += () => session.TimeScale = 1f;
		speed.MouseRightClick += () => session.TimeScale = 1f;

		playbackGroup.Layout.Add( speed );

		var syncDisplay = new ToolBarItemDisplay( "Toggle Playback Sync", "sync_lock",
			"When enabled, all MoviePlayers in the scene will sync with the currently open one when previewing a frame.",
			Background: false );

		playbackGroup.AddToggle( syncDisplay, () => session.SyncPlayback, x => session.SyncPlayback = x );

		if ( EditMode.AllTypes is { Count: > 1 } editModes )
		{
			var editModeGroup = ToolBar.AddGroup( true );

			foreach ( var editModeType in editModes )
			{
				var display = new ToolBarItemDisplay( editModeType.Title, editModeType.Icon, editModeType.Description );

				editModeGroup.AddToggle( display,
					() => editModeType.IsMatchingType( session.EditMode ),
					state => session.SetEditMode( state ? editModeType : null ) );
			}
		}

		var snapGroup = ToolBar.AddGroup( true, true );

		var objectSnapDisplay = new ToolBarItemDisplay( "Object Snap", "align_horizontal_left",
			"Snap to objects in the timeline.",
			Background: false );

		var frameSnapDisplay = new ToolBarItemDisplay( "Frame Snap", "straighten",
			"Snap to the selected frame rate.",
			Background: false );

		snapGroup.AddToggle( objectSnapDisplay, () => session.ObjectSnap, x => session.ObjectSnap = x );

		snapGroup.AddToggle( frameSnapDisplay, () => session.FrameSnap, x => session.FrameSnap = x );

		snapGroup.AddSpacingCell();

		var rate = new FpsComboBox { ToolTip = "Snap Frame Rate" };

		rate.Bind( "Value" ).From(
			() => session.FrameRate,
			value => session.FrameRate = value );

		snapGroup.Layout.Add( rate );

		var showHistoryGroup = ToolBar.AddGroup( true, true );
		var showHistoryDisplay = new ToolBarItemDisplay( "Show History", "schedule",
			"Show the edit history panel.",
			Background: false );

		showHistoryGroup.Bind( "Visible" )
			.ReadOnly()
			.From( () => !parent.ShowHistory, null );

		showHistoryGroup.AddToggle( showHistoryDisplay,
			() => false,
			value => parent.ShowHistory = true );
	}
}

file class FpsComboBox : IconComboBox<int>
{
	public FpsComboBox()
	{
		IconAspect = 5f;
	}

	protected override IEnumerable<int> OnGetOptions() => MovieTime.SupportedFrameRates
		.Intersect( [1, 2, 4, 5, 10, 15, 20, 24, 30, 48, 60, 90, 100, 120] );

	protected override string OnGetOptionTitle( int option ) => $"{option} FPS";

	protected override void OnPaintOptionIcon( int option, Rect rect )
	{
		Paint.DrawText( rect, $"{option} FPS" );
	}
}
