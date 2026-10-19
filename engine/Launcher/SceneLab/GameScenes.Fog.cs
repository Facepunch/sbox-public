using Sandbox.Rendering;

namespace Sandbox.SceneLab;

internal static partial class GameScenes
{
	/// <summary>
	/// A common fog stage with opaque, alpha-blended and additive geometry at several distances and heights.
	/// </summary>
	static Scene FogStage( out CameraComponent camera )
	{
		var scene = Stage( out camera );
		using var scope = scene.Push();
		PostProps();
		Prop( "Fog Transparent", "models/dev/box.vmdl", new Vector3( 50, 80, 70 ), new Color( 1, 1, 1, 0.5f ), "materials/dev/primary_white_trans.vmat", 0.6f );
		Prop( "Fog Additive", "models/dev/sphere.vmdl", new Vector3( -60, 80, 100 ), Color.White, "materials/dev/primary_red_additive.vmat", 0.7f );
		Prop( "High Fog Target", "models/dev/sphere.vmdl", new Vector3( 100, 100, 200 ), Color.White, scale: 0.7f );
		return scene;
	}

	/// <summary>
	/// Camera-owned artistic fog, including surfaces near the eye and the independent sky coverage control.
	/// </summary>
	public static Scene BetterFogDistance()
	{
		var scene = FogStage( out var camera );
		using var scope = scene.Push();
		var fog = camera.GameObject.Components.Create<Fog>();
		fog.Mode = FogMode.Distance;
		fog.DistanceRange = new Vector2( 80, 650 );
		fog.Color = new Color( 0.1f, 0.4f, 0.65f );
		fog.MaximumOpacity = 0.85f;
		fog.SkyAffect = 0.3f;
		return scene;
	}

	/// <summary>
	/// Independent height and distance gradients use different colors on the same surfaces.
	/// </summary>
	public static Scene BetterFogHeight()
	{
		var scene = FogStage( out var camera );
		using var scope = scene.Push();
		var fog = new GameObject( true, "Height Fog" ).Components.Create<Fog>();
		fog.AttenuationDistance = 280;
		fog.HeightEnabled = true;
		fog.HeightOffset = 40;
		fog.HeightGradientLength = 70;
		fog.HeightColor = new Color( 0.6f, 0.3f, 0.2f, 0.6f );
		fog.Color = new Color( 0.35f, 0.55f, 0.7f );
		fog.SkyAffect = 0.5f;
		return scene;
	}

	/// <summary>
	/// Height fog remains visible with distance fog disabled, including when its opacity cap is zero.
	/// </summary>
	public static Scene BetterFogHeightOnly()
	{
		var scene = BetterFogHeight();
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.DistanceEnabled = false;
		fog.MaximumOpacity = 0;
		fog.HeightColor = new Color( 0.8f, 0.1f, 0.05f );
		return scene;
	}

	/// <summary>
	/// Reverses the gradient without changing the distance settings, covering the opposite side of the height plane.
	/// </summary>
	public static void ReverseBetterFogHeight( Scene scene )
	{
		scene.GetAllComponents<Fog>().Single().HeightGradientLength *= -1;
	}

	/// <summary>
	/// Directional fog color comes from the scene sky while geometry and sky use independent coverage.
	/// </summary>
	public static Scene BetterFogSky()
	{
		var scene = FogStage( out var camera );
		using var scope = scene.Push();
		var fog = camera.GameObject.Components.Create<Fog>();
		fog.AttenuationDistance = 250;
		fog.ColorSource = FogColorSource.SceneSky;
		fog.SkyBlur = 0.25f;
		fog.SkyAffect = 0;
		fog.Color = new Color( 0.65f, 0.8f, 1 );
		return scene;
	}

