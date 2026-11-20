
using Sandbox.MovieMaker;

namespace Sandbox;

internal static class DemoRecording
{
	[DefaultMovieRecorderOptions]
	public static MovieRecorderOptions BuildMovieRecorderOptions( MovieRecorderOptions options )
	{
		return options
			.WithFilter( x => x.PrefabInstanceSource?.StartsWith( "prefabs/surface/" ) is not true );
	}
}
