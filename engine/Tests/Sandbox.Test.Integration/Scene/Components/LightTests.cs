namespace SceneTests.Components;

[TestClass]
public class LightComponentTest
{
	/// <summary>
	/// Finds the scene object of the given type that was created by the given component,
	/// using the internal SceneObject.Component back-reference. Returns null when the
	/// component has no live scene object (SceneWorld.SceneObjects filters invalid handles).
	/// </summary>
	static T FindSceneObjectFor<T>( Scene scene, Component component ) where T : SceneObject
	{
		return scene.SceneWorld.SceneObjects.OfType<T>().FirstOrDefault( x => x.Component == component );
	}

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
	/// Enabling a PointLight creates a ScenePointLight in the scene world with the component's
	/// defaults applied, tags the GameObject with "light"/"light_point", disabling deletes the
	/// scene object, re-enabling creates a fresh one, and destroying the GameObject tears it down.
	/// </summary>
	[TestMethod]
	public void PointLightLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>();

		var so = FindSceneObjectFor<ScenePointLight>( scene, light );
		Assert.IsNotNull( so, "Enabling a PointLight should create a ScenePointLight" );
		Assert.IsTrue( so.IsValid() );
		Assert.IsTrue( go.Tags.Has( "light" ), "Light components should tag their GameObject with 'light'" );
		Assert.IsTrue( go.Tags.Has( "light_point" ), "PointLight should tag its GameObject with 'light_point'" );
		Assert.AreEqual( 400.0f, so.Radius, 0.001f, "Default radius should be applied to the scene object" );

		light.Enabled = false;

		Assert.IsFalse( so.IsValid(), "Disabling the component should delete the scene object" );
		Assert.IsNull( FindSceneObjectFor<ScenePointLight>( scene, light ) );

		light.Enabled = true;

