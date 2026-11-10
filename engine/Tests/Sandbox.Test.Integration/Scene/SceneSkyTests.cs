using System;

namespace SceneTests;

/// <summary>
/// Checks sky state shared by scene-world consumers and the managed renderer.
/// </summary>
[TestClass]
public class SceneSkyTests
{
	/// <summary>
	/// Skies created through native interop must expose their material and subsequent native changes.
	/// </summary>
	[TestMethod]
	public void MaterialTracksNativeCreationAndChanges()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var sky = NativeEngine.CSceneSystem.CreateSkyBox( material.native, world );
			Assert.AreSame( material, sky.Material );

			var replacement = material.CreateCopy();
			var nativeSky = (NativeEngine.CSceneSkyBoxObject)sky.native;
			nativeSky.SetMaterial( replacement.native );
			Assert.AreSame( replacement, sky.Material );
			nativeSky.SetMaterial( default );
			Assert.IsNull( sky.Material );

			sky.SkyMaterial = material;
			Assert.AreSame( material, sky.Material );
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
	/// The renderer refreshes sky selection for each camera, including native material changes without notifications.
	/// </summary>
	[TestMethod]
	public void MirrorRefreshesSkyForEachView()
	{
		var world = new SceneWorld();
		try
		{
			var material = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			var first = new SceneSkyBox( world, material ) { SkyTint = Color.Red };
			var last = new SceneSkyBox( world, material ) { SkyTint = Color.Blue };
			last.Tags.Add( "last" );
			using var camera = new SceneCamera { World = world };
			var mirrorType = typeof( Sandbox.SceneRenderer.RenderWorld ).Assembly.GetType( "Sandbox.SceneRenderer.Bridge.SceneMirror", true );
			using var mirror = (IDisposable)Activator.CreateInstance( mirrorType, [world] );
			var renderWorld = (Sandbox.SceneRenderer.RenderWorld)mirrorType.GetProperty( "World" ).GetValue( mirror );
			var sync = mirrorType.GetMethod( "Sync" );

			sync.Invoke( mirror, [camera, false] );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SkyTint );
			camera.ExcludeTags.Add( "last" );
			sync.Invoke( mirror, [camera, false] );
			Assert.AreEqual( Color.Red, renderWorld.Lighting.SkyTint );
			sync.Invoke( mirror, [camera, true] );
			Assert.AreEqual( Color.Blue, renderWorld.Lighting.SkyTint );

			var replacement = material.CreateCopy();
			var nativeSky = (NativeEngine.CSceneSkyBoxObject)last.native;
			nativeSky.SetMaterial( replacement.native );
			camera.ExcludeTags.RemoveAll();
			sync.Invoke( mirror, [camera, false] );
			Assert.AreSame( replacement, renderWorld.Lighting.SkyMaterial );
			nativeSky.SetMaterial( default );
			sync.Invoke( mirror, [camera, false] );
			Assert.IsNull( renderWorld.Lighting.SkyMaterial, "A null material on the newest sky must not reveal an older sky" );
			first.RenderingEnabled = false;
			last.RenderingEnabled = false;
			sync.Invoke( mirror, [camera, false] );
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