	/// <summary>
	/// Fully fogged patches must disappear into an asymmetric sky at identity and after a quarter-turn rotation.
	/// </summary>
	public static Scene BetterFogRotatedSky()
	{
		var scene = BetterFogAdditiveSprites();
		using var scope = scene.Push();
		foreach ( var sprite in scene.GetAllComponents<SpriteRenderer>().ToArray() )
		{
			sprite.GameObject.Destroy();
		}

		// Uniform faces make adjacent sky and geometry pixels exactly comparable without a gradient tolerance.
		// Standard cube order: +X red, -X cyan, +Y green, -Y magenta, +Z blue, -Z yellow.
		Color32[] colors = [new( 255, 48, 24 ), new( 24, 220, 220 ), new( 32, 240, 48 ), new( 230, 32, 220 ), new( 32, 48, 240 ), new( 240, 220, 32 )];
		const int size = 8;
		var pixels = new byte[size * size * 4 * 6];
		for ( int face = 0; face < colors.Length; face++ )
		{
			for ( int pixel = 0; pixel < size * size; pixel++ )
			{
				int offset = (face * size * size + pixel) * 4;
				pixels[offset] = colors[face].r;
				pixels[offset + 1] = colors[face].g;
				pixels[offset + 2] = colors[face].b;
				pixels[offset + 3] = 255;
			}
		}

		var material = Material.Load( "materials/skybox/skybox_day_01.vmat" ).CreateCopy();
		material.SetFeature( "F_TEXTURE_FORMAT2", 1 );
		material.Set( "g_tSkyTexture", Texture.CreateCube( size, size, ImageFormat.RGBA8888 ).WithData( pixels ).Finish() );
		material.Set( "g_flBrightnessExposureBias", 0.0f );
		material.Set( "g_flRenderOnlyExposureBias", 0.0f );
		var sky = new GameObject( true, "Rotating Fog Sky" ).Components.Create<SkyBox2D>();
		sky.SkyIndirectLighting = false;
		sky.SkyMaterial = material;

		for ( int i = 0; i < 3; i++ )
		{
			var position = Vector3.Up * 30 + scene.Camera.WorldRotation.Right * ((i - 1) * 70);
			Prop( $"Sky Fog Patch {i}", "models/dev/box.vmdl", position, material: "materials/dev/black_unlit.vmat", scale: 0.25f );
		}

		var fog = scene.GetAllComponents<Fog>().Single();
		fog.Color = Color.White;
		fog.ColorSource = FogColorSource.SceneSky;
		fog.SkyBlur = 0;
		fog.SkyAffect = 0;
		return scene;
	}

	/// <summary>
	/// Height-only fog samples the asymmetric scene sky and follows its rotation.
	/// </summary>
	public static Scene BetterFogHeightSky()
	{
		var scene = BetterFogRotatedSky();
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.DistanceEnabled = false;
		fog.HeightEnabled = true;
		fog.HeightOffset = 100;
		fog.HeightGradientLength = 100;
		fog.HeightColorSource = FogColorSource.SceneSky;
		fog.HeightSkyBlur = 0;
		return scene;
	}

	/// <summary>
	/// Shared sky sampling and a separate custom height source must produce the same rendering in both pipelines.
	/// </summary>
	public static Scene BetterFogSharedSky()
	{
		var scene = BetterFogHeightSky();
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.DistanceEnabled = true;
		fog.MaximumOpacity = 0.4f;
		return scene;
	}

	/// <summary>
	/// Selects a different material, orientation and blur for height while distance continues to follow the scene sky.
	/// </summary>
	public static void ChangeBetterFogHeightSky( Scene scene )
	{
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.HeightColorSource = FogColorSource.CustomSky;
		fog.HeightSky = Material.Load( "materials/skybox/skybox_day_01.vmat" );
		fog.HeightSkyBlur = 0.75f;
		fog.WorldRotation = Rotation.FromYaw( 45 );
	}

	/// <summary>
	/// Height sky attributes must reach the native 3D skybox view as well as the managed view.
	/// </summary>
	public static Scene BetterFogHeightSkybox()
	{
		var scene = BetterFog3DSkybox();
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.DistanceEnabled = false;
		fog.HeightColorSource = FogColorSource.SceneSky;
		fog.HeightColor = Color.White;
		return scene;
	}

