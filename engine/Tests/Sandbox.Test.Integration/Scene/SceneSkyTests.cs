using System;

namespace SceneTests;

/// <summary>
/// Checks sky state shared by scene-world consumers and the managed renderer.
/// </summary>
[TestClass]
public class SceneSkyTests
{
	/// <summary>
	/// A sky's settings are kept in managed code: one native code made starts with its material and native's defaults, and
	/// changes stay managed (nothing native draws it).
	/// </summary>
	[TestMethod]
	public void SettingsAreManaged()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = NativeEngine.CSceneSystem.CreateSkyBox( material.native, world );
			var nativeSky = (NativeEngine.CSceneSkyBoxObject)sky.native;
			Assert.AreSame( material, sky.Material, "a sky native code made starts with its material" );
			Assert.AreEqual( SceneSkyBox.FogType.Distance, sky.FogParams.FogType, "and native's defaults" );
			Assert.AreEqual( -25.0f, sky.FogParams.FogMinStart );

			var replacement = material.CreateCopy();
			var fog = new SceneSkyBox.FogParamInfo { FogType = SceneSkyBox.FogType.Angular, FogMinStart = 1, FogMinEnd = 2, FogMaxStart = 3, FogMaxEnd = 4 };

			sky.SkyMaterial = replacement;
			sky.SkyTint = Color.Red;
			sky.FogParams = fog;
			Assert.AreSame( replacement, sky.Material );
			Assert.AreEqual( Color.Red, sky.SkyTint );
			Assert.AreEqual( fog, sky.FogParams );
			Assert.AreSame( material, Material.FromNative( nativeSky.GetMaterial() ), "native's sky isn't written" );

			sky.SkyMaterial = material;
			sky.SkyTint = Color.Blue;
			sky.FogParams = default;
			Assert.AreSame( material, sky.Material );
			Assert.AreEqual( Color.Blue, sky.SkyTint );
			Assert.AreEqual( default, sky.FogParams );

			sky.SkyMaterial = null;
			Assert.IsNull( sky.Material );
			sky.SkyMaterial = material;
			sky.Delete();
			Assert.IsNull( sky.Material );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Selection is per camera and follows creation order across visibility changes and deletions.
	/// </summary>
	[TestMethod]
	public void SelectionTracksCameraFiltersAndSkyLifetime()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var first = new SceneSkyBox( world, material );
			var middle = new SceneSkyBox( world, material );
			var last = NativeEngine.CSceneSystem.CreateSkyBox( material.native, world );
			first.Tags.Add( "first" );
			last.Tags.Add( "last" );
			using var camera = new SceneCamera();

			Assert.AreSame( last, world.GetSky( camera.RenderTags, camera.ExcludeTags ) );
			camera.ExcludeTags.Add( "last" );
			Assert.AreSame( middle, world.GetSky( camera.RenderTags, camera.ExcludeTags ) );
			camera.RenderTags.Add( "first" );
			Assert.AreSame( first, world.GetSky( camera.RenderTags, camera.ExcludeTags ) );
			Assert.AreSame( last, world.GetSky(), "Child views ignore the parent's object-tag filters" );

			camera.ExcludeTags.Add( "first" );
			Assert.IsNull( world.GetSky( camera.RenderTags, camera.ExcludeTags ) );
			camera.RenderTags.RemoveAll();
			camera.ExcludeTags.RemoveAll();
			last.RenderingEnabled = false;
			Assert.AreSame( middle, world.GetSky() );
			last.RenderingEnabled = true;
			middle.Delete();
			world.DeletePendingObjects();
			SceneWorld.FlushQueuedDeletes();
			Assert.AreSame( last, world.GetSky(), "Removing an older sky must preserve creation order" );

			last.Delete();
			Assert.AreSame( first, world.GetSky(), "Pending deletion must stop selection immediately" );
			world.DeletePendingObjects();
			SceneWorld.FlushQueuedDeletes();
			Assert.AreEqual( 1, world.Skies.Count );
			first.Delete();
			world.DeletePendingObjects();
			SceneWorld.FlushQueuedDeletes();
			Assert.IsNull( world.GetSky() );
			Assert.AreEqual( 0, world.Skies.Count );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Camera filtering must not allocate enumerators or convert cached tokens back to strings each view.
	/// </summary>
	[TestMethod]
	public void SelectionWithCameraTagsDoesNotAllocate()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = new SceneSkyBox( world, material );
			sky.Tags.Add( "skybox" );
			using var camera = new SceneCamera();
			camera.RenderTags.Add( "skybox" );
			camera.ExcludeTags.Add( "hidden" );