		var second = FindSceneObjectFor<ScenePointLight>( scene, light );
		Assert.IsNotNull( second, "Re-enabling should create a new scene object" );
		Assert.AreNotSame( so, second, "Re-enabling should not resurrect the old scene object" );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( second.IsValid(), "Destroying the GameObject should delete the scene object" );
		Assert.AreEqual( 0, scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().Count() );
	}

	/// <summary>
	/// Enabling a SpotLight creates a SceneSpotLight carrying the component's default radius,
	/// cone angles, falloff and the "light_spot" tag; disabling deletes the scene object.
	/// </summary>
	[TestMethod]
	public void SpotLightLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<SpotLight>();

		var so = FindSceneObjectFor<SceneSpotLight>( scene, light );
		Assert.IsNotNull( so, "Enabling a SpotLight should create a SceneSpotLight" );
		Assert.IsTrue( go.Tags.Has( "light_spot" ) );
		Assert.AreEqual( 500.0f, so.Radius, 0.001f, "Default radius should be applied" );
		Assert.AreEqual( 45.0f, so.ConeOuter, 0.1f, "Default outer cone should be applied" );
		Assert.AreEqual( 15.0f, so.ConeInner, 0.1f, "Default inner cone should be applied" );
		Assert.AreEqual( 1.0f, so.FallOff, 0.001f, "FallOff is hardcoded to 1 on creation" );

		light.Enabled = false;

		Assert.IsFalse( so.IsValid(), "Disabling the component should delete the scene object" );
		Assert.IsNull( FindSceneObjectFor<SceneSpotLight>( scene, light ) );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Enabling a DirectionalLight creates a SceneDirectionalLight with the default shadow
	/// cascade setup and the "light_directional" tag; disabling deletes the scene object.
	/// </summary>
	[TestMethod]
	public void DirectionalLightLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<DirectionalLight>();

		var so = FindSceneObjectFor<SceneDirectionalLight>( scene, light );
		Assert.IsNotNull( so, "Enabling a DirectionalLight should create a SceneDirectionalLight" );
		Assert.IsTrue( go.Tags.Has( "light_directional" ) );
		Assert.AreEqual( 4, so.ShadowCascadeCount, "Default cascade count should be applied" );
		Assert.AreEqual( 0.91f, so.ShadowCascadeSplitRatio, 0.01f, "Default split ratio should be applied" );

		light.Enabled = false;

		Assert.IsFalse( so.IsValid(), "Disabling the component should delete the scene object" );
		Assert.IsNull( FindSceneObjectFor<SceneDirectionalLight>( scene, light ) );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Property setters on a live PointLight write through to the scene object: color, radius,
	/// quadratic attenuation, shadow enable, and the managed shadow bias/hardness values.
	/// The component keeps its own state exactly as set.
	/// </summary>
	[TestMethod]
	public void PointLightSettersPropagate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>();
		var so = FindSceneObjectFor<ScenePointLight>( scene, light );

		light.LightColor = new Color( 0.25f, 0.5f, 0.75f );
		light.Radius = 123.0f;
		light.Attenuation = 2.5f;
		light.Shadows = false;
		light.ShadowBias = 0.25f;
		light.ShadowHardness = 0.5f;

		Assert.AreEqual( new Color( 0.25f, 0.5f, 0.75f ), light.LightColor );
		Assert.AreEqual( 123.0f, light.Radius );
		Assert.AreEqual( 2.5f, light.Attenuation );
		Assert.IsFalse( light.Shadows );

		Assert.AreEqual( 0.25f, so.LightColor.r, 0.001f );
		Assert.AreEqual( 0.5f, so.LightColor.g, 0.001f );
		Assert.AreEqual( 0.75f, so.LightColor.b, 0.001f );
		Assert.AreEqual( 123.0f, so.Radius, 0.001f );
		Assert.AreEqual( 2.5f, so.QuadraticAttenuation, 0.01f );
		Assert.IsFalse( so.ShadowsEnabled );
		Assert.AreEqual( 0.25f, so.ShadowBias );
		Assert.AreEqual( 0.5f, so.ShadowHardness );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Cone angle, radius and attenuation setters on a live SpotLight write through to the
	/// SceneSpotLight's native theta/phi/radius/attenuation values.
	/// </summary>
	[TestMethod]
	public void SpotLightConeSettersPropagate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<SpotLight>();
		var so = FindSceneObjectFor<SceneSpotLight>( scene, light );

		light.ConeOuter = 60.0f;
		light.ConeInner = 30.0f;
		light.Radius = 250.0f;
		light.Attenuation = 0.5f;

		Assert.AreEqual( 60.0f, light.ConeOuter );
		Assert.AreEqual( 30.0f, light.ConeInner );

		Assert.AreEqual( 60.0f, so.ConeOuter, 0.1f );
		Assert.AreEqual( 30.0f, so.ConeInner, 0.1f );
		Assert.AreEqual( 250.0f, so.Radius, 0.001f );
		Assert.AreEqual( 0.5f, so.QuadraticAttenuation, 0.01f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Shadow cascade count and split ratio setters on a live DirectionalLight write through
	/// to the SceneDirectionalLight.
	/// </summary>
	[TestMethod]
	public void DirectionalLightCascadeSettersPropagate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<DirectionalLight>();
		var so = FindSceneObjectFor<SceneDirectionalLight>( scene, light );

		light.ShadowCascadeCount = 2;
		light.ShadowCascadeSplitRatio = 0.5f;

		Assert.AreEqual( 2, light.ShadowCascadeCount );
		Assert.AreEqual( 2, so.ShadowCascadeCount );
		Assert.AreEqual( 0.5f, so.ShadowCascadeSplitRatio, 0.01f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// The Contribution flags map onto the scene object's RenderDiffuse/RenderSpecular/
	/// RenderTransmissive switches (all on by default), and the fog mode/strength properties
	/// write through to FogLighting and FogStrength.
	/// </summary>
	[TestMethod]
	public void LightContributionAndFogPropagate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>();
		var so = FindSceneObjectFor<ScenePointLight>( scene, light );

		Assert.IsTrue( so.RenderDiffuse, "Diffuse contribution should default on" );
		Assert.IsTrue( so.RenderSpecular, "Specular contribution should default on" );
		Assert.IsTrue( so.RenderTransmissive, "Transmissive contribution should default on" );
		Assert.AreEqual( SceneLight.FogLightingMode.Dynamic, so.FogLighting, "Fog should default to Dynamic" );

		light.Contribution = Light.LightContribution.Diffuse;

		Assert.IsTrue( so.RenderDiffuse );
		Assert.IsFalse( so.RenderSpecular, "Removing the Specular flag should turn off specular rendering" );
		Assert.IsFalse( so.RenderTransmissive, "Removing the Transmissive flag should turn off transmissive rendering" );

		light.FogMode = Light.FogInfluence.Disabled;
		light.FogStrength = 0.25f;

		Assert.AreEqual( SceneLight.FogLightingMode.None, so.FogLighting );
		Assert.AreEqual( 0.25f, so.FogStrength, 0.001f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A PointLight created disabled has no scene object; properties configured while disabled
	/// are all applied to the scene object created when the component is finally enabled.
	/// </summary>
	[TestMethod]
	public void PointLightConfiguredBeforeEnable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>( false );

		light.LightColor = Color.Red;
		light.Radius = 64.0f;
		light.Attenuation = 0.0f;
		light.Shadows = false;

		Assert.IsNull( FindSceneObjectFor<ScenePointLight>( scene, light ), "No scene object should exist while disabled" );

		light.Enabled = true;

		var so = FindSceneObjectFor<ScenePointLight>( scene, light );
		Assert.IsNotNull( so );
		Assert.AreEqual( 64.0f, so.Radius, 0.001f );
		Assert.AreEqual( 0.0f, so.QuadraticAttenuation, 0.01f );
		Assert.IsFalse( so.ShadowsEnabled );
		Assert.AreEqual( 1.0f, so.LightColor.r, 0.001f );
		Assert.AreEqual( 0.0f, so.LightColor.g, 0.001f );
		Assert.AreEqual( 0.0f, so.LightColor.b, 0.001f );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Lights subscribe to their GameObject's transform-changed event, so moving or rotating
	/// the GameObject moves the scene object - pinned here after a game tick, which is the
	/// state a frame would render with.
	/// </summary>
	[TestMethod]
	public void LightFollowsGameObjectTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<SpotLight>();
		var so = FindSceneObjectFor<SceneSpotLight>( scene, light );

		var pos = new Vector3( 100, 200, 300 );
		var rot = Rotation.From( 30, 60, 0 );

		go.WorldPosition = pos;
		go.WorldRotation = rot;

		scene.GameTick();

		Assert.IsTrue( so.Position.Distance( pos ) < 0.01f, "Scene object should follow the GameObject position" );
		Assert.IsTrue( so.Rotation.Distance( rot ) < 0.01f, "Scene object should follow the GameObject rotation" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A PointLight with non-default color, radius, attenuation, shadows, fog and contribution
	/// values survives a GameObject json serialize/deserialize round trip, and the deserialized
	/// component creates a live scene object when enabled.
	/// </summary>
	[TestMethod]
	public void PointLightSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<PointLight>();
		light.LightColor = new Color( 0.1f, 0.2f, 0.3f );
		light.Radius = 222.0f;
		light.Attenuation = 3.0f;
		light.Shadows = false;
		light.FogStrength = 0.5f;
		light.FogMode = Light.FogInfluence.WithoutShadows;
		light.Contribution = Light.LightContribution.Diffuse | Light.LightContribution.Specular;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<PointLight>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a PointLight" );
		Assert.AreEqual( new Color( 0.1f, 0.2f, 0.3f ), loaded.LightColor );
		Assert.AreEqual( 222.0f, loaded.Radius );
		Assert.AreEqual( 3.0f, loaded.Attenuation );
		Assert.IsFalse( loaded.Shadows );
		Assert.AreEqual( 0.5f, loaded.FogStrength );
		Assert.AreEqual( Light.FogInfluence.WithoutShadows, loaded.FogMode );
		Assert.AreEqual( Light.LightContribution.Diffuse | Light.LightContribution.Specular, loaded.Contribution );

		var so = FindSceneObjectFor<ScenePointLight>( scene, loaded );
		Assert.IsNotNull( so, "Deserialized light should create its scene object when enabled" );
		Assert.AreEqual( 222.0f, so.Radius, 0.001f );
		Assert.IsFalse( so.ShadowsEnabled );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A SpotLight's cone angles, radius and attenuation survive a serialize/deserialize
	/// round trip and are applied to the recreated scene object.
	/// </summary>
	[TestMethod]
	public void SpotLightSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<SpotLight>();
		light.ConeOuter = 60.0f;
		light.ConeInner = 30.0f;
		light.Radius = 250.0f;
		light.Attenuation = 0.5f;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<SpotLight>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a SpotLight" );
		Assert.AreEqual( 60.0f, loaded.ConeOuter );
		Assert.AreEqual( 30.0f, loaded.ConeInner );
		Assert.AreEqual( 250.0f, loaded.Radius );
		Assert.AreEqual( 0.5f, loaded.Attenuation );

		var so = FindSceneObjectFor<SceneSpotLight>( scene, loaded );
		Assert.IsNotNull( so );
		Assert.AreEqual( 60.0f, so.ConeOuter, 0.1f );
		Assert.AreEqual( 30.0f, so.ConeInner, 0.1f );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A DirectionalLight's cascade configuration, sky color and shadow bias/hardness survive
	/// a serialize/deserialize round trip and the cascade count is applied to the recreated
	/// scene object.
	/// </summary>
	[TestMethod]
	public void DirectionalLightSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var light = go.Components.Create<DirectionalLight>();
		light.LightColor = Color.White;
		light.SkyColor = Color.Blue;
		light.ShadowCascadeCount = 2;
		light.ShadowCascadeSplitRatio = 0.5f;
		light.ShadowBias = 0.25f;
		light.ShadowHardness = 0.75f;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<DirectionalLight>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a DirectionalLight" );
		Assert.AreEqual( Color.White, loaded.LightColor );
		Assert.AreEqual( Color.Blue, loaded.SkyColor );
		Assert.AreEqual( 2, loaded.ShadowCascadeCount );
		Assert.AreEqual( 0.5f, loaded.ShadowCascadeSplitRatio );
		Assert.AreEqual( 0.25f, loaded.ShadowBias );
		Assert.AreEqual( 0.75f, loaded.ShadowHardness );

		var so = FindSceneObjectFor<SceneDirectionalLight>( scene, loaded );
		Assert.IsNotNull( so );
		Assert.AreEqual( 2, so.ShadowCascadeCount );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// AmbientLight is a plain state component: it defaults to gray, holds its color through
	/// enable/disable cycles, and the color survives a serialize/deserialize round trip.
	/// </summary>
	[TestMethod]
	public void AmbientLightDefaultsAndRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var ambient = go.Components.Create<AmbientLight>();

		Assert.AreEqual( Color.Gray, ambient.Color, "Ambient color should default to gray" );

		ambient.Color = new Color( 0.25f, 0.0f, 0.5f );

		ambient.Enabled = false;
		ambient.Enabled = true;

		Assert.AreEqual( new Color( 0.25f, 0.0f, 0.5f ), ambient.Color, "Color should survive an enable cycle" );

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<AmbientLight>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have an AmbientLight" );
		Assert.AreEqual( new Color( 0.25f, 0.0f, 0.5f ), loaded.Color );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Enabling an EnvmapProbe creates a SceneCubemap in the scene world, disabling removes
	/// it again, and repeated enable/disable cycles never leak cubemap scene objects.
	/// </summary>
	[TestMethod]
	public void EnvmapProbeLifecycle()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseline = scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count();

		var go = scene.CreateObject();
		var probe = go.Components.Create<EnvmapProbe>();

		Assert.AreEqual( baseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count(), "Enabling should create one SceneCubemap" );

		for ( int i = 0; i < 3; i++ )
		{
			probe.Enabled = false;
			Assert.AreEqual( baseline, scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count(), $"Iteration {i}: disabling should remove the SceneCubemap" );

			probe.Enabled = true;
			Assert.AreEqual( baseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count(), $"Iteration {i}: re-enabling should create exactly one SceneCubemap" );
		}

		go.Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( baseline, scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count(), "Destroying the GameObject should remove the SceneCubemap" );
	}

	/// <summary>
	/// Property setters on a live EnvmapProbe (tint, feathering, priority, projection mode and
	/// bounds) write through to the SceneCubemap. In the default Baked mode the projection
	/// bounds are passed through unmodified.
	/// </summary>
	[TestMethod]
	public void EnvmapProbeSettersPropagate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<EnvmapProbe>();
		var so = scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Single();

		probe.TintColor = new Color( 0.5f, 0.25f, 0.125f );
		probe.Feathering = 4.0f;
		probe.Priority = 7;
		probe.Projection = SceneCubemap.ProjectionMode.Box;

		var bounds = new BBox( new Vector3( -64, -32, -16 ), new Vector3( 64, 32, 16 ) );
		probe.Bounds = bounds;

		Assert.AreEqual( 0.5f, so.TintColor.r, 0.001f );
		Assert.AreEqual( 0.25f, so.TintColor.g, 0.001f );
		Assert.AreEqual( 0.125f, so.TintColor.b, 0.001f );
		Assert.AreEqual( 4.0f, so.Feathering, 0.001f );
		Assert.AreEqual( 7, so.Priority );
		Assert.AreEqual( SceneCubemap.ProjectionMode.Box, so.Projection );
		Assert.AreEqual( bounds.Mins, so.ProjectionBounds.Mins );
		Assert.AreEqual( bounds.Maxs, so.ProjectionBounds.Maxs );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// An EnvmapProbe configured while disabled keeps all its state through a serialize/
	/// deserialize round trip - including the disabled component state - and can then be
	/// enabled safely with a CustomTexture mode and no texture assigned.
	/// </summary>
	[TestMethod]
	public void EnvmapProbeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var baseline = scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count();

		var go = scene.CreateObject();
		var probe = go.Components.Create<EnvmapProbe>( false );

		probe.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
		probe.Projection = SceneCubemap.ProjectionMode.Box;
		probe.Bounds = BBox.FromPositionAndSize( 0, 256 );
		probe.TintColor = Color.Red;
		probe.Feathering = 2.0f;
		probe.Priority = 3;
		probe.ZNear = 8.0f;
		probe.ZFar = 2048.0f;
		probe.MaxDistance = 256.0f;
		probe.UpdateStrategy = EnvmapProbe.CubemapDynamicUpdate.TimeInterval;
		probe.DelayBetweenUpdates = 0.5f;
		probe.FrameInterval = 7;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<EnvmapProbe>( true );

		Assert.IsNotNull( loaded, "Deserialized GameObject should have an EnvmapProbe" );
		Assert.IsFalse( loaded.Enabled, "Disabled component state should survive the round trip" );
		Assert.AreEqual( EnvmapProbe.EnvmapProbeMode.CustomTexture, loaded.Mode );
		Assert.AreEqual( SceneCubemap.ProjectionMode.Box, loaded.Projection );
		Assert.AreEqual( BBox.FromPositionAndSize( 0, 256 ), loaded.Bounds );
		Assert.AreEqual( Color.Red, loaded.TintColor );
		Assert.AreEqual( 2.0f, loaded.Feathering );
		Assert.AreEqual( 3, loaded.Priority );
		Assert.AreEqual( 8.0f, loaded.ZNear );
		Assert.AreEqual( 2048.0f, loaded.ZFar );
		Assert.AreEqual( 256.0f, loaded.MaxDistance );
		Assert.AreEqual( EnvmapProbe.CubemapDynamicUpdate.TimeInterval, loaded.UpdateStrategy );
		Assert.AreEqual( 0.5f, loaded.DelayBetweenUpdates );
		Assert.AreEqual( 7, loaded.FrameInterval );
		Assert.IsNull( loaded.Texture, "No custom texture was assigned" );

		loaded.Enabled = true;

		Assert.AreEqual( baseline + 1, scene.SceneWorld.SceneObjects.OfType<SceneCubemap>().Count(), "Enabling the deserialized probe should create its SceneCubemap" );

		clone.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// IndirectLightVolume's probe grid math is pure component state: counts derive from
	/// bounds size and density (ceil(size * density / 1024) + 1), clamped to 4..40 per axis,
	/// and spacing splits the bounds evenly between probes.
	/// </summary>
	[TestMethod]
	public void IndirectLightVolumeProbeGrid()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<IndirectLightVolume>( false );

		Assert.AreEqual( BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 512.0f ) ), volume.Bounds );
		Assert.AreEqual( 8, volume.ProbeDensity );
		Assert.AreEqual( 5.0f, volume.NormalBias );
		Assert.AreEqual( 1.0f, volume.Contrast );
		Assert.AreEqual( IndirectLightVolume.InsideGeometryBehavior.Relocate, volume.InsideGeometry );

		Assert.AreEqual( new Vector3Int( 5, 5, 5 ), volume.ProbeCounts, "512 units at density 8 should give 5 probes per axis" );
		Assert.AreEqual( new Vector3( 128, 128, 128 ), volume.ComputeSpacing( volume.ProbeCounts ), "5 probes across 512 units should be 128 apart" );

		volume.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 1024.0f ) );
		Assert.AreEqual( new Vector3Int( 9, 9, 9 ), volume.ProbeCounts );

		volume.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 16.0f ) );
		Assert.AreEqual( new Vector3Int( 4, 4, 4 ), volume.ProbeCounts, "Probe counts should clamp to a minimum of 4 per axis" );

		volume.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 100000.0f ) );
		Assert.AreEqual( new Vector3Int( 40, 40, 40 ), volume.ProbeCounts, "Probe counts should clamp to a maximum of 40 per axis" );

		volume.Bounds = BBox.FromPositionAndSize( Vector3.Zero, new Vector3( 512.0f ) );
		volume.ProbeDensity = 15;
		Assert.AreEqual( new Vector3Int( 9, 9, 9 ), volume.ProbeCounts, "Raising density should raise the probe count" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// An IndirectLightVolume with no baked data can be enabled, ticked, disabled and
	/// re-enabled repeatedly without creating textures or probe data, and the probe grid
	/// helpers behave: no probe data exists and probe zero sits at the bounds minimum.
	/// </summary>
	[TestMethod]
	public void IndirectLightVolumeEnableDisableSafety()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<IndirectLightVolume>();

		for ( int i = 0; i < 3; i++ )
		{
			for ( int t = 0; t < 5; t++ )
			{
				scene.GameTick();
			}

			volume.Enabled = false;
			scene.GameTick();
			volume.Enabled = true;
		}

		Assert.IsTrue( volume.IsValid(), "Volume should survive repeated enable/disable cycles" );
		Assert.IsNull( volume.IrradianceTexture, "No irradiance texture should exist without a bake" );
		Assert.IsNull( volume.DistanceTexture, "No distance texture should exist without a bake" );
		Assert.IsNull( volume.RelocationTexture, "No relocation texture should exist without a bake" );

		Assert.IsNull( volume.GetProbe( new Vector3Int( 0, 0, 0 ) ), "No probe data should exist before relocation or baking" );
		Assert.AreEqual( volume.Bounds.Mins, volume.GetProbeWorldPosition( new Vector3Int( 0, 0, 0 ) ), "Probe zero should sit at the bounds minimum for an origin GameObject" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// An IndirectLightVolume's bounds, density, bias, contrast and inside-geometry behavior
	/// survive a serialize/deserialize round trip; the baked texture slots stay empty.
	/// </summary>
	[TestMethod]
	public void IndirectLightVolumeSerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var volume = go.Components.Create<IndirectLightVolume>();

		var bounds = BBox.FromPositionAndSize( new Vector3( 0, 0, 128 ), new Vector3( 256.0f ) );
		volume.Bounds = bounds;
		volume.ProbeDensity = 12;
		volume.NormalBias = 10.0f;
		volume.Contrast = 1.5f;
		volume.InsideGeometry = IndirectLightVolume.InsideGeometryBehavior.Deactivate;

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<IndirectLightVolume>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have an IndirectLightVolume" );
		Assert.AreEqual( bounds, loaded.Bounds );
		Assert.AreEqual( 12, loaded.ProbeDensity );
		Assert.AreEqual( 10.0f, loaded.NormalBias );
		Assert.AreEqual( 1.5f, loaded.Contrast );
		Assert.AreEqual( IndirectLightVolume.InsideGeometryBehavior.Deactivate, loaded.InsideGeometry );
		Assert.IsNull( loaded.IrradianceTexture );
		Assert.IsNull( loaded.DistanceTexture );
		Assert.IsNull( loaded.RelocationTexture );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}
