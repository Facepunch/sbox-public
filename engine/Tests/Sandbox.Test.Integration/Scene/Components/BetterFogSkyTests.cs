namespace SceneTests.Components;

/// <summary>
/// Checks fog against actual scene-world skies, including map-style skies without SkyBox2D components.
/// </summary>
[TestClass]
public class BetterFogSkyTests
{
	int previousSkyboxSetting;

	/// <summary>
	/// Enables child skybox rendering while preserving the process-wide rendering preference.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previousSkyboxSetting = ConVarSystem.GetInt( "r_3d_skybox", 1, true );
		ConVarSystem.SetInt( "r_3d_skybox", 1, true );
	}

	/// <summary>
	/// Restores the rendering preference even when a test assertion fails.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		ConVarSystem.SetInt( "r_3d_skybox", previousSkyboxSetting, true );
	}

	/// <summary>
	/// A procedural cube keeps its dimensional metadata with both graphics and headless backends.
	/// </summary>
	static SceneSkyBox CreateSky( SceneWorld world, Color tint )
	{
		var cube = Texture.CreateCube( 1, 1 ).WithData( new byte[24] ).Finish();
		Assert.IsTrue( cube.Desc.IsCube );
		var material = Material.Load( "materials/skybox/skybox_day_01.vmat" ).CreateCopy();
		material.Set( "g_tSkyTexture", cube );
		var sky = new SceneSkyBox( world, material ) { SkyTint = tint };
		sky.Tags.Add( "skybox" );
		return sky;
	}

	/// <summary>
	/// Bare parent-world skies use native creation order, visibility and tag filtering, including deletion fallback.
	/// </summary>
	[TestMethod]
	public void ParentSkySelectionTracksNewestVisibleSurvivor()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.SceneSky;
			fog.Color = Color.White;
			var first = CreateSky( scene.SceneWorld, Color.Red );
			var last = CreateSky( scene.SceneWorld, Color.Blue );
			last.Tags.Add( "last-sky" );
			last.Transform = Transform.Zero.WithRotation( Rotation.FromYaw( 60 ) );

			var settings = fog.GetSettings( camera );
			Assert.AreEqual( Color.Blue, settings.Color );
			Assert.AreEqual( last.Material.GetTexture( "g_tSkyTexture" ), settings.SkyTexture );
			Assert.AreEqual( last.Transform.Rotation.Conjugate, settings.SkyRotation );
			last.SkyMaterial = first.Material.CreateCopy();
			Assert.AreEqual( first.Material.GetTexture( "g_tSkyTexture" ), fog.GetSettings( camera ).SkyTexture,
				"Changing a bare sky's material must update the fog source immediately" );
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color, "Material changes retain the selected sky's tint" );

			camera.RenderExcludeTags.Add( "last-sky" );
			Assert.AreSame( first, scene.SceneWorld.GetSky( camera.RenderTags, camera.RenderExcludeTags ) );
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
			camera.RenderExcludeTags = new();
			last.RenderingEnabled = false;
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
			first.RenderingEnabled = false;
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );

			last.RenderingEnabled = true;
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color );
			last.Delete();
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			first.RenderingEnabled = true;
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
			// Native-created tools/map skies bypass the managed constructor but still carry a material.
			var replacement = NativeEngine.CSceneSystem.CreateSkyBox( first.Material.native, scene.SceneWorld );
			replacement.SkyTint = Color.Green;
			replacement.Tags.Add( "skybox" );
			first.Delete();
			Assert.AreSame( replacement, scene.SceneWorld.GetSky() );
			Assert.AreEqual( Color.Green, fog.GetSettings( camera ).Color );
			Assert.IsNotNull( fog.GetSettings( camera ).SkyTexture );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// An active child replaces the parent even without a sky and uses child-view filtering plus the parent sky-layer gate.
	/// </summary>
	[TestMethod]
	public void ChildSkyUsesRenderedWorldAndSkyLayerGate()
	{
		var scene = new Scene();
		var childWorld = new SceneWorld();
		SceneSkybox3D child = null;
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.SceneSky;
			fog.Color = Color.White;
			CreateSky( scene.SceneWorld, Color.Red );
			child = new SceneSkybox3D( scene.SceneWorld, childWorld );
			child.Update();
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture, "An empty child world suppresses the parent's sky" );

			var sky = CreateSky( childWorld, Color.Blue );
			sky.Tags.Add( "child-private" );
			camera.RenderExcludeTags.Add( "child-private" );
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color, "Child sky views do not inherit parent object-tag filters" );
			camera.RenderExcludeTags.Add( "skybox" );
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			camera.RenderExcludeTags = new();
			camera.RenderTags.Add( "world" );
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			camera.RenderTags.Add( "skybox" );
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color );

			ConVarSystem.SetInt( "r_3d_skybox", 0, true );
			Assert.IsNull( scene.SceneWorld.GetRenderingSkybox() );
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
			ConVarSystem.SetInt( "r_3d_skybox", 1, true );
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color );
			sky.RenderingEnabled = false;
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			sky.Delete();
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
		}
		finally
		{
			child?.Delete();
			childWorld.Delete();
			scene.Destroy();
		}
	}

	/// <summary>
	/// Native keeps the last added child world and clears it when any registered skybox is removed.
	/// </summary>
	[TestMethod]
	public void ChildSkyActivationMatchesNativeAddAndRemove()
	{
		var scene = new Scene();
		var firstWorld = new SceneWorld();
		var lastWorld = new SceneWorld();
		SceneSkybox3D first = null;
		SceneSkybox3D last = null;
		SceneSkybox3D replacement = null;
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.SceneSky;
			fog.Color = Color.White;
			CreateSky( scene.SceneWorld, Color.Red );
			CreateSky( firstWorld, Color.Green );
			CreateSky( lastWorld, Color.Blue );
			first = new SceneSkybox3D( scene.SceneWorld, firstWorld );
			first.Update();
			Assert.AreEqual( Color.Green, fog.GetSettings( camera ).Color );
			last = new SceneSkybox3D( scene.SceneWorld, lastWorld );
			last.Update();
			Assert.AreSame( last, scene.SceneWorld.GetRenderingSkybox() );
			Assert.AreEqual( Color.Blue, fog.GetSettings( camera ).Color );

			first.Delete();
			first = null;
			Assert.IsNull( scene.SceneWorld.GetRenderingSkybox(), "Removing the older link clears native's active skybox too" );
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
			replacement = new SceneSkybox3D( scene.SceneWorld, firstWorld );
			replacement.Update();
			Assert.AreEqual( Color.Green, fog.GetSettings( camera ).Color );
			replacement.Delete();
			replacement = null;
			Assert.IsNull( scene.SceneWorld.GetRenderingSkybox(), "Surviving links must not reactivate automatically" );
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );
		}
		finally
		{
			replacement?.Delete();
			last?.Delete();
			first?.Delete();
			lastWorld.Delete();
			firstWorld.Delete();
			scene.Destroy();
		}
	}
}
