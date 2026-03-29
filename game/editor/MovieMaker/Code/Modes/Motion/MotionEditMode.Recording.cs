using Sandbox.MovieMaker;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private bool _stopPlayingAfterRecording;

	public override bool AllowRecording => true;

	private MovieRecorder? _recorder;
	private MovieTime _recordingLastTime;

	protected override bool OnStartRecording()
	{
		ClearChanges();
		TimeSelection = null;

		var samplePeriod = MovieTime.FromFrames( 1, Project.SampleRate );
		var startTime = Session.PlayheadTime.Floor( samplePeriod );

		Session.PlayheadTime = startTime;

		// Don't try to play back the in-progress recording

		Session.Player.Clip = Project.Compile();

		_recorder = new MovieRecorder( Session.Binder, CreateRecorderOptions() );
		_recorder.Advance( startTime );
		_stopPlayingAfterRecording = !Session.IsPlaying;
		_recordingLastTime = startTime;

		_recorder.Capture();

		Session.IsPlaying = true;

		return true;
	}

	private MovieRecorderOptions CreateRecorderOptions()
	{
		// If project is empty, auto-record renderers etc in the scene

		return Project.Tracks.Count == 0
			? MovieRecorderOptions.Default with { SampleRate = Project.SampleRate }
			: new MovieRecorderOptions( Project.SampleRate )
				.WithCaptureTracks( Session.TrackList.EditablePropertyTracks.Select( x => x.Track ) );
	}

	protected override void OnStopRecording()
	{
		if ( _recorder is not { } recorder ) return;

		_recorder = null;

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
		if ( !Session.IsRecording || _recorder is null ) return;

		var time = Session.PlayheadTime;
		var deltaTime = MovieTime.Max( time - _recordingLastTime, 0d );

		_recorder.Advance( deltaTime );
		_recorder.Capture();

		var tracksAdded = false;

		// First pass: add new tracks

		foreach ( var trackRecorder in _recorder.RecordedThisFrame )
		{
			var track = trackRecorder.Track;

			if ( track is not IPropertyTrack ) continue;
			if ( Session.TrackList.Find( track ) is not null ) continue;

			Project.GetOrAddTrack( track );
			tracksAdded = true;
		}

		// Add views for the new tracks

		if ( tracksAdded )
		{
			Session.TrackList.Update();
		}

		foreach ( var trackRecorder in _recorder.RecordedThisFrame )
		{
			var track = trackRecorder.Track;

			if ( track is not IPropertyTrack ) continue;
			if ( Session.TrackList.Find( track ) is not { } view ) continue;
			if ( view.Parent?.IsExpanded is false ) continue;

			view.SetPreviewBlocks( [], trackRecorder.Blocks );
		}

		Timeline.PanToPlayheadTime();

		_recordingLastTime = time;
	}
}
