using System.IO;

namespace Sandbox.MovieMaker;

#nullable enable

partial class MovieRecorder
{
	private static MovieRecorder? _recorder;

	private static bool IsEjectEffect( GameObject go ) => go.Name.StartsWith( "eject_" );
	private static bool IsImpactEffect( GameObject go ) => go.IsPrefabInstanceRoot && go.PrefabInstanceSource.StartsWith( "prefabs/surface/" );

	[ConCmd( "movie" )]
	internal static bool StartRecording()
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

		var fileName = ScreenCaptureUtility.GenerateScreenshotFilename( "movie", filePath: "movies" );

		_recorder = new MovieRecorder( scene, MovieRecorderOptions.Default );
		_recorder.Stopped += recorder =>
		{
			_recorder = null;
			SaveRecording( recorder, fileName );
		};

		_recorder.Start();

		Log.Info( $"Movie recording started: {fileName}" );

		return true;
	}

	private static void SaveRecording( MovieRecorder recorder, string fileName )
	{
		var clip = recorder.ToClip();

		FileSystem.Data.WriteJson( fileName, clip.ToResource() );

		Log.Info( $"Saved {fileName} (Duration: {clip.Duration})" );
	}

	internal static void StopRecording()
	{
		_recorder?.Stop();
	}
}
