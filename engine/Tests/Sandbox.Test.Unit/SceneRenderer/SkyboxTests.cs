using Sandbox.Rendering;
using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using Sandbox.SceneRenderer.Gpu;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>
/// The 3D skybox: its world seen through the main view moved into its space, as native's camera renderer and 3D skybox
/// renderer set it up.
/// </summary>
// Frames go through the engine's ShadowMapper, whose cache and frame counter are static
[TestClass]
[DoNotParallelize]
public class SkyboxTests
{
	static readonly uint SkyboxTag = new StringToken( "skybox" ).Value;

	/// <summary>
	/// Native's <c>CCameraRenderer::CreateView</c>: the skybox's origin plus the camera's position over the scale, with the
	/// camera's own angles, field of view and planes. The template view has no size culling.
	/// </summary>
	[TestMethod]
	public void ViewIsTheCameraInSkyboxSpace()
	{
		var main = new RenderView
		{
			Position = new Vector3( 1600, -320, 160 ),
			Rotation = Rotation.From( 10, 45, 0 ),
			FieldOfView = 70,
			ZNear = 5,
			ZFar = 30000,
			Viewport = new Rect( 0, 0, 1280, 720 ),
			SizeCullThreshold = 0.0025f,
		};

		var view = new RenderView();
		RenderSystem.FillSkyboxView( view, main, new Skybox3D( new RenderWorld() ) { Origin = new Vector3( 100, 200, -50 ), Scale = 16 } );

		Assert.AreEqual( new Vector3( 200, 180, -40 ), view.Position );
		Assert.AreEqual( main.Rotation, view.Rotation );
		Assert.AreEqual( 70, view.FieldOfView );
		Assert.AreEqual( 5, view.ZNear, "the planes aren't scaled" );
		Assert.AreEqual( 30000, view.ZFar );
		Assert.AreEqual( main.Viewport, view.Viewport );
		Assert.AreEqual( 0, view.SizeCullThreshold );
	}

	/// <summary>
	/// The skybox world is culled through the skybox view - what's in front of the skybox camera, not the main one - and
	/// isn't size culled: a far, small object the main view would drop still draws.
	/// </summary>
	[TestMethod]
	public void WorldIsCollectedThroughTheSkyboxView()
	{
		var mesh = TestMesh( false );
		var sky = new RenderWorld();
		sky.Add( new MeshObject( mesh, new Transform( new Vector3( 5010, 0, 0 ) ) ) );
		sky.Add( new MeshObject( mesh, new Transform( new Vector3( 4990, 0, 0 ) ) ) );
		sky.Add( new MeshObject( mesh, new Transform( new Vector3( 5900, 0, 0 ) ) ) );

		var world = new RenderWorld { Skybox3D = new Skybox3D( sky ) { Origin = new Vector3( 5000, 0, 0 ), Scale = 16 } };
		var view = LookingDownX();

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, view );

		Assert.IsNotNull( system.SkyboxStats );
		var skybox = system.SkyboxSystem;
		var visible = skybox.MainPass.Visible( skybox.GetFeature<MeshRenderFeature>() );
		CollectionAssert.AreEquivalent( new[] { 0, 2 }, visible.ToArray(), "in front of the skybox camera, the far one included; not the one behind it" );

