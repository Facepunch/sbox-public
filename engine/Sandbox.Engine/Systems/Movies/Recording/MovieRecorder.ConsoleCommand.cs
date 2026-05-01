namespace Sandbox.MovieMaker;

#nullable enable

partial class MovieRecorder
{
	private static MovieRecorder? _recorder;
	private static MovieTime _lastSaveTime;

	/// <summary>
	/// Start or stop movie recording.
	/// </summary>
	/// <param name="bufferDurationSeconds">Optional ring buffer duration.</param>
	[ConCmd( "movie" )]
	internal static bool ToggleRecording( float bufferDurationSeconds = 0f )
	{
		if ( _recorder is not null )
		{
			StopRecording();
			return false;
		}

		if ( Game.ActiveScene is not { } scene )
		{
			Log.Warning( "No active scene!" );
			return false;
		}

		if ( scene.GetSystem<MovieRecorderSystem>()?.CanUseMovieCommand is false )
		{
			Log.Warning( "Movie recording is disabled!" );
			return false;
		}

		var options = MovieRecorderOptions.Default;

		if ( bufferDurationSeconds > 0f )
		{
			options = options with { BufferDuration = bufferDurationSeconds };
		}

		_recorder = new MovieRecorder( scene, options );
		_recorder.Stopped += recorder =>
		{
			if ( _recorder != recorder ) return;

			try
			{
				Save();
			}
			finally
			{
				_recorder = null;
			}
		};

		_recorder.Start();
		_lastSaveTime = MovieTime.Zero;

		var details = "";

		if ( options.BufferDuration is { } bufferDuration )
		{
			details = $" (buffer duration: {bufferDuration})";
		}

		Log.Info( $"Movie recording started{details}.\nType \"movie\" to save and stop, or \"movie_save\" to save without stopping." );

		return true;
	}

	/// <summary>
	/// Saves the current recording without stopping it.
	/// </summary>
	[ConCmd( "movie_save" )]
	internal static void Save()
	{
		if ( _recorder is not { } recorder )
		{
			Log.Warning( "Recording hasn't started!" );
			return;
		}

		var clip = recorder.Options.BufferDuration is null
			? recorder.ToClip( (_lastSaveTime, recorder.Time) )
			: recorder.ToClip();

		var fileName = ScreenCaptureUtility.GenerateScreenshotFilename( "movie", filePath: "movies" );

		FileSystem.Data.WriteJson( fileName, clip.ToResource() );

		Log.Info( $"Saved {fileName} (Start: {recorder.Time - clip.Duration}, Duration: {clip.Duration})" );

		_lastSaveTime = recorder.Time;
	}

	internal static void StopRecording()
	{
		_recorder?.Stop();
	}
}
