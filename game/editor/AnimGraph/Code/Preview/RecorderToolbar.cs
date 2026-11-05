using Sandbox.Animation;

namespace Editor.AnimGraph;

internal enum RecorderPlaybackSpeed
{
	[Title( "0.25x" )]
	Quarter,
	[Title( "0.5x" )]
	Half,
	[Title( "1x" )]
	Normal,
	[Title( "2x" )]
	Double,
	[Title( "4x" )]
	Quadruple
}

internal sealed class RecorderToolbar : Widget
{
	private sealed class PlaybackSettings
	{
		public RecorderPlaybackSpeed Speed { get; set; } = RecorderPlaybackSpeed.Normal;
	}

	private readonly Window _window;
	private readonly IconButton _recordButton;
	private readonly IconButton _clearButton;
	private readonly IconButton _stepBackwardButton;
	private readonly IconButton _playButton;
	private readonly IconButton _stopButton;
	private readonly IconButton _stepForwardButton;
	private readonly IconButton _loopButton;
	private readonly EnumControlWidget _speed;
	private readonly FloatSlider _timeline;
	private readonly List<GraphRecording.UpdateInfo> _frames = [];
	private readonly List<float> _frameTimes = [];
	private readonly PlaybackSettings _playbackSettings = new();

	private bool _wasPaused;
	private bool _reviewActive;
	private bool _playing;
	private bool _loop;
	private bool _updatingTimeline;
	private float _playbackAccumulator;

	private SceneModel Model => _window.PreviewModel;
	private float PlaybackSpeed => _playbackSettings.Speed switch
	{
		RecorderPlaybackSpeed.Quarter => 0.25f,
		RecorderPlaybackSpeed.Half => 0.5f,
		RecorderPlaybackSpeed.Double => 2f,
		RecorderPlaybackSpeed.Quadruple => 4f,
		_ => 1f
	};

	public RecorderToolbar( Window window, Widget parent ) : base( parent )
	{
		_window = window;

		FocusMode = FocusMode.Click;
		FixedHeight = Theme.RowHeight + 2;
		OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( LocalRect );
			return true;
		};

		var controls = Layout = Layout.Row();
		controls.Margin = new Sandbox.UI.Margin( 0, 2, 0, 0 );
		controls.Spacing = 2;

		_recordButton = controls.Add( new IconButton( "fiber_manual_record", ToggleRecording, this )
		{
			ToolTip = "Record live graph",
			Background = Theme.ControlBackground,
			BackgroundActive = Theme.Red,
			Foreground = Theme.Red,
			IconSize = 16
		} );
		_clearButton = controls.Add( new IconButton( "delete", Clear, this )
		{
			ToolTip = "Clear recording",
			Background = Theme.ControlBackground,
			IconSize = 16
		} );
		controls.Add( new Separator( 1 )
		{
			FixedWidth = 1,
			FixedHeight = Theme.RowHeight - 8,
			Color = Color.White.WithAlpha( 0.1f )
		} );

		_stepBackwardButton = controls.Add( new IconButton( "skip_previous", StepBackward, this )
		{
			ToolTip = "Previous update (Left)",
			Background = Theme.ControlBackground,
			IconSize = 16
		} );
		_playButton = controls.Add( new IconButton( "play_arrow", TogglePlayback, this )
		{
			ToolTip = "Play recording (Space)",
			Background = Theme.ControlBackground,
			BackgroundActive = Theme.Blue,
			IconSize = 18
		} );
		_stopButton = controls.Add( new IconButton( "stop", StopReview, this )
		{
			ToolTip = "Stop review",
			Background = Theme.ControlBackground,
			IconSize = 16
		} );
		_stepForwardButton = controls.Add( new IconButton( "skip_next", StepForward, this )
		{
			ToolTip = "Next update (Right)",
			Background = Theme.ControlBackground,
			IconSize = 16
		} );
		_loopButton = controls.Add( new IconButton( "repeat", null, this )
		{
			ToolTip = "Loop playback",
			IsToggle = true,
			Background = Theme.ControlBackground,
			BackgroundActive = Theme.Blue,
			IconSize = 16,
			OnToggled = value => _loop = value
		} );

		var speedProperty = _playbackSettings.GetSerialized().GetProperty( nameof( PlaybackSettings.Speed ) );
		_speed = new EnumControlWidget( speedProperty )
		{
			MinimumWidth = 64,
			MaximumWidth = 64,
			MinimumHeight = Theme.RowHeight,
			MaximumHeight = Theme.RowHeight,
			ToolTip = "Playback speed"
		};
		controls.Add( _speed );

