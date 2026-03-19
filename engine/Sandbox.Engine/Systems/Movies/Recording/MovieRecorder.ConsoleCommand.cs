using System.IO;

namespace Sandbox.MovieMaker;

#nullable enable

partial class MovieRecorder
{
	private static string? _fileName;
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

		_fileName = ScreenCaptureUtility.GenerateScreenshotFilename( "movie", filePath: "movies" );
		_recorder = new MovieRecorder( scene, MovieRecorderOptions.Default );
		_recorder.Start();

		Log.Info( $"Movie recording started: {_fileName}" );
		return true;
	}

	internal static void StopRecording()
	{
		if ( _recorder is not { } recorder ) return;

		_recorder = null;

		recorder.Stop();

		if ( _fileName is not { } fileName ) return;

		var clip = recorder.ToClip();

		FileSystem.Data.WriteJson( fileName, clip.ToResource() );

		Log.Info( $"Saved {fileName} (Duration: {clip.Duration})" );
	}
}