	/// <summary>
	/// Rotates the visible sky and its fog color source together, exposing a reversed matrix convention.
	/// </summary>
	public static void RotateBetterFogSky( Scene scene )
	{
		scene.GetAllComponents<SkyBox2D>().Single().WorldRotation = Rotation.FromYaw( 90 );
	}

	/// <summary>
	/// The visible sky belongs to a separate 3D-skybox world; a misleading parent sky must not supply its fog color.
	/// </summary>
	public static Scene BetterFogSkyboxWorld()
	{
		var scene = BetterFogRotatedSky();
		using var scope = scene.Push();
		var parentSky = scene.GetAllComponents<SkyBox2D>().Single();
		parentSky.WorldRotation = Rotation.FromYaw( 180 );
		var owner = new GameObject( true, "Bare Skybox World" ).Components.Create<FogSkyWorldFixture>();
		owner.Initialize( parentSky.SkyMaterial );
		return scene;
	}

	/// <summary>
	/// Changes only the sky in the child world, leaving the parent's misleading cyan sky unchanged.
	/// </summary>
	public static void RotateBetterFogSkyboxWorld( Scene scene )
	{
		scene.GetAllComponents<FogSkyWorldFixture>().Single().Rotate();
	}

	/// <summary>
	/// Fog and both renderers must select the last visible sky, including camera tag filtering.
	/// </summary>
	public static Scene BetterFogSkySelection()
	{
		var scene = BetterFogRotatedSky();
		using var scope = scene.Push();
		var firstSky = scene.GetAllComponents<SkyBox2D>().Single();
		var lastSky = new GameObject( true, "Last Tagged Fog Sky" ).Components.Create<SkyBox2D>();
		lastSky.SkyIndirectLighting = false;
		lastSky.SkyMaterial = firstSky.SkyMaterial;
		lastSky.WorldRotation = Rotation.FromYaw( 90 );
		lastSky.Tags.Add( "fog-last-sky" );
		return scene;
	}

	/// <summary>
	/// Excludes the last sky so the original unrotated sky becomes the visible fog source.
	/// </summary>
	public static void ExcludeBetterFogLastSky( Scene scene )
	{
		scene.Camera.RenderExcludeTags.Add( "fog-last-sky" );
	}

	/// <summary>
	/// A finite volume blends halfway from the complete legacy fog composition to a new underwater-like color.
	/// </summary>
	public static Scene BetterFogBlend()
	{
		var scene = FogStage( out var camera );
		using var scope = scene.Push();
#pragma warning disable CS0618 // Retain legacy fog support for existing content.
		var gradient = new GameObject( true, "Legacy Gradient" ).Components.Create<GradientFog>();
#pragma warning restore CS0618
		gradient.Color = new Color( 0.9f, 0.35f, 0.15f, 0.8f );
		gradient.StartDistance = 30;
		gradient.EndDistance = 500;
		gradient.Height = 400;
#pragma warning disable CS0618 // Retain legacy fog support for existing content.
		var cubemap = new GameObject( true, "Legacy Cubemap" ).Components.Create<CubemapFog>();
#pragma warning restore CS0618
		// Give the legacy layer a controlled plain cube with a known directional color source.
		cubemap.Sky = Material.Load( "materials/skybox/skybox_day_01.vmat" ).CreateCopy();
		cubemap.Sky.Set( "g_tSkyTexture", SkyCube.Create() );
		cubemap.StartDistance = 100;
		cubemap.EndDistance = 900;

		var volumeObject = new GameObject( true, "Better Fog Volume" );
		volumeObject.WorldPosition = camera.WorldPosition;
		var volume = volumeObject.Components.Create<PostProcessVolume>();
		volume.BlendDistance = 100;
		var fog = volumeObject.Components.Create<Fog>();
		fog.AttenuationDistance = 180;
		fog.Color = new Color( 0.03f, 0.3f, 0.35f );
		return scene;
	}

