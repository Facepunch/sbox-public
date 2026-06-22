using System.Reflection;

namespace SceneTests.Components;

[TestClass]
public class FogComponentTest
{
	/// <summary>
	/// Serializes a GameObject to json, destroys the original, then deserializes the json
	/// back into the scene and enables it - the standard save/load round trip idiom used
	/// by the integration tests.
	/// </summary>
	static GameObject SerializeRoundTrip( Scene scene, GameObject go )
	{
		var json = go.Serialize().ToJsonString();

		go.Destroy();
		scene.ProcessDeletes();

		var jsonObject = Json.ParseToJsonObject( json );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var clone = new GameObject( false );
		clone.Deserialize( jsonObject );
		clone.Enabled = true;

		return clone;
	}

	/// <summary>
	/// Grabs the SceneFogVolume a VolumetricFogVolume component registered with the scene
	/// world. The component keeps it in a private field with no managed accessor - and a
	/// SceneFogVolume is not a SceneObject, so it never shows up in SceneWorld.SceneObjects -
	/// so the tests reach it via reflection to assert the state the component actually drives.
	/// </summary>
	static SceneFogVolume GetFogSceneObject( VolumetricFogVolume component )
	{
		var field = typeof( VolumetricFogVolume ).GetField( "sceneObject", BindingFlags.Instance | BindingFlags.NonPublic );
		return (SceneFogVolume)field.GetValue( component );
	}

	/// <summary>
	/// CubemapFog defaults are pinned, and with no Sky material and no SkyBox2D in the scene
	/// there is no texture to fog with: the camera post-processing pass leaves cubemap fog
	/// disabled with a null texture. The distance, falloff and height parameters are still
	/// copied to the scene camera unconditionally, with LodBias derived as 1 - Blur.
	/// </summary>
	[TestMethod]
	public void CubemapFogDefaultsAndNoTextureDisablesCameraFog()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();

		Assert.IsNull( fog.Sky, "No sky material is assigned by default" );
		Assert.AreEqual( 0.5f, fog.Blur );
		Assert.AreEqual( 10.0f, fog.StartDistance );
		Assert.AreEqual( 4096.0f, fog.EndDistance );
		Assert.AreEqual( 1.0f, fog.FalloffExponent );
		Assert.AreEqual( 0.0f, fog.HeightWidth );
		Assert.AreEqual( 2000.0f, fog.HeightStart );
		Assert.AreEqual( 2.0f, fog.HeightExponent );
		Assert.AreEqual( Color.White, fog.Tint );

		fog.StartDistance = 25.0f;
		fog.EndDistance = 1000.0f;
		fog.Blur = 0.75f;
		fog.HeightWidth = 50.0f;
		fog.HeightStart = 300.0f;
		fog.HeightExponent = 4.0f;
		fog.FalloffExponent = 2.0f;

		cam.CopyPostProcessing( sceneCamera );

