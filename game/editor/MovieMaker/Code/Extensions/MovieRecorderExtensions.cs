using System.Linq;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

public static class MovieRecorderExtensions
{
	public static MovieRecorderOptions WithCaptureTracks( this MovieRecorderOptions options, IEnumerable<IProjectTrack> tracks )
	{
		return options with
		{
			CaptureActions =
			[
				..options.CaptureActions,
				..tracks.OfType<IProjectPropertyTrack>().Select( GetCaptureAction )
			]
		};
	}

	public static MovieRecorderOptions WithCaptureTrack( this MovieRecorderOptions options, IProjectTrack track )
	{
		return track is IProjectPropertyTrack propertyTrack
			? options.WithCaptureAction( GetCaptureAction( propertyTrack ) )
			: options;
	}

	private static MovieRecorderAction GetCaptureAction( IProjectPropertyTrack track )
	{
		// Cache the track recorder so we don't need to find it every frame

		var firstTime = true;
		IMovieTrackRecorder? trackRecorder = null;

		return recorder =>
		{
			if ( firstTime )
			{
				firstTime = false;
				trackRecorder = recorder.GetTrackRecorder( track );
			}

			trackRecorder?.Capture();
		};
	}
}
