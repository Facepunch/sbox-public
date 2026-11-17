using Sandbox.Rendering.Bridge;

namespace SceneTests.Components;

/// <summary>
/// Borealis's envmap scheduling (<see cref="EnvmapScheduler"/>): which dirty probe a camera needs captured next.
/// </summary>
[TestClass]
public class EnvmapSchedulerTests
{
	static EnvmapProbe Probe( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		var probe = go.Components.Create<EnvmapProbe>();
		probe.Mode = EnvmapProbe.EnvmapProbeMode.Baked;
		probe.Bounds = BBox.FromPositionAndSize( 0, 200 );
		probe.Dirty = true;
		return probe;
	}

	/// <summary>
	/// A camera at the origin looking down +X needs the probes in front of it, the largest on screen first, and never the
	/// one behind it however long it's dirty; a probe's first capture goes before a refresh of one a little larger on
	/// screen. Before, every dirty probe captured in turn, seen or not.
	/// </summary>
	[TestMethod]
	public void PicksProbesTheCameraSees()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.FieldOfView = 90;
		camera.CustomSize = new Vector2( 800, 600 );

		var near = Probe( scene, new Vector3( 500, 0, 0 ) );
		var middle = Probe( scene, new Vector3( 1100, 0, 0 ) );
		var far = Probe( scene, new Vector3( 5000, 0, 0 ) );
		var behind = Probe( scene, new Vector3( -2000, 0, 0 ) );
		var scheduler = new EnvmapScheduler( null );

		Assert.AreSame( near, scheduler.Pick( scene, camera ), "the largest on screen first" );
		near.Dirty = false;
		Assert.AreSame( middle, scheduler.Pick( scene, camera ) );
		middle.Dirty = false;
		Assert.AreSame( far, scheduler.Pick( scene, camera ) );
		far.Dirty = false;
		Assert.IsNull( scheduler.Pick( scene, camera ), "not the one behind the camera" );

		// The near one captured before: its refresh waits behind the middle one's first capture
		near.Dirty = true;
		middle.Dirty = true;
		scheduler.MarkCaptured( near );
		Assert.AreSame( middle, scheduler.Pick( scene, camera ), "a first capture before a refresh" );
		Assert.IsTrue( behind.Dirty, "the unseen probe is left dirty" );
	}
}