	/// <summary>
	/// Equal orthographic depths receive equal distance fog even towards the edges of the image.
	/// </summary>
	public static Scene BetterFogOrthographic()
	{
		var scene = BetterFogDistance();
		var camera = scene.Camera;
		camera.Orthographic = true;
		camera.OrthographicHeight = 500;
		return scene;
	}

	/// <summary>
	/// A synthetic skyline rendered at sixteen-times scale and an offset tests height and distance fog in 3D skybox space.
	/// </summary>
	public static Scene BetterFog3DSkybox()
	{
		var scene = BetterFogHeight();
		using var scope = scene.Push();
		// SceneLab orbits around Z=30; a lower eye leaves the skyline visible above the foreground stage.
		scene.Camera.WorldPosition = new Vector3( -260, -200, 80 );
		var fog = scene.GetAllComponents<Fog>().Single();
		fog.AttenuationDistance = 1800;
		fog.HeightGradientLength = -250;
		fog.SkyAffect = 0.2f;

		var skyScene = new Scene();
		SceneFile file;
		try
		{
			using var skyScope = skyScene.Push();
			new GameObject( true, "Skyline Sky" ).Components.Create<SkyBox2D>();

			// Unlit geometry isolates fog coordinate conversion from the renderers' existing sky-lighting differences.
			for ( int i = 0; i < 7; i++ )
			{
				var position = new Vector3( 80, 60, 0 ) + new Vector3( -0.6f, 0.8f, 0 ) * ((i - 3) * 12);
				var tower = Prop( $"Skyline Tower {i}", "models/dev/box.vmdl", position, material: "materials/dev/black_unlit.vmat" );
				tower.WorldScale = new Vector3( 0.25f, 0.25f, 0.4f + (i % 3) * 0.3f );
				OnFloor( tower );
				// Skybox origin is -position/scale: this places the skyline's ground at main-world Z=0.
				tower.WorldPosition += Vector3.Up * (100.0f / 16.0f);
			}

			file = skyScene.CreateSceneFile();
			file.RegisterWeakResourceId( "scenelab_better_fog_skybox.scene" );
		}
		finally
		{
			skyScene.Destroy();
		}

		var skyboxObject = new GameObject( true, "Scaled Fog Skyline" );
		skyboxObject.WorldPosition = new Vector3( 0, 0, -100 );
		skyboxObject.WorldScale = 16;
		skyboxObject.Components.Create<Sandbox.Skybox3D>().SkyboxScene = file;
		return scene;
	}

	/// <summary>
	/// Additive sprites retain zero, half and full fog strength through their alpha attenuation.
	/// </summary>
	public static Scene BetterFogAdditiveSprites()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var cameraObject = new GameObject( true, "Camera" );
		cameraObject.WorldPosition = new Vector3( -300, 0, 30 );
		cameraObject.WorldRotation = Rotation.LookAt( Vector3.Forward );
		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.IsMainCamera = true;
		camera.FieldOfView = 70;
		camera.FovAxis = CameraComponent.Axis.Horizontal;
		camera.BackgroundColor = Color.Black;
		camera.ZNear = 5;
		camera.ZFar = 1000;

		// Solid white avoids downloaded or compiled sprite assets, and gives a uniform interior to sample.
		var sprite = Sprite.FromTexture( Texture.White );
		for ( int i = 0; i < 3; i++ )
		{
			var go = new GameObject( true, $"Additive Sprite Fog Strength {i * 0.5f}" );
			go.WorldPosition = Vector3.Up * 30 + camera.WorldRotation.Right * ((i - 1) * 90);
			var renderer = go.Components.Create<SpriteRenderer>();
			renderer.Sprite = sprite;
			renderer.Size = 60;
			renderer.Color = new Color( 0.4f, 0.6f, 0.8f );
			renderer.Additive = true;
			renderer.Opaque = false;
			renderer.Lighting = false;
			renderer.FogStrength = i * 0.5f;
		}