			for ( int i = 0; i < 10; i++ )
				world.GetSky( camera.RenderTags, camera.ExcludeTags );

			var before = GC.GetAllocatedBytesForCurrentThread();
			for ( int i = 0; i < 10; i++ )
				world.GetSky( camera.RenderTags, camera.ExcludeTags );
			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

			Assert.AreEqual( 0L, allocated );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// The renderer refreshes sky selection for each camera.
	/// </summary>
	[TestMethod]
	public void WorldRefreshesSkyForEachView()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var first = new SceneSkyBox( world, material ) { SkyTint = Color.Red };
			var last = new SceneSkyBox( world, material ) { SkyTint = Color.Blue };
			last.Tags.Add( "last" );
			using var camera = new SceneCamera { World = world };
			var renderType = System.Type.GetType( "Sandbox.Rendering.Bridge.WorldRender, Sandbox.Rendering", true );
			using var render = (IDisposable)Activator.CreateInstance( renderType, [world] );
			var renderWorld = (Borealis.RenderWorld)renderType.GetProperty( "World" ).GetValue( render );
			var prepare = renderType.GetMethod( "Prepare" );

			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SkyTint );
			camera.ExcludeTags.Add( "last" );
			prepare.Invoke( render, [camera, false] );
			Assert.AreEqual( Color.Red, renderWorld.Lighting.SkyTint );
			prepare.Invoke( render, [camera, true] );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SkyTint );

			camera.ExcludeTags.RemoveAll();
			last.SkyMaterial = null;
			prepare.Invoke( render, [camera, false] );
			Assert.IsNull( renderWorld.Lighting.SkyMaterial, "A null material on the newest sky must not reveal an older sky" );
			first.RenderingEnabled = false;
			last.RenderingEnabled = false;
			prepare.Invoke( render, [camera, false] );
			Assert.IsNull( renderWorld.Lighting.SkyMaterial );
		}
		finally
		{
			world.Delete();
		}
	}

	/// <summary>
	/// Native uses the last added child world and clears it when any link is removed.
	/// </summary>
	[TestMethod]
	public void ChildSelectionTracksNativeRegistrationAndRenderingGates()
	{
		var previousSetting = ConVarSystem.GetInt( "r_3d_skybox", 1, true );
		var world = new SceneWorld();
		var firstWorld = new SceneWorld();
		var lastWorld = new SceneWorld();
		SceneSkybox3D first = null;
		SceneSkybox3D last = null;
		try
		{
			ConVarSystem.SetInt( "r_3d_skybox", 1, true );
			first = new SceneSkybox3D( world, firstWorld );
			first.Update();
			Assert.AreSame( first, world.GetRenderingSkybox() );
			last = new SceneSkybox3D( world, lastWorld );
			last.Update();
			Assert.AreSame( last, world.GetRenderingSkybox(), "An empty child world still replaces its parent" );

			ConVarSystem.SetInt( "r_3d_skybox", 0, true );
			Assert.IsNull( world.GetRenderingSkybox() );
			ConVarSystem.SetInt( "r_3d_skybox", 1, true );
			last.Scale = 0;
			last.Update();
			Assert.IsNull( world.GetRenderingSkybox() );
			last.Scale = -1;
			last.Update();
			Assert.IsNull( world.GetRenderingSkybox() );
			last.Scale = 16;
			last.Update();
			Assert.AreSame( last, world.GetRenderingSkybox() );

			first.Delete();
			first = null;
			Assert.IsNull( world.GetRenderingSkybox(), "Removing an older link also clears native's active child" );
			first = new SceneSkybox3D( world, firstWorld );
			first.Update();
			Assert.AreSame( first, world.GetRenderingSkybox() );
			first.Delete();
			first = null;
			Assert.IsNull( world.GetRenderingSkybox(), "Surviving links must not reactivate automatically" );
		}
		finally
		{
			last?.Delete();
			first?.Delete();
			lastWorld.Delete();
			firstWorld.Delete();
			world.Delete();
			ConVarSystem.SetInt( "r_3d_skybox", previousSetting, true );
		}
	}
}