		Assert.IsFalse( sceneCamera.CubemapFog.Enabled, "No texture source means cubemap fog stays disabled" );
		Assert.IsNull( sceneCamera.CubemapFog.Texture );
		Assert.AreEqual( 25.0f, sceneCamera.CubemapFog.StartDistance, "Parameters are written even while disabled" );
		Assert.AreEqual( 1000.0f, sceneCamera.CubemapFog.EndDistance );
		Assert.AreEqual( 0.25f, sceneCamera.CubemapFog.LodBias, 0.001f, "LodBias is 1 - Blur" );
		Assert.AreEqual( 2.0f, sceneCamera.CubemapFog.FalloffExponent );
		Assert.AreEqual( 50.0f, sceneCamera.CubemapFog.HeightWidth );
		Assert.AreEqual( 300.0f, sceneCamera.CubemapFog.HeightStart );
		Assert.AreEqual( 4.0f, sceneCamera.CubemapFog.HeightExponent );
		Assert.AreEqual( Color.White, sceneCamera.CubemapFog.Tint, "With no skybox the tint is not multiplied" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// With no Sky material assigned, CubemapFog falls back to the scene's SkyBox2D: it takes
	/// the sky material's g_tSkyTexture and multiplies its own tint by the skybox tint. If the
	/// camera's RenderExcludeTags exclude the skybox (it is tagged "skybox"), the fallback is
	/// skipped and the fog ends up disabled again.
	/// </summary>
	[TestMethod]
	public void CubemapFogUsesSceneSkyboxTextureAndTint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var skyGo = scene.CreateObject();
		var skybox = skyGo.Components.Create<SkyBox2D>();
		skybox.Tint = new Color( 0.5f, 0.5f, 0.5f );

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();
		fog.Tint = new Color( 0.8f, 0.6f, 0.4f );

		cam.CopyPostProcessing( sceneCamera );

		Assert.IsNotNull( skybox.SkyTexture, "The default sky material should expose g_tSkyTexture" );
		Assert.IsTrue( sceneCamera.CubemapFog.Enabled, "A skybox texture enables the cubemap fog" );
		Assert.AreEqual( skybox.SkyTexture, sceneCamera.CubemapFog.Texture, "The fog texture comes from the skybox material" );
		Assert.AreEqual( 0.4f, sceneCamera.CubemapFog.Tint.r, 0.001f, "Fog tint is multiplied by the skybox tint" );
		Assert.AreEqual( 0.3f, sceneCamera.CubemapFog.Tint.g, 0.001f );
		Assert.AreEqual( 0.2f, sceneCamera.CubemapFog.Tint.b, 0.001f );

		cam.RenderExcludeTags.Add( "skybox" );
		cam.CopyPostProcessing( sceneCamera );

		Assert.IsFalse( sceneCamera.CubemapFog.Enabled, "Excluding the skybox tag removes the fallback texture source" );
		Assert.IsNull( sceneCamera.CubemapFog.Texture );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// An explicitly assigned Sky material takes priority over the scene skybox: the texture
	/// comes straight from that material and the tint is not multiplied by the skybox tint.
	/// The fog transform is the component's world transform with the rotation conjugated and
	/// the scale forced to one.
	/// </summary>
	[TestMethod]
	public void CubemapFogExplicitSkyMaterialAndTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var skyGo = scene.CreateObject();
		var skybox = skyGo.Components.Create<SkyBox2D>();
		skybox.Tint = new Color( 0.1f, 0.1f, 0.1f );

		var skyMaterial = Material.Load( "materials/skybox/skybox_day_01.vmat" );

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 10, 20, 30 );
		var rot = Rotation.From( 30, 60, 0 );
		go.WorldRotation = rot;

		var fog = go.Components.Create<CubemapFog>();
		fog.Sky = skyMaterial;
		fog.Tint = new Color( 0.25f, 0.5f, 0.75f );
		fog.Blur = 0.25f;

		cam.CopyPostProcessing( sceneCamera );

		Assert.IsTrue( sceneCamera.CubemapFog.Enabled );
		Assert.AreEqual( skyMaterial.GetTexture( "g_tSkyTexture" ), sceneCamera.CubemapFog.Texture, "Texture comes from the explicit material" );
		Assert.AreEqual( new Color( 0.25f, 0.5f, 0.75f ), sceneCamera.CubemapFog.Tint, "An explicit sky material skips the skybox tint multiply" );
		Assert.AreEqual( 0.75f, sceneCamera.CubemapFog.LodBias, 0.001f );