		var fog = cameraObject.Components.Create<Fog>();
		fog.Mode = FogMode.Distance;
		fog.DistanceRange = new Vector2( 0, 100 );
		fog.MaximumOpacity = 1;
		fog.Color = Color.Black;
		fog.SkyAffect = 0;
		return scene;
	}

	/// <summary>
	/// Each row compares line fog strengths zero, half and one across ordinary/additive and unlit/lit rendering.
	/// </summary>
	public static Scene BetterFogLines()
	{
		var scene = BetterFogAdditiveSprites();
		using var scope = scene.Push();
		foreach ( var sprite in scene.GetAllComponents<SpriteRenderer>().ToArray() )
		{
			sprite.GameObject.Destroy();
		}

		var sunObject = new GameObject( true, "Line Lighting" );
		sunObject.WorldRotation = Rotation.LookAt( Vector3.Forward );
		var sun = sunObject.Components.Create<DirectionalLight>();
		sun.LightColor = Color.White;
		sun.SkyColor = Color.White * 0.2f;
		sun.Shadows = false;

		var lightObject = new GameObject( true, "Line Front Light" );
		lightObject.WorldPosition = scene.Camera.WorldPosition;
		var light = lightObject.Components.Create<PointLight>();
		light.LightColor = Color.White * 2;
		light.Radius = 1000;
		light.Shadows = false;

		// The stock line material is fully metallic. Use a diffuse surface so lighting remains visible
		// without environment probes or a narrow specular highlight aligned with the camera.
		var material = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();
		material.Set( "g_tColor", Texture.White );
		material.Set( "g_tRma", Texture.Create( 1, 1 ).WithData( new byte[] { 180, 0, 255, 255 } ).Finish() );
		var texturing = TrailTextureConfig.Default;
		texturing.Material = material;

		// Top to bottom: unlit ordinary, unlit additive, lit ordinary, lit additive, then unlit/lit opaque + additive.
		// Uniform black full-opacity fog makes each row's expected linear intensities 1, 0.5 and 0.
		for ( int row = 0; row < 6; row++ )
		{
			for ( int column = 0; column < 3; column++ )
			{
				var center = Vector3.Up * (100 - row * 28) + scene.Camera.WorldRotation.Right * ((column - 1) * 90);
				var line = new GameObject( true, $"Fog Line Row {row} Strength {column * 0.5f}" ).Components.Create<LineRenderer>();
				line.UseVectorPoints = true;
				line.VectorPoints = [center - scene.Camera.WorldRotation.Right * 30, center + scene.Camera.WorldRotation.Right * 30];
				line.Face = SceneLineObject.FaceMode.Camera;
				line.Width = 8;
				line.Color = new Color( 0.4f, 0.6f, 0.8f );
				line.Texturing = texturing;
				line.Opaque = row >= 4;
				line.Additive = row % 2 == 1 || row >= 4;
				line.Lighting = row is 2 or 3 or 5;
				line.CastShadows = false;
				line.FogStrength = column * 0.5f;
			}
		}

		return scene;
	}

	/// <summary>
	/// A motionless water surface under a fixed half-opacity black fog exposes accidental double application.
	/// </summary>
	public static Scene BetterFogWater()
	{
		var scene = Stage( out var camera );
		using var scope = scene.Push();
		var material = Material.Load( "materials/water/water_blue.vmat" ).CreateCopy();
		material.Set( "g_tColor", Texture.White );
		material.Set( "NormalScale", 0.0f );
		material.Set( "BigWaveSize", 0.0f );
		material.Set( "BigWaveTime", 0.0f );
		material.Set( "Refraction", 0.0f );
		material.Set( "Translucency", 0.0f );
		material.Set( "g_fRoughness", 1.0f );
		material.Set( "g_fMetalness", 0.0f );
		material.Set( "g_fAmbientOcclusion", 1.0f );
		var water = Prop( "Half Fog Water", "models/dev/plane.vmdl", Vector3.Up * 2, scale: 2 );
		water.MaterialOverride = material;

		var fog = camera.GameObject.Components.Create<Fog>();
		fog.Mode = FogMode.Distance;
		fog.DistanceRange = new Vector2( 0, 50 );
		fog.MaximumOpacity = 0.5f;
		fog.Color = Color.Black;
		fog.SkyAffect = 0;
		return scene;
	}

	/// <summary>
	/// World-space text exercises the UI text shader with normal, premultiplied and multiply blending.
	/// </summary>
	public static Scene BetterFogText()
	{
		var scene = BetterFogAdditiveSprites();
		using var scope = scene.Push();
		foreach ( var sprite in scene.GetAllComponents<SpriteRenderer>().ToArray() )
		{
			sprite.GameObject.Destroy();
		}

		scene.Camera.BackgroundColor = new Color( 0.15f, 0.15f, 0.15f );
		scene.GetAllComponents<Fog>().Single().Color = new Color( 0.8f, 0.2f, 0.1f );
		BlendMode[] modes = [BlendMode.Normal, BlendMode.PremultipliedAlpha, BlendMode.Multiply];
		for ( int row = 0; row < modes.Length; row++ )
		{
			for ( int column = 0; column < 2; column++ )
			{
				var go = new GameObject( true, $"Fog Text {modes[row]} Strength {column}" );
				go.WorldPosition = Vector3.Up * (80 - row * 50) + scene.Camera.WorldRotation.Right * ((column - 0.5f) * 150);
				var text = go.Components.Create<TextRenderer>();
				text.TextScope = new TextRendering.Scope( "Fog", new Color( 0.8f, 0.7f, 0.6f, 0.5f ), 38, "Poppins", 600 );
				text.Billboard = TextRenderer.BillboardMode.Always;
				text.BlendMode = modes[row];
				text.FogStrength = column;
			}
		}

		return scene;
	}

	/// <summary>
	/// Colored but explicitly disabled fog must leave all three special static-overlay blend modes unchanged.
	/// </summary>
	public static Scene BetterFogOverlayOff()
	{
		return FogOverlayStage( false );
	}

	/// <summary>
	/// Enabled fog provides the visible positive control for the same static-overlay surfaces.
	/// </summary>
	public static Scene BetterFogOverlayOn()
	{
		return FogOverlayStage( true );
	}

	/// <summary>
	/// Three separated patches exercise Mod2x, Multiply and ModThenAdd over an opaque floor.
	/// </summary>
	static Scene FogOverlayStage( bool enabled )
	{
		var scene = Stage( out var camera, new Vector3( -260, -200, 350 ) );
		using var scope = scene.Push();
		int[] modes = [3, 5, 6];

		for ( int i = 0; i < modes.Length; i++ )
		{
			var position = new Vector3( -0.6f, 0.8f, 0 ) * ((i - 1) * 120);
			Prop( $"Overlay Backing {modes[i]}", "models/dev/plane.vmdl", position, new Color( 0.75f, 0.75f, 0.75f ), "materials/dev/primary_white.vmat", 1.1f );

			var material = Material.FromShader( "shaders/static_overlay.shader" ).CreateCopy();
			material.SetFeature( "F_LIT", 0 );
			material.SetFeature( "F_BLEND_MODE", modes[i] );
			// Set feature-dependent defaults explicitly after selecting the translucent permutation.
			material.Set( "g_tColor", Texture.White );
			material.Set( "g_tAmbientOcclusion", Texture.White );
			material.Set( "g_flOpacityScale", 1.0f );
			material.Set( "g_flModelTintAmount", 1.0f );
			material.Set( "g_flFadeExponent", 1.0f );
			material.Set( "g_vColorTint", new Vector3( 0.2f, 0.45f, 0.3f ) );
			if ( modes[i] == 6 )
			{
				material.Set( "g_tSelfIllumMask", Texture.White );
				material.Set( "g_vSelfIllumTint", Vector3.One );
				material.Set( "g_flSelfIllumScale", 1.0f );
				material.Set( "g_flSelfIllumAlbedoFactor", 1.0f );
			}

			var overlay = Prop( $"Fog Overlay Mode {modes[i]}", "models/dev/plane.vmdl", position + Vector3.Up * 0.1f, scale: 0.9f );
			overlay.MaterialOverride = material;
			overlay.RenderType = ModelRenderer.ShadowRenderType.Off;
		}

		// Native draws static overlays before dynamic opaque geometry. Static receivers keep the floor and
		// backing patches beneath their overlays in both renderers, as they would be in a compiled map.
		foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
		{
			// IsStatic does not refresh an existing scene object; recreate it with its final material and flags.
			renderer.Enabled = false;
			renderer.GameObject.IsStatic = true;
			renderer.Enabled = true;
		}

		var fog = camera.GameObject.Components.Create<Fog>();
		fog.FogEnabled = enabled;
		fog.Mode = FogMode.Distance;
		fog.DistanceRange = new Vector2( 0, 650 );
		// A saturated color above the overlay's red channel exposes Multiply changing RGB even at zero opacity.
		fog.Color = new Color( 0.95f, 0.08f, 0.65f );
		fog.MaximumOpacity = 0.8f;
		fog.SkyAffect = 0;
		return scene;
	}

	/// <summary>
	/// Removes the override entirely: the off preset must match this capture, while the on preset must visibly change.
	/// </summary>
	public static void RemoveBetterFogOverlay( Scene scene )
	{
		foreach ( var fog in scene.GetAllComponents<Fog>().ToArray() )
		{
			fog.Destroy();
		}
	}

	/// <summary>
	/// A clear second capture proves the fog was visible and that disabling its component clears camera state.
	/// </summary>
	public static void DisableBetterFog( Scene scene )
	{
		foreach ( var fog in scene.GetAllComponents<Fog>().ToArray() )
		{
			fog.Enabled = false;
		}
	}

	/// <summary>
	/// Fully suppresses both legacy fog layers through an explicit off override in a hard-edged finite volume.
	/// </summary>
	public static void ClearBetterFogBlend( Scene scene )
	{
		var volume = scene.GetAllComponents<PostProcessVolume>().Single();
		volume.BlendDistance = 0;
		volume.GameObject.Components.Get<Fog>().FogEnabled = false;
	}
}

