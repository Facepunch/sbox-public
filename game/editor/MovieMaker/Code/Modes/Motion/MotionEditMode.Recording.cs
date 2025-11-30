using System.Collections.Immutable;
using Sandbox.MovieMaker;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	private bool _stopPlayingAfterRecording;

	public override bool AllowRecording => true;

	private MovieClipRecorder? _recorder;
	private MovieTime _recordingLastTime;

	/// <summary>
	/// When recording, we don't want recorded tracks to play back. This clip has those tracks
	/// filtered out, and live-updates its duration to match the recording.
	/// </summary>
	private sealed class FilteredClip : IMovieClip
	{
		private readonly ImmutableArray<ITrack> _tracks;
		private readonly MovieClipRecorder _recorder;
		private readonly ImmutableDictionary<Guid, IReferenceTrack> _referenceTracks;

		public FilteredClip( IEnumerable<ITrack> tracks, MovieClipRecorder recorder )
		{
			_tracks = [..tracks];
			_recorder = recorder;
			_referenceTracks = _tracks.OfType<IReferenceTrack>()
				.ToImmutableDictionary( x => x.Id, x => x );
		}

		public IEnumerable<ITrack> Tracks => _tracks;

		// Add a second to the end so playback doesn't stop while we're recording.

		public MovieTime Duration => _recorder.TimeRange.End + 1d;

		public IReferenceTrack? GetTrack( Guid trackId ) => _referenceTracks.GetValueOrDefault( trackId );
	}

	protected override bool OnStartRecording()
	{
		ClearChanges();
		TimeSelection = null;

		var samplePeriod = MovieTime.FromFrames( 1, Project.SampleRate );
		var startTime = Session.PlayheadTime.Floor( samplePeriod );

		Session.PlayheadTime = startTime;

		var options = new RecorderOptions( Project.SampleRate );

		_recorder = new MovieClipRecorder( Session.Binder, options, startTime );
		_stopPlayingAfterRecording = !Session.IsPlaying;
		_recordingLastTime = startTime;

		foreach ( var view in Session.TrackList.EditablePropertyTracks )
		{
			_recorder.Tracks.Add( (IProjectPropertyTrack)view.Track );
		}

		var playbackIgnoreTracks = Session.TrackList.AllTracks
			.Where( x => !x.IsLocked )
			.Select( x => x.Track );

		Session.Player.Clip = new FilteredClip( ((IMovieClip)Session.Project).Tracks.Except( playbackIgnoreTracks ), _recorder );
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

		var compiled = recorder.ToClip();
		var sourceClip = new ProjectSourceClip( Guid.NewGuid(), compiled, new JsonObject
		{
			{ "Date", DateTime.UtcNow.ToString( "o", CultureInfo.InvariantCulture ) },
			{ "IsEditor", Session.Player.Scene.IsEditor },
			{ "SceneSource", Json.ToNode( Session.Player.Scene.Source ) },
			{ "MoviePlayer", Json.ToNode( Session.Player.Id ) }
		} );

		foreach ( var trackRecorder in recorder.Tracks )
		{
			if ( Session.TrackList.Find( (IProjectTrack)trackRecorder.Track ) is not { } view )
			{
				continue;
			}

			view.ClearPreviewBlocks();

			if ( view.Track is not IProjectPropertyTrack propertyTrack )
			{
				continue;
			}

			propertyTrack.AddRange( propertyTrack.CreateSourceBlocks( sourceClip ).Select( x => x.Shift( timeRange.Start ) ) );

			view.MarkValueChanged();
		}

		Session.PlayheadTime = timeRange.End;
		TimeSelection = new TimeSelection( timeRange, DefaultInterpolation );
		
		DisplayAction( "radio_button_checked" );
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
				if ( !view.IsExpanded ) continue;

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

		Session.ScrollToPlayheadTime();

		_recordingLastTime = time;
	}
}
