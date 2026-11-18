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
	/// The scene's camera without a scene camera (not made yet, or already disposed) has nothing to see from: the scheduler
	/// captures in native's order rather than asking it for a frustum. Before, it threw every frame (Mouse Fishing).
	/// </summary>
	[TestMethod]
	public void CameraWithoutASceneCameraCapturesInNativeOrder()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		Assert.AreSame( camera, scene.Camera );

		// As a camera between being destroyed and leaving the scene's index has it, or one not awake yet
		var field = typeof( CameraComponent ).GetField( "sceneCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
		camera.SceneCamera.Dispose();
		field.SetValue( camera, null );

		Probe( scene, new Vector3( 500, 0, 0 ) );
		new EnvmapScheduler().Run( scene );
	}
}
