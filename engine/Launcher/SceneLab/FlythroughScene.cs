namespace Sandbox.SceneLab;

/// <summary>
/// A scripted camera through Construct, as a player and then a car would move: walking out from the spawn over the field,
/// a fast turn round, running back, north towards the pond and west along the field; then in a car across the field at
/// speed, a handbrake turn, and back past the pond to the spawn. What's visible, which LODs, the occlusion history and the
/// shadows' time slicing all churn as it goes. The path's points are on the ground (traced once the map has loaded) at eye
/// height; it loops, and the benchmark measures the whole of it.
/// </summary>
internal static class FlythroughScene
{
	const float Walk = 130;
	const float Run = 320;
	const float Drive = 1100;
	const float Eye = 64;
	const float CarEye = 72;

	static CameraPath path;
	static Scene ready;
	static int frame;

	/// <summary>
	/// The path's frames: a walk, a run, a drive, two quick turns and the waits between.
	/// </summary>
	public static int Frames => Plan( _ => null ).Frames;

	public static Scene Build()
	{
		var scene = MapScenes.ConstructSkyline();
		ready = null;
		frame = 0;
		return scene;
	}

	/// <summary>
	/// The path, its heights from <paramref name="ground"/>.
	/// </summary>
	static CameraPath Plan( Func<Vector2, float?> ground )
	{
		// Where nothing's under a point, the last point's height
		var last = 0f;
		Vector3 On( float x, float y, float eye = Eye )
		{
			last = ground( new Vector2( x, y ) ) ?? last;
			return new Vector3( x, y, last + eye );
		}

		return new CameraPath( On( 864, -608 ), 180, -2 )
			// Out over the field at a walk, then a fast turn round
			.MoveTo( On( -500, -300 ), Walk )
			.Turn( 180, 0.4f )
			// Running back, north up the field's edge towards the pond, and west along the field
			.MoveTo( On( 600, -650 ), Run )
			.MoveTo( On( 600, 1500 ), Run )
			.MoveTo( On( -2500, 1500 ), Run )
			// In a car: across the field and up the hill at speed, a handbrake turn, and back past the pond to the spawn
			.MoveTo( On( -4700, -600, CarEye ), Drive, 0.4f )
			.Turn( 180, 0.5f )
			.MoveTo( On( -1000, 2000, CarEye ), Drive, 0.3f )
			.MoveTo( On( 500, -500, CarEye ), Drive, 0.3f )
			.Hold( 0.5f );
	}

	/// <summary>
	/// The map has loaded and the path is on its ground.
	/// </summary>
	public static bool Ready( Scene scene ) => MapScenes.Loaded( scene ) && ready == scene;

	public static void Rewind( Scene scene )
	{
		frame = 0;
		Fly( scene );
	}

	/// <summary>
	/// A frame along the path, once the map is in.
	/// </summary>
	public static void Fly( Scene scene )
	{
		MapScenes.StartWhenOnline( scene );
		if ( !MapScenes.Loaded( scene ) ) return;

		if ( ready != scene )
		{
			path = Plan( at => Ground( scene, at ) );
			ready = scene;
			frame = 0;
		}

		if ( scene.Camera is { } camera ) path.Apply( camera, frame++ );
	}

	/// <summary>
	/// The height of whatever's under a point, or null where there's nothing to stand on. The path keeps out from under roofs.
	/// </summary>
	static float? Ground( Scene scene, Vector2 at )
	{
		var trace = scene.Trace.Ray( new Vector3( at, 3000 ), new Vector3( at, -3000 ) ).Run();
		return trace.Hit ? trace.HitPosition.z : null;
	}
}