		var timelinePanel = controls.Add( new Widget( this )
		{
			FixedHeight = 24,
			HorizontalSizeMode = SizeMode.Flexible
		}, 1 );
		timelinePanel.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( timelinePanel.LocalRect, 3f );
			return true;
		};

		var timeline = timelinePanel.Layout = Layout.Row();
		timeline.Margin = new Sandbox.UI.Margin( 6, 0 );
		timeline.Spacing = 4;

		_timeline = timeline.Add( new FloatSlider( timelinePanel )
		{
			Minimum = 0,
			Maximum = 0,
			Step = 1,
			Value = 0,
			HighlightColor = Theme.Blue,
			HorizontalSizeMode = SizeMode.Flexible
		}, 1 );
		_timeline.OnValueEdited = Scrub;

		Refresh();
	}

	private void ToggleRecording()
	{
		var model = Model;
		if ( !model.IsValid() || model.Animation.Recording.IsPlaybackActive )
			return;

		if ( model.Animation.Recording.IsRecording )
		{
			model.Animation.Recording.Stop();
		}
		else
		{
			_frames.Clear();
			_frameTimes.Clear();
			_playing = false;
			model.Animation.Recording.Start();
		}

		Refresh();
	}

	private bool StartReview()
	{
		var model = Model;
		if ( !model.IsValid() || model.Animation.Recording.IsRecording || !model.Animation.Recording.HasData )
			return false;

		if ( model.Animation.Recording.IsPlaybackActive )
			return true;

		_wasPaused = _window.IsPreviewPaused;
		if ( !model.Animation.Recording.StartPlayback() )
			return false;

		_window.SetRecordingReviewMode( true, true );
		_reviewActive = true;
		_playbackAccumulator = 0f;
		if ( ApplyRecordedUpdate( 0 ) )
			return true;

		StopReview();
		return false;
	}

	private void StopReview()
	{
		var model = Model;
		if ( !_reviewActive && (!model.IsValid() || !model.Animation.Recording.IsPlaybackActive) )
			return;

		_playing = false;
		_playbackAccumulator = 0f;
		if ( model.IsValid() && model.Animation.Recording.IsPlaybackActive )
			model.Animation.Recording.StopPlayback();

		if ( _reviewActive )
		{
			_window.SetRecordingReviewMode( false, _wasPaused );
			_reviewActive = false;
		}

		Refresh();
	}

	private void TogglePlayback()
	{
		var model = Model;
		if ( !model.IsValid() || model.Animation.Recording.IsRecording )
			return;

		if ( !model.Animation.Recording.IsPlaybackActive && !StartReview() )
			return;

		if ( model.Animation.Recording.CurrentIndex >= model.Animation.Recording.Count - 1 )
			ApplyRecordedUpdate( 0 );

		_playing = !_playing;
		_playbackAccumulator = 0f;
		Refresh();
	}

	private void Seek( int updateIndex )
	{
		if ( !StartReview() )
			return;

		_playing = false;
		_playbackAccumulator = 0f;
		ApplyRecordedUpdate( updateIndex );
	}

	private void Scrub()
	{
		if ( !_updatingTimeline )
			Seek( (int)MathF.Round( _timeline.Value ) );
	}

	private void JumpToStart() => Seek( 0 );

	private void JumpToEnd()
	{
		var count = Model.IsValid() ? Model.Animation.Recording.Count : 0;
		if ( count > 0 )
			Seek( count - 1 );
	}

	private void StepBackward()
	{
		var current = Model.IsValid() ? Model.Animation.Recording.CurrentIndex : 0;
		Seek( Math.Max( 0, current - 1 ) );
	}

	private void StepForward()
	{
		var model = Model;
		var count = model.IsValid() ? model.Animation.Recording.Count : 0;
		if ( count > 0 )
			Seek( Math.Min( count - 1, model.Animation.Recording.CurrentIndex + 1 ) );
	}

	private bool ApplyRecordedUpdate( int updateIndex, bool refresh = true )
	{
		if ( !_window.ApplyRecordedUpdate( updateIndex ) )
			return false;

		if ( refresh )
			Refresh();

		return true;
	}

	internal void Clear()
	{
		var model = Model;
		if ( !model.IsValid() )
		{
			if ( _reviewActive )
				StopReview();

			return;
		}

		if ( model.Animation.Recording.IsRecording )
			model.Animation.Recording.Stop();

		if ( model.Animation.Recording.IsPlaybackActive )
			StopReview();

		model.Animation.Recording.Clear();
		_frames.Clear();
		_frameTimes.Clear();
		_playing = false;
		_playbackAccumulator = 0f;
		Refresh();
	}

	private void RefreshRecordedFrames( SceneModel model, int count )
	{
		if ( count < _frames.Count )
		{
			_frames.Clear();
			_frameTimes.Clear();
		}

		var elapsed = _frameTimes.Count > 0 ? _frameTimes[^1] : 0f;
		for ( var i = _frames.Count; i < count; i++ )
		{
			if ( !model.Animation.Recording.TryGetUpdate( i, out var info ) )
				break;

			_frames.Add( info );
			elapsed += info.DeltaTime;
			_frameTimes.Add( elapsed );
		}
	}

	private void AdvancePlayback()
	{
		var model = Model;
		if ( !_playing || !model.IsValid() || !model.Animation.Recording.IsPlaybackActive )
			return;

		var count = model.Animation.Recording.Count;
		if ( count <= 1 )
		{
			_playing = false;
			return;
		}

		_playbackAccumulator += RealTime.Delta * PlaybackSpeed;
		var current = model.Animation.Recording.CurrentIndex;

		while ( current < count - 1 )
		{
			var next = current + 1;
			var frameDuration = next < _frames.Count ? _frames[next].DeltaTime : 0f;
			if ( frameDuration > 0f && _playbackAccumulator < frameDuration )
				break;

			_playbackAccumulator = Math.Max( 0f, _playbackAccumulator - frameDuration );
			if ( !ApplyRecordedUpdate( next, refresh: false ) )
			{
				_playing = false;
				return;
			}

			current = next;
		}

		if ( current < count - 1 )
			return;

		if ( _loop )
		{
			_playbackAccumulator = 0f;
			ApplyRecordedUpdate( 0, refresh: false );
			return;
		}

		_playing = false;
		_playbackAccumulator = 0f;
	}

	private void Refresh()
	{
		var model = Model;
		var valid = model.IsValid();
		var recording = valid && model.Animation.Recording.IsRecording;
		var reviewing = valid && model.Animation.Recording.IsPlaybackActive;
		var hasRecording = valid && model.Animation.Recording.HasData;
		var canReview = hasRecording && !recording;
		var count = valid ? model.Animation.Recording.Count : 0;
		var current = reviewing ? model.Animation.Recording.CurrentIndex : 0;

		if ( valid )
			RefreshRecordedFrames( model, count );

		_recordButton.Enabled = valid && !reviewing;
		_recordButton.IsActive = recording;
		_recordButton.Icon = recording ? "stop" : "fiber_manual_record";
		_recordButton.ToolTip = recording ? "Stop recording" : "Record live graph";
		_clearButton.Enabled = canReview && !reviewing;

		_stepBackwardButton.Enabled = canReview && (!reviewing || current > 0);
		_playButton.Enabled = canReview;
		_playButton.IsActive = _playing;
		_playButton.Icon = _playing ? "pause" : "play_arrow";
		_playButton.ToolTip = _playing ? "Pause recording (Space)" : "Play recording (Space)";
		_stopButton.Enabled = reviewing;
		_stepForwardButton.Enabled = canReview && (!reviewing || current < count - 1);
		_loopButton.Enabled = canReview;
		_speed.Enabled = canReview;

		_updatingTimeline = true;
		_timeline.Enabled = canReview;
		_timeline.Minimum = 0;
		_timeline.Maximum = Math.Max( 0, count - 1 );
		_timeline.Value = current;
		_updatingTimeline = false;

		var elapsed = current >= 0 && current < _frameTimes.Count ? _frameTimes[current] : 0f;
		var duration = _frameTimes.Count > 0 ? _frameTimes[^1] : 0f;

		string infoText;
		if ( recording )
			infoText = $"Recording · {count} updates";
		else if ( reviewing && current >= 0 && current < _frames.Count )
			infoText = RecordedUpdateText( current, count, _frames[current] );
		else if ( hasRecording )
			infoText = $"{count} recorded updates";
		else
			infoText = valid ? "No recording" : "No preview model";
		ToolTip = infoText;
		ToolTip = infoText;
		_timeline.ToolTip = $"{FormatTime( elapsed )} / {FormatTime( duration )}\n{infoText}";

		Update();
	}

	private static string RecordedUpdateText( int current, int count, GraphRecording.UpdateInfo info )
		=> info.Kind switch
		{
			GraphRecording.UpdateKind.Reset => $"Graph Reset: {current + 1} / {count} · Init Sync Time: {FormatSyncTime( info.EndEvent, info.EndPercentage )}",
			GraphRecording.UpdateKind.FullState or GraphRecording.UpdateKind.TimeStep => $"Update {current + 1} / {count} · {info.DeltaTime * 1000f:0.00} ms",
			GraphRecording.UpdateKind.ExternalGraphChanged => "External Graphs Changed",
			_ => "Unknown"
		};

	private static string FormatSyncTime( int eventIndex, float percentage ) => $"{eventIndex}:{percentage:0.000}";

	private static string FormatTime( float seconds )
	{
		var time = TimeSpan.FromSeconds( Math.Max( 0f, seconds ) );
		return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.Space: TogglePlayback(); break;
			case KeyCode.Left: StepBackward(); break;
			case KeyCode.Right: StepForward(); break;
			case KeyCode.Home: JumpToStart(); break;
			case KeyCode.End: JumpToEnd(); break;
			default:
				base.OnKeyPress( e );
				return;
		}

		e.Accepted = true;
	}

	[EditorEvent.Frame]
	public void Tick()
	{
		if ( !Visible )
			return;

		AdvancePlayback();
		Refresh();
	}
}
