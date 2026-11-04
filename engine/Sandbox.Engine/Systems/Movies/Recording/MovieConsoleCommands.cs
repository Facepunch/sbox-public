using System.IO;
using System.Text.Json;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Console commands to start, save, and stop <see cref="MovieRecorder"/> recording.
/// </summary>
internal static class MovieConsoleCommands
{
	private static MovieRecorder? _recorder;
	private static MovieTime _lastSaveTime;
	private static DateTime _recordingStartTime;
	private static int _recordingCount;

	/// <summary>
	/// Start or stop movie recording.
	/// </summary>
	/// <param name="bufferDurationSeconds">Optional ring buffer duration.</param>
	[ConCmd( "movie", Help = "Start or stop movie recording." )]
	internal static void StartStop( float bufferDurationSeconds = 0f )
	{
		if ( _recorder is not null )
		{
			StopRecording();
			return;
		}

		if ( Game.ActiveScene is not { } scene )
		{
			Log.Warning( "No active scene!" );
			return;
		}

		if ( scene.GetSystem<MovieRecorderSystem>()?.CanUseMovieCommand is false )
		{
			Log.Warning( "Movie recording is disabled!" );
			return;
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

		_recordingCount = 0;
		_recordingStartTime = DateTime.Now;
		_lastSaveTime = MovieTime.Zero;

		var details = "";

		if ( options.BufferDuration is { } bufferDuration )
		{
			details = $" (buffer duration: {bufferDuration})";
		}

		Log.Info( $"Movie recording started{details}.\nType \"movie\" to save and stop, \"movie_save\" to save without stopping, or \"movie_stop\" to stop without saving." );
	}

	/// <summary>
	/// Generate a default movie file name including a timestamp, game ident, map ident (if available),
	/// and clip index if the recording is split into multiple parts. Does not include a file extension
	/// or directory.
	/// </summary>
	private static string GenerateMovieName()
	{
		var fileName = $"{_recordingStartTime:yyyy.MM.dd.HH.mm.ss}";

		if ( Package.TryParseIdent( Networking.MapName, out var parsedMap ) )
		{
			fileName = $"{parsedMap.package}.{fileName}";
		}

		if ( Package.TryParseIdent( Game.Ident, out var parsedGame ) )
		{
			fileName = $"{parsedGame.package}.{fileName}";
		}

		if ( _recordingCount > 0 )
		{
			fileName = $"{fileName}.{_recordingCount}";
		}

		return fileName;
	}

	/// <summary>
	/// Saves the current recording without stopping it.
	/// </summary>
	[ConCmd( "movie_save", Help = "Saves the current recording without stopping it." )]
	internal static void Save( string? fileName = null )
	{
		if ( _recorder is not { } recorder )
		{
			Log.Warning( "Recording hasn't started!" );
			return;
		}

		var clip = recorder.Options.BufferDuration is null
			? recorder.ToClip( (_lastSaveTime, recorder.Time) )
			: recorder.ToClip();

		var filePath = Path.Combine( "movies", $"{fileName ?? GenerateMovieName()}.movie" );
		var directory = Path.GetDirectoryName( filePath );

		if ( !string.IsNullOrEmpty( directory ) )
		{
			Directory.CreateDirectory( directory );
		}

		File.WriteAllText( filePath, JsonSerializer.Serialize( clip.ToResource(), Json.options ) );

		Log.Info( $"Movie saved to: {filePath} (Start: {recorder.Time - clip.Duration}, Duration: {clip.Duration})" );

		_lastSaveTime = recorder.Time;
		_recordingCount++;
	}


	/// <summary>
	/// Stops the current recording without saving it.
	/// </summary>
	[ConCmd( "movie_stop", Help = "Stops the current recording without saving it." )]
	internal static void Stop()
	{
		if ( _recorder is not { } recorder )
		{
			Log.Warning( "Recording hasn't started!" );
			return;
		}

		// Recorder won't save if it's been set to null here

		_recorder = null;

		recorder.Stop();

		Log.Info( "Movie recording stopped." );
	}

	internal static void StopRecording()
	{
		_recorder?.Stop();
	}
}