		// The same far box in the main world is size culled
		var near = new RenderWorld();
		near.Add( new MeshObject( mesh, new Transform( new Vector3( 900, 0, 0 ) ) ) );
		using var main = new RenderSystem();
		CollectAndPrepare( main, near, view );
		Assert.AreEqual( 0, main.MainPass.Visible( main.GetFeature<MeshRenderFeature>() ).Count, "the main view size culls it" );
	}

	/// <summary>
	/// The skybox's frame records with the main one: its layers are planned in the 3D skybox layer's place, after the main
	/// frame's opaque layer and before its translucency, and the skybox layer records nothing itself.
	/// </summary>
	[TestMethod]
	public void SkyboxLayersRecordInItsLayersPlace()
	{
		var sky = new RenderWorld();
		sky.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 10, 0, 0 ) ) ) );
		var world = new RenderWorld { Skybox3D = new Skybox3D( sky ) };
		world.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 20, 0, 0 ) ) ) );

		using var system = new RenderSystem();
		CollectAndPrepare( system, world, LookingDownX() );
		var main = system.Frame;
		var skybox = main.Skybox;
		Assert.AreSame( system.SkyboxSystem.Frame, skybox );

		using var recorder = new FrameRecorder();
		recorder.Plan( main, parallel: false );
		var planned = recorder.Planned;
		int IndexOf( RenderFrame frame, System.Type type ) => System.Array.FindIndex( planned, p => p.Frame == frame && p.Layer.GetType() == type );

		var mainOpaque = IndexOf( main, typeof( OpaqueLayer ) );
		var skyOpaque = IndexOf( skybox, typeof( OpaqueLayer ) );
		var skyTranslucent = IndexOf( skybox, typeof( TranslucentLayer ) );
		var mainTranslucent = IndexOf( main, typeof( TranslucentLayer ) );
		Assert.IsTrue( mainOpaque >= 0 && skyOpaque > mainOpaque, "the skybox after the main frame's opaque" );
		Assert.IsTrue( skyTranslucent > skyOpaque && mainTranslucent > skyTranslucent, "and before its translucency" );
		Assert.IsFalse( planned.Any( p => p.Layer is Skybox3DLayer ), "the skybox layer only puts the skybox's in its place" );
	}

	/// <summary>
	/// Native's gate on the <c>skybox</c> tag: a view with render tags needs it, and one that excludes it draws no
	/// skybox. Nor does one with the skybox off, or a scale of zero.
	/// </summary>
	[TestMethod]
	public void SkyboxFollowsTheViewsTags()
	{
		var sky = new RenderWorld();
		sky.Add( new MeshObject( TestMesh( false ), new Transform( new Vector3( 10, 0, 0 ) ) ) );
		var world = new RenderWorld { Skybox3D = new Skybox3D( sky ) };
		var view = LookingDownX();
		using var system = new RenderSystem();

		bool Draws()
		{
			CollectAndPrepare( system, world, view );
			return system.SkyboxStats is not null;
		}

		Assert.IsTrue( Draws(), "no tags" );

		view.RenderTags = [new StringToken( "prop" ).Value];
		Assert.IsFalse( Draws(), "render tags without skybox" );

		view.RenderTags = [SkyboxTag];
		Assert.IsTrue( Draws(), "render tags with skybox" );

		view.RenderTags = [];
		view.ExcludeTags = [SkyboxTag];
		Assert.IsFalse( Draws(), "skybox excluded" );

		view.ExcludeTags = [];
		system.Draw3DSkybox = false;
		Assert.IsFalse( Draws(), "switched off" );

		system.Draw3DSkybox = true;
		world.Skybox3D.Scale = 0;
		Assert.IsFalse( Draws(), "no scale" );
	}

	/// <summary>
	/// The main world's gradient fog in the skybox's space (<c>Add3DSkyboxLayers</c>): distances and heights over the
	/// scale, then heights moved up by the skybox's origin.
	/// </summary>
	[TestMethod]
	public void GradientFogIsInSkyboxSpace()
	{
		var vr = new ViewEnvironment.ViewConstantsVRData();
		ViewEnvironment.SetupGradientFog( ref vr, new GradientFogSetup
		{
			Enabled = true,
			StartDistance = 160,
			EndDistance = 1760,
			StartHeight = 0,
			EndHeight = 320,
			MaximumOpacity = 1,
			Color = Color.White,
		}, scale: 16, offsetZ: 100 );

		// Start 10, end 110, heights 100 to 120
		Assert.AreEqual( new Vector4( -0.1f, 6, 0.01f, -0.05f ), vr.GradientFogBiasAndScale );
		Assert.AreEqual( new Vector4( 100, 120, 0, 0 ), vr.GradientFogCullingParams );
	}
}
