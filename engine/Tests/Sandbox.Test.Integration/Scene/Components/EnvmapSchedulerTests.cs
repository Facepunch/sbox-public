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

	/// <summary>
	/// Just after a loading screen goes, captures earn more time a frame: the load left every probe's bounces to capture, and
	/// the view settled seconds after native's while they trickled in at the usual budget (Construct: 2.4-4.3 s on bench-low).
	/// </summary>
	[TestMethod]
	public void CapturesEarnMoreJustAfterALoad()
	{
		var screenBefore = LoadingScreen.IsVisible;
		try
		{
			LoadingScreen.IsVisible = true;
			LoadingScreen.IsVisible = false;
			Assert.AreEqual( EnvmapScheduler.AfterLoadBudgetMs, EnvmapScheduler.FrameBudget( inDrawing: true ) );
			Assert.AreEqual( EnvmapScheduler.AfterLoadBudgetMs, EnvmapScheduler.FrameBudget( inDrawing: false ) );
			Assert.IsTrue( EnvmapScheduler.AfterLoadBudgetMs > EnvmapScheduler.DrawingBudgetMs );
		}
		finally
		{
			LoadingScreen.IsVisible = screenBefore;
		}
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
		new EnvmapScheduler( null ).Run( scene );
	}

	/// <summary>
	/// A world's renderer that counts what its cubemaps ask for: each time one asks for its cube, its settings were written.
	/// </summary>
	sealed class CubeRecorder : Sandbox.Rendering.ISceneWorldRender
	{
		public int CubesAsked;
		public Borealis.RenderWorld World { get; } = new();
		public bool OnDevice => false;
		public Borealis.GpuTexture Texture( Texture texture ) => null;
		public Borealis.GpuTexture TextureWhenRead( Texture texture, SceneObject waiting, Sandbox.Rendering.SceneObjectChange change ) => null;
		public Borealis.GpuTexture ProbeCube( SceneCubemap cubemap )
		{
			CubesAsked++;
			return null;
		}

		public void WriteMesh( SceneObject sceneObject, Borealis.MeshObject mesh, Sandbox.Rendering.SceneObjectChange change ) { }
		public Borealis.AggregateObject MakeAggregate( SceneObject sceneObject, out bool waiting )
		{
			waiting = false;
			return null;
		}

		public void WriteAggregate( SceneObject sceneObject, Borealis.AggregateObject aggregate, Sandbox.Rendering.SceneObjectChange change ) { }
		public void WriteLighting( SceneObject sceneObject, Sandbox.Rendering.SceneObjectChange change ) { }
		public void Made( SceneObject sceneObject ) { }
		public void Deleted( SceneObject sceneObject ) { }
		public void Destroyed( SceneObject sceneObject ) { }
		public void Later( SceneObject sceneObject, Sandbox.Rendering.SceneObjectChange change ) { }
	}

	/// <summary>
	/// A probe's scene object writes its render object when its texture or settings change, as when a realtime probe makes its
	/// cube anew once a native tool draws. Before, only its creation did: the renderer kept the old cube, freed with the old
	/// texture, and the probe reflected black.
	/// </summary>
	[TestMethod]
	public void CubemapChangesReachTheRenderer()
	{
		typeof( ThreadSafe ).GetMethod( "MarkMainThread", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static ).Invoke( null, null );

		var world = new SceneWorld();
		var recorder = new CubeRecorder();
		world.Render = recorder;
		var cubemap = new SceneCubemap( world, null, BBox.FromPositionAndSize( 0, 200 ) );
		cubemap.RenderObject = cubemap.MakeRenderObject( recorder );
		recorder.World.Add( cubemap.RenderObject );

		cubemap.TintColor = Color.Red;
		Assert.AreEqual( Color.Red, ((Borealis.EnvMapObject)cubemap.RenderObject).Tint, "a setting" );

		var asked = recorder.CubesAsked;
		using var texture = Texture.CreateCube( 16, 16 ).WithUAVBinding().WithMips( 1 ).WithFormat( ImageFormat.RGBA16161616F ).Finish();
		cubemap.Texture = texture;
		Assert.IsTrue( recorder.CubesAsked > asked, "its texture" );

		world.Render = null;
		cubemap.Delete();
		world.Delete();
	}
}