		var t = sceneCamera.CubemapFog.Transform;
		Assert.IsTrue( t.Position.Distance( new Vector3( 10, 20, 30 ) ) < 0.01f, "Fog transform uses the GameObject position" );
		Assert.IsTrue( t.Rotation.Distance( rot.Conjugate ) < 0.01f, "Fog transform uses the conjugated rotation" );
		Assert.AreEqual( Vector3.One, t.Scale, "Fog transform scale is forced to one" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The camera post-processing pass resets cubemap fog to disabled before letting the
	/// enabled CubemapFog components write their state, so disabling the component leaves the
	/// scene camera's fog off after the next pass and re-enabling brings it back.
	/// </summary>
	[TestMethod]
	public void CubemapFogDisableLeavesCameraFogOff()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var skyGo = scene.CreateObject();
		skyGo.Components.Create<SkyBox2D>();

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();

		cam.CopyPostProcessing( sceneCamera );
		Assert.IsTrue( sceneCamera.CubemapFog.Enabled );

		fog.Enabled = false;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsFalse( sceneCamera.CubemapFog.Enabled, "A disabled component is not visited, leaving the per-pass reset in place" );

		fog.Enabled = true;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsTrue( sceneCamera.CubemapFog.Enabled, "Re-enabling restores the fog on the next pass" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A CubemapFog with a non-default sky material, blur, distances, falloff, height setup
	/// and tint survives a serialize/deserialize round trip; the material round trips by path.
	/// </summary>
	[TestMethod]
	public void CubemapFogSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var skyMaterial = Material.Load( "materials/skybox/skybox_day_01.vmat" );

		var go = scene.CreateObject();
		var fog = go.Components.Create<CubemapFog>();
		fog.Sky = skyMaterial;
		fog.Blur = 0.25f;
		fog.StartDistance = 32.0f;
		fog.EndDistance = 2048.0f;
		fog.FalloffExponent = 2.0f;
		fog.HeightWidth = 100.0f;
		fog.HeightStart = 500.0f;
		fog.HeightExponent = 3.0f;
		fog.Tint = new Color( 0.1f, 0.2f, 0.3f, 0.4f );

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<CubemapFog>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a CubemapFog" );
		Assert.AreEqual( skyMaterial.Name, loaded.Sky?.Name, "Sky material should round trip by path" );
		Assert.AreEqual( 0.25f, loaded.Blur );
		Assert.AreEqual( 32.0f, loaded.StartDistance );
		Assert.AreEqual( 2048.0f, loaded.EndDistance );
		Assert.AreEqual( 2.0f, loaded.FalloffExponent );
		Assert.AreEqual( 100.0f, loaded.HeightWidth );
		Assert.AreEqual( 500.0f, loaded.HeightStart );
		Assert.AreEqual( 3.0f, loaded.HeightExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), loaded.Tint );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Enabling a VolumetricFogVolume registers a SceneFogVolume carrying the component's
	/// world transform, bounds, strength and falloff exponent - but not its color, which stays
	/// at the scene-object default white until the first pre-render copies it. Disabling
	/// deletes the registration, re-enabling creates a fresh one, and destroying the
	/// GameObject tears it down.
	/// </summary>
	[TestMethod]
	public void VolumetricFogVolumeSceneObjectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 50, 60, 70 );

		var volume = go.Components.Create<VolumetricFogVolume>( false );

		Assert.AreEqual( BBox.FromPositionAndSize( 0, 300 ), volume.Bounds, "Default bounds are a 300 unit box" );
		Assert.AreEqual( 1.0f, volume.Strength );
		Assert.AreEqual( 1.0f, volume.FalloffExponent );
		Assert.AreEqual( Color.White, volume.Color );
		Assert.IsNull( GetFogSceneObject( volume ), "No scene fog volume should exist while disabled" );

		var bounds = new BBox( new Vector3( -10, -20, -30 ), new Vector3( 10, 20, 30 ) );
		volume.Bounds = bounds;
		volume.Strength = 0.5f;
		volume.FalloffExponent = 0.75f;
		volume.Color = Color.Red;

		volume.Enabled = true;

