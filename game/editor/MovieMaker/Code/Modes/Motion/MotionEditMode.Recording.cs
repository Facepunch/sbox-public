using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private bool _stopPlayingAfterRecording;

	public override bool AllowRecording => true;

	private MovieClipRecorder? _recorder;
	private MovieTime _recordingLastTime;

	protected override bool OnStartRecording()
	{
		ClearChanges();
		TimeSelection = null;

		var samplePeriod = MovieTime.FromFrames( 1, Project.SampleRate );
		var startTime = Session.PlayheadTime.Floor( samplePeriod );

		Session.PlayheadTime = startTime;
		Session.Player.Clip = null;

		var options = new RecorderOptions( Project.SampleRate );

		_recorder = new MovieClipRecorder( Session.Binder, options, startTime );
		_stopPlayingAfterRecording = !Session.IsPlaying;
		_recordingLastTime = startTime;

		foreach ( var view in Session.TrackList.EditablePropertyTracks )
		{
			_recorder.Tracks.Add( (IProjectPropertyTrack)view.Track );
		}

		Session.IsPlaying = true;

		return true;
	}

	protected override void OnStopRecording()
	{
		if ( _recorder is not { } recorder ) return;

		var timeRange = recorder.TimeRange;

		if ( _stopPlayingAfterRecording )
		{
			Session.IsPlaying = false;
		}

		Session.Player.Clip = Session.Project;

		SetModification<BlendModification>( new TimeSelection( recorder.TimeRange, DefaultInterpolation ) )
			.SetFromMovieClip( recorder.ToClip(), recorder.TimeRange, 0d, false );

		CommitChanges();
		DisplayAction( "radio_button_checked" );

		Session.PlayheadTime = timeRange.End;
	}

	private void RecordingFrame()
	{
		if ( !Session.IsRecording ) return;

		var time = Session.PlayheadTime;
		var deltaTime = MovieTime.Max( time - _recordingLastTime, 0d );

		if ( _recorder?.Advance( deltaTime ) is true )
		{
			foreach ( var trackRecorder in _recorder.Tracks )
			{
				var track = (IProjectPropertyTrack)trackRecorder.Track;

				if ( Session.TrackList.Find( track ) is not { } view ) continue;
				if ( view.Parent?.IsExpanded is false ) continue;

				var finishedBlocks = trackRecorder.FinishedBlocks;

				if ( trackRecorder.CurrentBlock is { } current )
				{
					view.SetPreviewBlocks( [], [..finishedBlocks, current] );
				}
				else
				{
					view.SetPreviewBlocks( [], finishedBlocks );
				}
			}
		}

		Timeline.PanToPlayheadTime();

		_recordingLastTime = time;
	}
}