/// <summary>
/// Owns the bare scene world used by the fog source fixture, including cleanup when SceneLab changes presets.
/// </summary>
file sealed class FogSkyWorldFixture : Component
{
	SceneSkybox3D skybox;
	SceneWorld world;
	SceneSkyBox sky;

	/// <summary>
	/// Creates the fixture owner through the component API.
	/// </summary>
	public FogSkyWorldFixture()
	{
	}

	/// <summary>
	/// Creates a map-style sky object with no SkyBox2D component for the parent scene query to find.
	/// </summary>
	internal void Initialize( Material material )
	{
		world = new SceneWorld();
		sky = new SceneSkyBox( world, material )
		{
			SkyTint = Color.White,
			Transform = global::Transform.Zero
		};
		sky.Tags.Add( "skybox" );
		skybox = new SceneSkybox3D( Scene.SceneWorld, world ) { Scale = 16 };
		skybox.Update();
	}

	/// <summary>
	/// Turns the actual rendered sky by ninety degrees, independently of the parent sky.
	/// </summary>
	internal void Rotate()
	{
		sky.Transform = sky.Transform.WithRotation( Rotation.FromYaw( 90 ) );
	}

	protected override void OnDisabled()
	{
		skybox?.Delete();
		world?.Delete();
		skybox = null;
		world = null;
		sky = null;
	}
}