		var so = GetFogSceneObject( volume );
		Assert.IsNotNull( so, "Enabling should create the SceneFogVolume" );
		Assert.IsTrue( so.IsValid );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.5f, so.FogStrength );
		Assert.AreEqual( 0.75f, so.FalloffExponent );
		Assert.AreEqual( Color.White, so.Color, "The constructor does not take the color - it stays white until pre-render" );
		Assert.IsTrue( so.Transform.Position.Distance( new Vector3( 50, 60, 70 ) ) < 0.01f );

		scene.GameTick();

		Assert.AreEqual( Color.Red, so.Color, "The first pre-render copies the component color" );

		volume.Enabled = false;

		Assert.IsFalse( so.IsValid, "Disabling should delete the scene fog volume" );
		Assert.IsNull( GetFogSceneObject( volume ) );

		volume.Enabled = true;

		var second = GetFogSceneObject( volume );
		Assert.IsNotNull( second, "Re-enabling should create a new scene fog volume" );
		Assert.AreNotSame( so, second );
		Assert.IsTrue( second.IsValid );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( second.IsValid, "Destroying the GameObject should delete the scene fog volume" );
	}

	/// <summary>
	/// VolumetricFogVolume copies its transform, bounds, strength, falloff and color to the
	/// SceneFogVolume every pre-render, so property changes and GameObject moves are visible
	/// on the scene object after a tick.
	/// </summary>
	[TestMethod]
	public void VolumetricFogVolumePropertyPropagation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();
		var so = GetFogSceneObject( volume );

		var bounds = new BBox( new Vector3( -16, -32, -48 ), new Vector3( 16, 32, 48 ) );
		volume.Bounds = bounds;
		volume.Strength = 0.25f;
		volume.FalloffExponent = 0.5f;
		volume.Color = new Color( 0.1f, 0.2f, 0.3f );
		go.WorldPosition = new Vector3( 100, 0, 0 );

		scene.GameTick();

		Assert.AreEqual( bounds, so.BoundingBox, "Bounds should propagate on pre-render" );
		Assert.AreEqual( 0.25f, so.FogStrength );
		Assert.AreEqual( 0.5f, so.FalloffExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), so.Color );
		Assert.IsTrue( so.Transform.Position.Distance( new Vector3( 100, 0, 0 ) ) < 0.01f, "The scene fog volume follows the GameObject" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The camera post-processing pass turns the scene camera's volumetric fog on exactly when
	/// the scene contains at least one enabled VolumetricFogVolume, writing a fixed set of
	/// parameters (draw distance 4096, fade-in 64..256, indirect strength/anisotropy/scattering
	/// all 1). The whole block only runs when the camera clears color, so without a color clear
	/// the previous values go stale rather than being recomputed.
	/// </summary>
	[TestMethod]
	public void VolumetricFogVolumeDrivesCameraVolumetrics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		cam.CopyPostProcessing( sceneCamera );

		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "No fog volumes means volumetrics stay off" );
		Assert.AreEqual( 4096.0f, sceneCamera.VolumetricFog.DrawDistance );
		Assert.AreEqual( 64.0f, sceneCamera.VolumetricFog.FadeInStart );
		Assert.AreEqual( 256.0f, sceneCamera.VolumetricFog.FadeInEnd );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.IndirectStrength );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.Anisotropy );
		Assert.AreEqual( 1.0f, sceneCamera.VolumetricFog.Scattering );
		Assert.IsNull( sceneCamera.VolumetricFog.BakedIndirectTexture, "No controller means no baked texture" );

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();

		cam.CopyPostProcessing( sceneCamera );
		Assert.IsTrue( sceneCamera.VolumetricFog.Enabled, "An enabled fog volume turns volumetrics on" );

		volume.Enabled = false;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "Disabled volumes are not counted" );

		volume.Enabled = true;
		cam.ClearFlags = ClearFlags.Depth;
		cam.CopyPostProcessing( sceneCamera );
		Assert.IsFalse( sceneCamera.VolumetricFog.Enabled, "Without a color clear the volumetric block is skipped, leaving the previous state" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Nothing enforces a single VolumetricFogController at runtime - only the legacy map
	/// loader destroys existing controllers before creating its own - so two controllers
	/// coexist and the camera takes the baked fog texture from the first enabled one in
	/// registration order. Disabling that one promotes the next.
	/// </summary>
	[TestMethod]
	public void VolumetricFogControllerRegistrationSemantics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		var cam = camGo.Components.Create<CameraComponent>();
		var sceneCamera = cam.SceneCamera;

		var goA = scene.CreateObject();
		var controllerA = goA.Components.Create<VolumetricFogController>();
		controllerA.BakedFogTexture = Texture.White;
		controllerA.GlobalScale = 2.0f;

		var goB = scene.CreateObject();
		var controllerB = goB.Components.Create<VolumetricFogController>();
		controllerB.BakedFogTexture = Texture.Transparent;

		Assert.AreEqual( 2, scene.GetAllComponents<VolumetricFogController>().Count(), "Runtime-created controllers are not deduplicated" );

		cam.CopyPostProcessing( sceneCamera );
		Assert.AreEqual( Texture.White, sceneCamera.VolumetricFog.BakedIndirectTexture, "The first registered controller wins" );

		controllerA.Enabled = false;
		cam.CopyPostProcessing( sceneCamera );
		Assert.AreEqual( Texture.Transparent, sceneCamera.VolumetricFog.BakedIndirectTexture, "Disabling the first controller promotes the next one" );

		goA.Destroy();
		goB.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A VolumetricFogVolume's bounds, strength, falloff exponent and color survive a
	/// serialize/deserialize round trip, and the enabled clone registers a live SceneFogVolume
	/// carrying the loaded values once a tick has run.
	/// </summary>
	[TestMethod]
	public void VolumetricFogVolumeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var bounds = new BBox( new Vector3( -64, -32, -16 ), new Vector3( 64, 32, 16 ) );

		var go = scene.CreateObject();
		var volume = go.Components.Create<VolumetricFogVolume>();
		volume.Bounds = bounds;
		volume.Strength = 0.25f;
		volume.FalloffExponent = 0.75f;
		volume.Color = new Color( 0.1f, 0.2f, 0.3f, 0.4f );

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<VolumetricFogVolume>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a VolumetricFogVolume" );
		Assert.AreEqual( bounds, loaded.Bounds );
		Assert.AreEqual( 0.25f, loaded.Strength );
		Assert.AreEqual( 0.75f, loaded.FalloffExponent );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), loaded.Color );

		var so = GetFogSceneObject( loaded );
		Assert.IsNotNull( so, "The enabled clone should register its scene fog volume" );
		Assert.IsTrue( so.IsValid );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.25f, so.FogStrength );

		scene.GameTick();

		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f, 0.4f ), so.Color, "Pre-render copies the loaded color" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// SceneFogVolume's public surface: construction registers a valid native volume holding
	/// the constructor values with a white default color, property setters store their values,
	/// and Delete invalidates it (double delete is safe). Suspected engine quirk pinned here:
	/// setting a property after Delete calls Update, which re-registers the native volume and
	/// makes the handle valid again.
	/// </summary>
	[TestMethod]
	public void SceneFogVolumeDirectLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var bounds = BBox.FromPositionAndSize( 0, 100 );
		var so = new SceneFogVolume( scene.SceneWorld, Transform.Zero.WithPosition( new Vector3( 1, 2, 3 ) ), bounds, 0.75f, 2.0f );

		Assert.IsTrue( so.IsValid, "Construction should register a valid fog volume" );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), so.Transform.Position );
		Assert.AreEqual( bounds, so.BoundingBox );
		Assert.AreEqual( 0.75f, so.FogStrength );
		Assert.AreEqual( 2.0f, so.FalloffExponent );
		Assert.AreEqual( Color.White, so.Color, "Color defaults to white" );

		var newBounds = new BBox( new Vector3( -50, -50, 0 ), new Vector3( 50, 50, 100 ) );
		so.BoundingBox = newBounds;
		so.FogStrength = 0.5f;
		so.FalloffExponent = 3.0f;
		so.Color = Color.Blue;
		so.Transform = Transform.Zero.WithPosition( new Vector3( 9, 8, 7 ) );

		Assert.AreEqual( newBounds, so.BoundingBox );
		Assert.AreEqual( 0.5f, so.FogStrength );
		Assert.AreEqual( 3.0f, so.FalloffExponent );
		Assert.AreEqual( Color.Blue, so.Color );
		Assert.AreEqual( new Vector3( 9, 8, 7 ), so.Transform.Position );
		Assert.IsTrue( so.IsValid, "Property updates keep the volume valid" );

		so.Delete();

		Assert.IsFalse( so.IsValid, "Delete should invalidate the volume" );

		so.Delete();

		Assert.IsFalse( so.IsValid, "Double delete is safe" );

		so.FogStrength = 0.9f;

		Assert.IsTrue( so.IsValid, "A property write after Delete re-registers the native volume" );

		so.Delete();

		Assert.IsFalse( so.IsValid );
	}
}
