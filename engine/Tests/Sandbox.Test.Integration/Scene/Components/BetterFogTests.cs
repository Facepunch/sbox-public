#pragma warning disable CS0618 // Tests intentionally exercise legacy fog compatibility.

using Sandbox.Rendering;
using Sandbox.Volumes;

namespace SceneTests.Components;

/// <summary>
/// Moves the composed view without changing the camera component so fog selection can exercise the camera pipeline.
/// </summary>
public sealed class FogTestCameraOffsetModifier : Component, ICameraModifier
{
	/// <summary>
	/// Translation applied to each composed view.
	/// </summary>
	public Vector3 Offset { get; set; }

	/// <summary>
	/// Number of compositions, used to detect accidental recomposition during fog resolution.
	/// </summary>
	public int ModifyCalls { get; private set; }

	/// <inheritdoc />
	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		ModifyCalls++;
		view.Position += Offset;
	}
}

/// <summary>
/// Exercises fog through real scene updates, including legacy coexistence and camera/volume ownership.
/// </summary>
[TestClass]
public class BetterFogTests
{
	/// <summary>
	/// Height settings blend their own color and signed gradient independently of distance settings.
	/// </summary>
	[TestMethod]
	public void HeightSettingsBlendIndependentlyOfDistance()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var global = scene.CreateObject().Components.Create<Fog>();
			global.Color = Color.Blue;
			global.DistanceEnabled = false;
			global.HeightEnabled = true;
			global.HeightColor = Color.Red;
			global.HeightGradientLength = 100;
			var (volume, fog) = CreateVolume( scene, infinite: true );
			volume.BlendWeight = 0.5f;
			fog.Color = Color.Blue;
			fog.DistanceEnabled = false;
			fog.WorldPosition = new Vector3( 0, 0, 200 );
			fog.HeightEnabled = true;
			fog.HeightColor = Color.Green;
			fog.HeightGradientLength = -100;
			fog.HeightExponent = 3;
			scene.GameTick();

			var result = ResolveFog( camera ).Value;
			Assert.AreEqual( Color.Blue, result.Color );
			Assert.IsFalse( result.DistanceEnabled );
			Assert.AreEqual( Color.Lerp( Color.Red, Color.Green, 0.5f ), result.HeightColor );
			Assert.AreEqual( 100f, result.BaseHeight );
			Assert.AreEqual( 0f, result.HeightFalloff, "Opposite gradient directions may blend through a hard boundary" );
			Assert.AreEqual( 2f, result.HeightExponent );
		}
		finally
		{
			scene.Destroy();
		}
	}

	bool previousPostProcessing;

	/// <summary>
	/// Makes camera processing deterministic without leaving the process-wide switch changed.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previousPostProcessing = PostProcessSystem.EnablePostProcess;
		PostProcessSystem.EnablePostProcess = true;
	}

	/// <summary>
	/// Restores the rendering preference even when an assertion fails.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		PostProcessSystem.EnablePostProcess = previousPostProcessing;
	}

	/// <summary>
	/// Fog resolution and clearing do not depend on the post-process system's scene-stage order.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FogResolvesIndependentlyOfPostProcessOrder( bool postProcessFirst )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var (volume, fog) = CreateVolume( scene );
		fog.Color = Color.Blue;
		scene.GameTick();

		var postProcessStage = (Component.ISceneStage)scene.GetSystem<PostProcessSystem>();

		void UpdateSystems()
		{
			if ( postProcessFirst )
			{
				postProcessStage.End();
				camera.CopyPostProcessing( camera.SceneCamera );
			}
			else
			{
				camera.CopyPostProcessing( camera.SceneCamera );
				postProcessStage.End();
			}
		}

		camera.WorldPosition = new Vector3( 500, 0, 0 );
		UpdateSystems();
		Assert.IsNull( camera.SceneCamera.Fog );

		camera.WorldPosition = Vector3.Zero;
		UpdateSystems();

		Assert.AreEqual( Color.Blue, camera.SceneCamera.Fog.Value.Color );
		Assert.AreEqual( 1.0f, camera.SceneCamera.Fog.Value.Weight );

		// Running the fullscreen effect system alone must neither rebuild nor clear fog state.
		fog.Color = Color.Red;
		postProcessStage.End();
		Assert.AreEqual( Color.Blue, camera.SceneCamera.Fog.Value.Color );

		UpdateSystems();
		Assert.AreEqual( Color.Red, camera.SceneCamera.Fog.Value.Color );

		volume.Enabled = false;
		UpdateSystems();
		Assert.IsNull( camera.SceneCamera.Fog );
	}

	/// <summary>
	/// Builds the camera's current effects and reads the snapshot used by rendering.
	/// </summary>
	static FogSetup? ResolveFog( CameraComponent camera )
	{
		camera.CopyPostProcessing( camera.SceneCamera );
		return camera.SceneCamera.Fog;
	}

	/// <summary>
	/// Creates a local fog volume with no edge fade, or an infinite volume when requested.
	/// </summary>
	static (PostProcessVolume Volume, Fog Fog) CreateVolume( Scene scene, bool infinite = false )
	{
		var go = scene.CreateObject();
		var volume = go.Components.Create<PostProcessVolume>();
		volume.BlendDistance = 0;

		if ( infinite )
		{
			volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		}

		return (volume, go.Components.Create<Fog>());
	}

	/// <summary>
	/// Disk textures use dimensionless placeholders headlessly; a procedural cube retains its explicit descriptor.
	/// </summary>
	static void AssignTestSkyCube( SkyBox2D sky )
	{
		var cube = Texture.CreateCube( 1, 1 ).WithData( new byte[24] ).Finish();
		Assert.IsTrue( cube.Desc.IsCube, "The procedural fixture must provide a real cube descriptor even with the headless renderer" );
		sky.SkyMaterial = sky.SkyMaterial.CreateCopy();
		sky.SkyMaterial.Set( "g_tSkyTexture", cube );
	}

	/// <summary>
	/// Existing fog components retain both independent layers and do not opt into the new shader path.
	/// </summary>
	[TestMethod]
	public void LegacyComponentsLeaveNewFogUnset()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var gradient = scene.CreateObject().Components.Create<GradientFog>();
		gradient.Color = new Color( 0.2f, 0.4f, 0.8f, 0.6f );
		gradient.StartDistance = 75;
		gradient.EndDistance = 1250;
		gradient.Height = 300;
		gradient.WorldPosition = new Vector3( 0, 0, 50 );
		var cubemap = scene.CreateObject().Components.Create<CubemapFog>();
		cubemap.Sky = Material.Load( "materials/skybox/skybox_day_01.vmat" );
		cubemap.StartDistance = 30;
		cubemap.EndDistance = 600;
		cubemap.Tint = Color.Red;

		scene.GameTick();
		camera.CopyPostProcessing( camera.SceneCamera );

		Assert.IsNull( ResolveFog( camera ) );
		Assert.IsNull( camera.SceneCamera.Fog );
		Assert.IsTrue( scene.SceneWorld.GradientFog.Enabled );
		Assert.AreEqual( 75.0f, scene.SceneWorld.GradientFog.StartDistance );
		Assert.AreEqual( 1250.0f, scene.SceneWorld.GradientFog.EndDistance );
		Assert.AreEqual( 50.0f, scene.SceneWorld.GradientFog.StartHeight );
		Assert.AreEqual( 350.0f, scene.SceneWorld.GradientFog.EndHeight );
		Assert.AreEqual( 0.6f, scene.SceneWorld.GradientFog.MaximumOpacity );
		Assert.AreEqual( gradient.Color.WithAlpha( 1 ), scene.SceneWorld.GradientFog.Color );
		Assert.IsTrue( camera.SceneCamera.CubemapFog.Enabled );
		Assert.AreEqual( 30.0f, camera.SceneCamera.CubemapFog.StartDistance );
		Assert.AreEqual( 600.0f, camera.SceneCamera.CubemapFog.EndDistance );
		Assert.AreEqual( Color.Red, camera.SceneCamera.CubemapFog.Tint );
	}

	/// <summary>
	/// A finite hard-edged fog volume affects each camera independently and honors its explicit anchor.
	/// </summary>
	[TestMethod]
	public void FiniteVolumeUsesEachCameraAndItsAnchor()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var inside = scene.CreateObject().Components.Create<CameraComponent>();
		var outside = scene.CreateObject().Components.Create<CameraComponent>();
		outside.WorldPosition = new Vector3( 500, 0, 0 );
		var (volume, fog) = CreateVolume( scene );
		fog.Color = Color.Blue;

		scene.GameTick();

		Assert.IsNotNull( ResolveFog( inside ) );
		Assert.AreEqual( 1.0f, ResolveFog( inside ).Value.Weight );
		Assert.IsNull( ResolveFog( outside ) );
		Assert.AreEqual( volume.BlendWeight, volume.GetWeight( Vector3.Zero ), "Zero blend distance applies the full authored weight inside the volume" );

		outside.PostProcessAnchor = inside.GameObject;
		scene.GameTick();
		Assert.AreEqual( Color.Blue, ResolveFog( outside ).Value.Color );

		outside.PostProcessAnchor = null;
		inside.WorldPosition = new Vector3( 500, 0, 0 );
		scene.GameTick();
		Assert.IsNull( ResolveFog( inside ), "Leaving a volume clears its previous camera state" );
		Assert.IsNull( ResolveFog( outside ) );
	}

	/// <summary>
	/// Nested volumes contribute exactly once, and disabling their owner never promotes their fog globally.
	/// </summary>
	[TestMethod]
	public void NestedAndDisabledOwnersDoNotLeakFog()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var outer = scene.CreateObject().Components.Create<PostProcessVolume>();
		outer.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		var (inner, fog) = CreateVolume( scene, infinite: true );
		inner.GameObject.Parent = outer.GameObject;
		inner.BlendWeight = 0.5f;
		fog.DistanceRange = new Vector2( fog.StartDistance, 1234 );

		scene.GameTick();
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.Weight, 0.0001f, "The outer volume must not collect the inner volume's fog again" );
		Assert.AreEqual( 1234.0f, ResolveFog( camera ).Value.EndDistance, "A first layer fades its rendered result, without fading its authored settings a second time" );

		inner.Enabled = false;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ), "A disabled structural owner blocks collection by its active parent" );

		inner.Enabled = true;
		inner.GameObject.Parent = camera.GameObject;
		scene.GameTick();
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.Weight, 0.0001f, "A camera must not collect fog owned by a child volume" );

		inner.Enabled = false;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ) );
	}

	/// <summary>
	/// Higher priority volumes blend their complete settings with the preceding layer.
	/// </summary>
	[TestMethod]
	public void PriorityAndCompleteSettingsCompose()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var global = scene.CreateObject().Components.Create<Fog>();
		global.Color = Color.Red;
		global.DistanceRange = new Vector2( global.StartDistance, 100 );
		var (lowVolume, lowFog) = CreateVolume( scene, infinite: true );
		lowFog.Color = Color.Blue;
		lowFog.DistanceRange = new Vector2( lowFog.StartDistance, 300 );
		var (highVolume, highFog) = CreateVolume( scene, infinite: true );
		highVolume.Priority = 5;
		highVolume.BlendWeight = 0.5f;
		highFog.Color = Color.Green;
		highFog.DistanceRange = new Vector2( highFog.StartDistance, 700 );

		scene.GameTick();
		Assert.AreEqual( 500.0f, ResolveFog( camera ).Value.EndDistance, 0.001f );
		Assert.AreEqual( Color.Lerp( Color.Blue, Color.Green, 0.5f ), ResolveFog( camera ).Value.Color );

		lowVolume.Priority = 10;
		scene.GameTick();
		Assert.AreEqual( 300.0f, ResolveFog( camera ).Value.EndDistance, 0.001f );
		Assert.AreEqual( Color.Blue, ResolveFog( camera ).Value.Color );
	}

	/// <summary>
	/// Explicitly turning fog off suppresses both legacy layers; disabling the component instead inherits them.
	/// </summary>
	[TestMethod]
	public void ExplicitOffDiffersFromDisabledComponent()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		scene.CreateObject().Components.Create<GradientFog>();
		var (volume, fog) = CreateVolume( scene, infinite: true );
		fog.FogEnabled = false;

		scene.GameTick();
		Assert.IsNotNull( ResolveFog( camera ) );
		Assert.IsFalse( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 1.0f, ResolveFog( camera ).Value.Weight );
		Assert.IsTrue( scene.SceneWorld.GradientFog.Enabled, "A camera override must not mutate shared world fog" );

		fog.Enabled = false;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ) );
		Assert.IsTrue( scene.SceneWorld.GradientFog.Enabled );
	}

	/// <summary>
	/// All disable paths clear camera overrides, including the global post-processing early return.
	/// </summary>
	[TestMethod]
	public void DisablingPostProcessingClearsCameraFog()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		CreateVolume( scene, infinite: true );

		scene.GameTick();
		Assert.IsNotNull( ResolveFog( camera ) );

		camera.EnablePostProcessing = false;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ) );

		camera.EnablePostProcessing = true;
		scene.GameTick();
		Assert.IsNotNull( ResolveFog( camera ) );

		PostProcessSystem.EnablePostProcess = false;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ) );
		camera.CopyPostProcessing( camera.SceneCamera );
		Assert.IsNull( camera.SceneCamera.Fog );
	}

	/// <summary>
	/// Scene-sky color follows each camera's visibility filter and does not alter attenuation or sky coverage.
	/// </summary>
	[TestMethod]
	public void SkyColorSourceRespectsCameraVisibility()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var visibleCamera = scene.CreateObject().Components.Create<CameraComponent>();
		var excludedCamera = scene.CreateObject().Components.Create<CameraComponent>();
		excludedCamera.RenderExcludeTags.Add( "skybox" );
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		AssignTestSkyCube( sky );
		sky.Tint = new Color( 0.5f, 0.25f, 1 );
		var fog = scene.CreateObject().Components.Create<Fog>();
		fog.ColorSource = FogColorSource.SceneSky;
		fog.HeightColorSource = FogColorSource.SceneSky;
		fog.HeightColor = Color.White.WithAlpha( 0.4f );
		fog.Color = new Color( 0.8f, 0.8f, 0.8f );
		fog.AttenuationDistance = 750;
		fog.SkyAffect = 0;

		scene.GameTick();

		Assert.IsNotNull( ResolveFog( visibleCamera ).Value.SkyTexture );
		Assert.AreEqual( sky.SkyTexture, ResolveFog( visibleCamera ).Value.SkyTexture );
		Assert.AreEqual( sky.SkyTexture, ResolveFog( visibleCamera ).Value.HeightSkyTexture );
		Assert.AreEqual( new Color( 0.5f, 0.25f, 1, 0.4f ), ResolveFog( visibleCamera ).Value.HeightColor );
		Assert.IsNull( ResolveFog( excludedCamera ).Value.HeightSkyTexture );
		Assert.AreEqual( fog.HeightColor, ResolveFog( excludedCamera ).Value.HeightColor );
		Assert.AreEqual( new Color( 0.4f, 0.2f, 0.8f ), ResolveFog( visibleCamera ).Value.Color );
		Assert.IsNull( ResolveFog( excludedCamera ).Value.SkyTexture );
		Assert.AreEqual( 0.0f, ResolveFog( excludedCamera ).Value.SkyContribution, "A missing cubemap falls back to constant color" );
		Assert.AreEqual( fog.Color, ResolveFog( excludedCamera ).Value.Color );
		Assert.AreEqual( 750.0f, ResolveFog( visibleCamera ).Value.AttenuationDistance );
		Assert.AreEqual( 750.0f, ResolveFog( excludedCamera ).Value.AttenuationDistance );
		Assert.AreEqual( 0.0f, ResolveFog( visibleCamera ).Value.SkyAffect );

		var constants = ResolveFog( visibleCamera ).Value.ToConstants();
		var expectedIndex = g_pRenderDevice.GetTextureViewIndex( sky.SkyTexture.native,
			(byte)NativeEngine.RenderColorSpace.RENDER_SRGB, NativeEngine.RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );
		Assert.AreEqual( expectedIndex, constants.SkyTextureIndex );
		Assert.AreEqual( sky.SkyTexture.Desc.IsArray ? 1f : 0f, constants.SkyFormat.y );

		var unsupported = ResolveFog( visibleCamera ).Value;
		unsupported.SkyTexture = Texture.White;
		constants = unsupported.ToConstants();
		Assert.AreEqual( -1, constants.SkyTextureIndex );
	}

	/// <summary>
	/// A scene sky source follows the sky layer's literal skybox gate as well as the sky object's own tag filter.
	/// </summary>
	[TestMethod]
	public void SkyColorSourceHonorsSkyLayerIncludeTags()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		AssignTestSkyCube( sky );
		sky.Tags.Add( "customsky" );
		var fog = scene.CreateObject().Components.Create<Fog>();
		fog.ColorSource = FogColorSource.SceneSky;

		camera.RenderTags.Add( "world" );
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ).Value.SkyTexture );

		camera.RenderTags.Add( "skybox" );
		scene.GameTick();
		Assert.AreEqual( sky.SkyTexture, ResolveFog( camera ).Value.SkyTexture );

		camera.RenderTags = new();
		camera.RenderTags.Add( "customsky" );
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ).Value.SkyTexture, "Matching a sky object's custom tag does not bypass the renderer's skybox layer gate" );

		camera.RenderTags.Add( "skybox" );
		camera.RenderExcludeTags.Add( "skybox" );
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ).Value.SkyTexture );
	}

	/// <summary>
	/// Height sky fades independently of distance color when a volume blends from a constant source.
	/// </summary>
	[TestMethod]
	public void HeightSkyVolumeBlendsIndependently()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		AssignTestSkyCube( sky );
		var global = scene.CreateObject().Components.Create<Fog>();
		global.ColorSource = FogColorSource.Constant;
		global.HeightEnabled = true;
		global.HeightColor = Color.Red.WithAlpha( 0.2f );
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = 0.5f;
		fog.HeightEnabled = true;
		fog.HeightColorSource = FogColorSource.CustomSky;
		fog.HeightSky = sky.SkyMaterial;
		fog.HeightColor = Color.Blue.WithAlpha( 0.6f );
		fog.ColorSource = FogColorSource.Constant;
		scene.GameTick();

		var resolved = ResolveFog( camera ).Value;
		Assert.AreEqual( sky.SkyTexture, resolved.HeightSkyTexture );
		Assert.AreEqual( 0.5f, resolved.HeightSkyContribution );
		Assert.AreEqual( Color.Lerp( global.HeightColor, fog.HeightColor, 0.5f ), resolved.HeightColor );
		Assert.IsNull( resolved.SkyTexture );
		Assert.AreEqual( Color.White, resolved.Color );
	}

	/// <summary>
	/// Distinct sky textures switch with their encoding and orientation at the dominant layer while tint continues blending.
	/// </summary>
	[TestMethod]
	[DataRow( 0.49f )]
	[DataRow( 0.51f )]
	public void DistinctSkyVolumesSelectDominantTextureAndOrientation( float weight )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var firstSky = scene.CreateObject().Components.Create<SkyBox2D>();
		var secondSky = scene.CreateObject().Components.Create<SkyBox2D>();
		AssignTestSkyCube( firstSky );
		AssignTestSkyCube( secondSky );
		Assert.AreNotEqual( firstSky.SkyTexture, secondSky.SkyTexture );
		firstSky.SkyMaterial.SetFeature( "F_TEXTURE_FORMAT2", 2 );
		secondSky.SkyMaterial.SetFeature( "F_TEXTURE_FORMAT2", 4 );
		var global = scene.CreateObject().Components.Create<Fog>();
		global.ColorSource = FogColorSource.CustomSky;
		global.Sky = firstSky.SkyMaterial;
		global.Color = Color.Red;
		global.WorldRotation = Rotation.From( 20, 10, 0 );
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = weight;
		fog.ColorSource = FogColorSource.CustomSky;
		fog.Sky = secondSky.SkyMaterial;
		fog.Color = Color.Blue;
		fog.WorldRotation = Rotation.From( 0, 90, 0 );

		scene.GameTick();
		var resolved = ResolveFog( camera ).Value;
		var dominant = weight < 0.5f ? global : fog;
		Assert.AreEqual( dominant.Sky.GetTexture( "g_tSkyTexture" ), resolved.SkyTexture );
		Assert.AreEqual( weight < 0.5f ? FogSkyEncoding.YCoCg : FogSkyEncoding.Rgbm, resolved.SkyEncoding );
		Assert.IsTrue( resolved.SkyRotation.Distance( dominant.WorldRotation.Conjugate ) < 0.001f, "Distinct textures retain the chosen source's orientation rather than blending incompatible sky frames" );
		Assert.AreEqual( 1 - weight, resolved.Color.r, 0.0001f );
		Assert.AreEqual( weight, resolved.Color.b, 0.0001f );
		Assert.AreEqual( 1.0f, resolved.SkyContribution );
	}

	/// <summary>
	/// A shared cubemap with different decoding is a distinct color source and switches only at the dominant layer.
	/// </summary>
	[TestMethod]
	[DataRow( 0.0f )]
	[DataRow( 0.01f )]
	[DataRow( 0.49f )]
	[DataRow( 0.51f )]
	public void SharedSkyTextureWithDifferentEncodingUsesDominantSource( float weight )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		AssignTestSkyCube( sky );
		sky.SkyMaterial.SetFeature( "F_TEXTURE_FORMAT2", 2 );
		var incomingMaterial = sky.SkyMaterial.CreateCopy();
		incomingMaterial.SetFeature( "F_TEXTURE_FORMAT2", 4 );
		Assert.AreEqual( sky.SkyTexture, incomingMaterial.GetTexture( "g_tSkyTexture" ) );
		var global = scene.CreateObject().Components.Create<Fog>();
		global.ColorSource = FogColorSource.CustomSky;
		global.Sky = sky.SkyMaterial;
		global.WorldRotation = Rotation.FromYaw( 15 );
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = weight;
		fog.ColorSource = FogColorSource.CustomSky;
		fog.Sky = incomingMaterial;
		fog.WorldRotation = Rotation.FromYaw( 90 );

		scene.GameTick();
		var resolved = ResolveFog( camera ).Value;
		Assert.AreEqual( sky.SkyTexture, resolved.SkyTexture );
		Assert.AreEqual( weight < 0.5f ? FogSkyEncoding.YCoCg : FogSkyEncoding.Rgbm, resolved.SkyEncoding,
			"Entering a volume must not immediately replace the dominant sky source's decoder" );
		var dominant = weight < 0.5f ? global : fog;
		Assert.IsTrue( resolved.SkyRotation.Distance( dominant.WorldRotation.Conjugate ) < 0.001f,
			"Sources with incompatible decoding must keep the dominant source's complete sampling frame" );
	}

	/// <summary>
	/// The last component supplies the volume's complete settings, with its spatial influence applied once.
	/// </summary>
	[TestMethod]
	public void MultipleFogComponentsUseTheirVolumeWeightOnce()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		scene.CreateObject().Components.Create<GradientFog>().Color = Color.Blue;
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = 0.5f;

		static void Configure( Fog target )
		{
			target.Color = Color.Red;
			target.AttenuationDistance = 750;
			target.MaximumOpacity = 0.8f;
			target.HeightEnabled = true;
			target.HeightOffset = 10;
			target.HeightGradientLength = 200;
			target.SkyAffect = 0.25f;
			target.MaxDistance = 10000;
		}

		Configure( fog );
		scene.GameTick();
		var baseline = ResolveFog( camera ).Value;
		Assert.AreEqual( 0.5f, baseline.Weight );

		fog.Color = Color.Green;
		fog.AttenuationDistance = 2000;
		fog.HeightEnabled = false;
		var child = scene.CreateObject();
		child.Parent = volume.GameObject;
		var second = child.Components.Create<Fog>();
		Configure( second );
		scene.GameTick();
		var resolved = ResolveFog( camera ).Value;

		Assert.AreEqual( baseline.Weight, resolved.Weight, 0.0001f, "The owner's influence must not increase with its number of Fog components" );
		Assert.AreEqual( baseline.Color, resolved.Color );
		Assert.AreEqual( baseline.AttenuationDistance, resolved.AttenuationDistance );
		Assert.AreEqual( baseline.MaximumOpacity, resolved.MaximumOpacity );
		Assert.AreEqual( baseline.HeightEnabled, resolved.HeightEnabled );
		Assert.AreEqual( baseline.HeightContribution, resolved.HeightContribution );
		Assert.AreEqual( baseline.BaseHeight, resolved.BaseHeight );
		Assert.AreEqual( baseline.HeightFalloff, resolved.HeightFalloff );
		Assert.AreEqual( baseline.SkyAffect, resolved.SkyAffect );
		Assert.AreEqual( baseline.MaxDistance, resolved.MaxDistance );
	}

	/// <summary>
	/// Finite invalid authored values behave like their sanitized endpoints before a volume blends with surrounding fog.
	/// </summary>
	[TestMethod]
	public void FiniteInvalidFogEndpointsBlendLikeClampedValues()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var global = scene.CreateObject().Components.Create<Fog>();
		global.StartDistance = 100;
		global.DistanceRange = new Vector2( global.StartDistance, 2000 );
		global.FalloffExponent = 2;
		global.AttenuationDistance = 1000;
		global.HeightGradientLength = 800;
		global.SkyAffect = 0.4f;
		global.SkyBlur = 0.2f;
		global.SkyBlurDistanceEnabled = false;
		global.SunLightingEnabled = false;
		global.MaxDistance = 6000;
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = 0.25f;
		fog.StartDistance = -1000;
		fog.DistanceRange = new Vector2( fog.StartDistance, -1000 );
		fog.FalloffExponent = -1;
		fog.AttenuationDistance = -1000;
		fog.HeightExponent = -1;
		fog.SkyAffect = 2;
		fog.SkyBlur = -1;
		fog.SkyBlurDistanceEnabled = true;
		fog.SkyBlurDistanceRange = new( 100, 400 );
		fog.SkyBlurFar = 2;
		fog.SunLightingEnabled = true;
		fog.SunIntensity = 2;
		fog.SunLobeWidth = 100;
		fog.SunStartDistance = -100;
		fog.SunSaturation = 3;
		fog.SunFadeDistance = 20000;
		fog.MaxDistance = -1000;
		scene.GameTick();
		var invalid = ResolveFog( camera ).Value;

		fog.StartDistance = 0;
		fog.DistanceRange = new Vector2( fog.StartDistance, 0 );
		fog.FalloffExponent = 0.001f;
		fog.AttenuationDistance = 0.001f;
		fog.HeightExponent = 0.001f;
		fog.SkyAffect = 1;
		fog.SkyBlur = 0;
		fog.SkyBlurFar = 1;
		fog.MaxDistance = 0.001f;
		scene.GameTick();
		var sanitized = ResolveFog( camera ).Value;

		Assert.AreEqual( sanitized.AttenuationDistance, invalid.AttenuationDistance, 0.0001f, "Negative attenuation must not pull surrounding valid fog toward zero before it is clamped" );
		Assert.AreEqual( sanitized.HeightExponent, invalid.HeightExponent, 0.0001f );
		Assert.AreEqual( sanitized.StartDistance, invalid.StartDistance, 0.0001f );
		Assert.AreEqual( sanitized.EndDistance, invalid.EndDistance, 0.0001f );
		Assert.AreEqual( sanitized.FalloffExponent, invalid.FalloffExponent, 0.0001f );
		Assert.AreEqual( sanitized.SkyAffect, invalid.SkyAffect, 0.0001f );
		Assert.AreEqual( sanitized.SkyBlur, invalid.SkyBlur, 0.0001f );
		Assert.AreEqual( sanitized.SkyBlurFar, invalid.SkyBlurFar, 0.0001f );
		Assert.IsTrue( invalid.SunLightingEnabled );
		Assert.AreEqual( 0.5f, invalid.SunIntensity, 0.0001f, "Sun lighting fades from zero when the surrounding fog has it disabled" );
		Assert.AreEqual( 100f, invalid.SunLobeWidth, "The disabled layer must not change the enabled layer's lobe width" );
		Assert.AreEqual( sanitized.SunLobeWidth, invalid.SunLobeWidth );
		Assert.AreEqual( -100f, invalid.SunStartDistance );
		Assert.AreEqual( 3f, invalid.SunSaturation );
		Assert.AreEqual( 20000f, invalid.SunFadeDistance );
		Assert.AreEqual( 0.4f, invalid.SkyBlurFar, 0.0001f, "Fixed blur blends as equal near and far endpoints" );
		Assert.AreEqual( new Vector2( 100, 400 ), invalid.SkyBlurDistanceRange );
		Assert.AreEqual( sanitized.MaxDistance, invalid.MaxDistance, 0.0001f );
	}

	/// <summary>
	/// The shipped sky texture reaches the matching shader resource under the real renderer.
	/// </summary>
	[TestMethod]
	public void CoreSkyUsesMatchingResourceDimension()
	{
		if ( System.Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
		{
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to verify the shipped sky's real descriptor and shader binding." );
		}

		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var sky = scene.CreateObject().Components.Create<SkyBox2D>();
		scene.CreateObject().Components.Create<Fog>().ColorSource = FogColorSource.SceneSky;
		scene.GameTick();

		Assert.IsTrue( sky.SkyTexture.Desc.IsCube );
		Assert.AreEqual( sky.SkyTexture, ResolveFog( camera ).Value.SkyTexture );
		Assert.AreEqual( 1.0f, ResolveFog( camera ).Value.SkyContribution );
		var constants = ResolveFog( camera ).Value.ToConstants();
		var expectedIndex = g_pRenderDevice.GetTextureViewIndex( sky.SkyTexture.native,
			(byte)NativeEngine.RenderColorSpace.RENDER_SRGB, NativeEngine.RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );
		Assert.AreEqual( expectedIndex, constants.SkyTextureIndex );
		Assert.AreEqual( sky.SkyTexture.Desc.IsArray ? 1f : 0f, constants.SkyFormat.y );
	}

	/// <summary>
	/// Explicit fog-off volumes fade through opacity instead of snapping as their spatial weight changes.
	/// </summary>
	[TestMethod]
	public void FogOffVolumeFadesAndRestoresGlobalSettings()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var global = scene.CreateObject().Components.Create<Fog>();
		global.MaximumOpacity = 0.8f;
		global.Color = Color.Blue;
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = 0.25f;
		fog.MaximumOpacity = 0.8f;
		fog.Color = Color.Blue;
		fog.FogEnabled = false;

		scene.GameTick();
		Assert.IsTrue( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 0.8f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f );
		Assert.AreEqual( 0.75f, ResolveFog( camera ).Value.EnabledContribution.Value, 0.0001f );
		Assert.AreEqual( Color.Blue, ResolveFog( camera ).Value.Color );

		volume.BlendWeight = 1;
		scene.GameTick();
		Assert.IsFalse( ResolveFog( camera ).Value.Enabled );

		volume.BlendWeight = 0;
		scene.GameTick();
		Assert.IsTrue( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 0.8f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f );
	}

	/// <summary>
	/// Fog, height and directional color transitions through a partially weighted volume remain continuous.
	/// </summary>
	[TestMethod]
	public void EnabledAndHeightTransitionsKeepFractionalInfluence()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var global = scene.CreateObject().Components.Create<Fog>();
		global.FogEnabled = false;
		global.HeightEnabled = false;
		var (volume, fog) = CreateVolume( scene, infinite: true );
		volume.BlendWeight = 0.5f;
		fog.HeightEnabled = true;
		fog.MaximumOpacity = 0.8f;

		scene.GameTick();
		Assert.IsTrue( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 0.9f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f, "The authored cap blends halfway from the global default of 1 to the volume's 0.8" );
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.EnabledContribution.Value, 0.0001f );
		Assert.IsTrue( ResolveFog( camera ).Value.HeightEnabled );
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.HeightContribution.Value, 0.0001f );
		var constants = ResolveFog( camera ).Value.ToConstants();
		Assert.AreEqual( 0.9f, constants.Control.y, 0.0001f );
		Assert.AreEqual( 0.5f, constants.OpacityScale, 0.0001f, "The shader fades evaluated opacity separately from its authored cap" );

		global.HeightEnabled = true;
		fog.HeightEnabled = false;
		scene.GameTick();
		Assert.IsTrue( ResolveFog( camera ).Value.HeightEnabled, "Height density remains active while fading away" );
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.HeightContribution.Value, 0.0001f );

		global.Color = Color.Red;
		global.ColorDirection = Vector3.Forward;
		fog.Color = Color.Blue;
		fog.SecondaryColor = Color.Green;
		fog.ColorDirection = Vector3.Left;
		foreach ( var outgoingDirectional in new[] { false, true } )
		{
			global.DirectionalColorEnabled = outgoingDirectional;
			fog.DirectionalColorEnabled = !outgoingDirectional;
			foreach ( var weight in new[] { 0f, 0.01f, 0.49f, 0.5f, 0.51f, 0.99f, 1f } )
			{
				volume.BlendWeight = weight;
				scene.GameTick();
				var resolved = ResolveFog( camera ).Value;
				foreach ( var direction in new[] { Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right } )
				{
					var from = global.GetSettings( camera ).ToConstants( viewDirection: direction ).Color;
					var to = fog.GetSettings( camera ).ToConstants( viewDirection: direction ).Color;
					var expected = Vector3.Lerp( from, to, weight );
					var actual = resolved.ToConstants( viewDirection: direction ).Color;
					Assert.AreEqual( expected.x, actual.x, 0.0001f, $"Directional transition at weight {weight}, looking {direction}" );
					Assert.AreEqual( expected.y, actual.y, 0.0001f, $"Directional transition at weight {weight}, looking {direction}" );
					Assert.AreEqual( expected.z, actual.z, 0.0001f, $"Directional transition at weight {weight}, looking {direction}" );
				}
			}
		}
	}

	/// <summary>
	/// Complete layers blend enablement separately from their authored opacity caps.
	/// </summary>
	[TestMethod]
	public void CompleteLayersBlendEnablementSeparatelyFromOpacity()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		var global = scene.CreateObject().Components.Create<Fog>();
		global.MaximumOpacity = 0.8f;
		var (middle, middleFog) = CreateVolume( scene, infinite: true );
		middle.BlendWeight = 0.99f;
		middleFog.MaximumOpacity = 0.4f;
		middleFog.FogEnabled = false;
		var (top, topFog) = CreateVolume( scene, infinite: true );
		top.Priority = 5;
		topFog.MaximumOpacity = 0.6f;

		scene.GameTick();
		Assert.AreEqual( 0.6f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f );
		Assert.IsTrue( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 1.0f, ResolveFog( camera ).Value.EnabledContribution ?? 1, 0.0001f );

		middle.BlendWeight = 1;
		scene.GameTick();
		Assert.AreEqual( 0.6f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f, "A full-weight top layer supplies its own opacity regardless of the preceding disabled layer" );
		Assert.IsTrue( ResolveFog( camera ).Value.Enabled );
		Assert.AreEqual( 1.0f, ResolveFog( camera ).Value.EnabledContribution ?? 1, 0.0001f );

		top.BlendWeight = 0.5f;
		scene.GameTick();
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.MaximumOpacity, 0.0001f );
		Assert.AreEqual( 0.5f, ResolveFog( camera ).Value.EnabledContribution.Value, 0.0001f, "Enablement fades independently of the interpolated opacity cap" );
	}

	/// <summary>
	/// An editor volume's preview switch clears its camera fog without changing runtime volume settings.
	/// </summary>
	[TestMethod]
	public void EditorPreviewDisableClearsFog()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var (volume, fog) = CreateVolume( scene, infinite: true );
			scene.UpdateMainCamera();

			scene.EditorTick( 0.1f, 0.1f );
			Assert.IsNotNull( ResolveFog( camera ) );

			volume.EditorPreview = false;
			scene.EditorTick( 0.2f, 0.1f );
			Assert.IsNull( ResolveFog( camera ) );

			volume.EditorPreview = true;
			scene.EditorTick( 0.3f, 0.1f );
			Assert.IsNotNull( ResolveFog( camera ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Selecting a secondary camera previews its fog independently of the main camera, then clears it on deselection.
	/// </summary>
	[TestMethod]
	public void EditorSelectedCameraSuppliesViewportFog()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var main = scene.CreateObject().Components.Create<CameraComponent>();
			main.EnablePostProcessing = false;
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			var secondary = scene.CreateObject().Components.Create<CameraComponent>();
			secondary.IsMainCamera = false;
			secondary.GameObject.Components.Create<Fog>().Color = Color.Blue;
			scene.UpdateMainCamera();
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( secondary.GameObject );

			scene.EditorTick( 0.1f, 0.1f );
			Assert.AreSame( main, scene.Camera );
			viewport.InitializeRendering();
			Assert.AreEqual( Color.Blue, viewport.SceneCamera.Fog.Value.Color );
			Assert.IsNull( ResolveFog( main ), "Previewing another camera must not mutate the main camera's independent state" );

			scene.Editor.Selection.Clear();
			scene.EditorTick( 0.2f, 0.1f );
			Assert.IsNull( ResolveFog( main ) );
			viewport.InitializeRendering();
			Assert.IsNull( viewport.SceneCamera.Fog );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Selecting an inactive capture camera previews its current resolved fog just like an active camera.
	/// </summary>
	[TestMethod]
	public void EditorSelectedInactiveCameraSuppliesViewportFog()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			viewport.IsMainCamera = false;
			var selected = scene.CreateObject().Components.Create<CameraComponent>();
			selected.IsMainCamera = false;
			selected.GameObject.Components.Create<Fog>().Color = Color.Blue;
			selected.Enabled = false;
			scene.UpdateMainCamera();
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( selected.GameObject );

			scene.EditorTick( 0.1f, 0.1f );
			viewport.InitializeRendering();
			Assert.AreEqual( Color.Blue, ResolveFog( selected ).Value.Color );
			Assert.IsNotNull( viewport.SceneCamera.Fog, "Selecting an inactive camera must not replace its valid snapshot with an empty preview" );
			Assert.AreEqual( Color.Blue, viewport.SceneCamera.Fog.Value.Color );

			selected.EnablePostProcessing = false;
			scene.EditorTick( 0.2f, 0.1f );
			viewport.InitializeRendering();
			Assert.IsNull( viewport.SceneCamera.Fog, "The selected camera's explicit processing preference still controls its fog" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Selecting a camera with no fog intentionally clears the viewport's own fog until deselection.
	/// </summary>
	[TestMethod]
	public void EditorSelectedCameraWithoutFogOverridesViewportFog()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			viewport.GameObject.Components.Create<Fog>().Color = Color.Red;
			var selected = scene.CreateObject().Components.Create<CameraComponent>();
			scene.UpdateMainCamera();
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( selected.GameObject );

			scene.EditorTick( 0.1f, 0.1f );
			viewport.InitializeRendering();
			Assert.IsNull( viewport.SceneCamera.Fog, "An empty selected camera must not fall back to the viewport's own fog." );

			scene.Editor.Selection.Clear();
			scene.EditorTick( 0.2f, 0.1f );
			viewport.InitializeRendering();
			Assert.AreEqual( Color.Red, viewport.SceneCamera.Fog.Value.Color );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Gameplay cameras retain their own fog while the editor previews a different selected camera.
	/// </summary>
	[TestMethod]
	public void EditorPreviewDoesNotReplaceGameplayCameraFog()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var main = scene.CreateObject().Components.Create<CameraComponent>();
			main.GameObject.Components.Create<Fog>().Color = Color.Red;
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			var secondary = scene.CreateObject().Components.Create<CameraComponent>();
			secondary.IsMainCamera = false;
			secondary.GameObject.Components.Create<Fog>().Color = Color.Blue;
			scene.UpdateMainCamera();
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( secondary.GameObject );

			scene.EditorTick( 0.1f, 0.1f );
			main.CopyPostProcessing( main.SceneCamera );
			secondary.CopyPostProcessing( secondary.SceneCamera );

			viewport.InitializeRendering();
			Assert.AreEqual( Color.Blue, viewport.SceneCamera.Fog.Value.Color );
			Assert.AreEqual( Color.Red, main.SceneCamera.Fog.Value.Color );
			Assert.AreEqual( Color.Blue, secondary.SceneCamera.Fog.Value.Color );

			scene.Editor.Selection.Clear();
			scene.EditorTick( 0.2f, 0.1f );
			Assert.AreEqual( Color.Red, ResolveFog( main ).Value.Color );
			Assert.AreEqual( Color.Blue, ResolveFog( secondary ).Value.Color );

			var (volume, fog) = CreateVolume( scene );
			volume.WorldPosition = new Vector3( 500, 0, 0 );
			fog.Color = Color.Green;
			scene.Editor.Selection.Set( volume.GameObject );
			scene.EditorTick( 0.3f, 0.1f );

			viewport.InitializeRendering();
			Assert.AreEqual( Color.Green, viewport.SceneCamera.Fog.Value.Color );
			Assert.AreEqual( Color.Red, ResolveFog( main ).Value.Color, "A volume preview must not overwrite the gameplay camera used to evaluate its sky settings" );
			Assert.AreEqual( Color.Blue, ResolveFog( secondary ).Value.Color );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Selection previews the nearest volume rather than an ancestor camera, including when that volume's preview is disabled.
	/// </summary>
	[TestMethod]
	public void EditorSelectionUsesNearestFogOwner()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var main = scene.CreateObject().Components.Create<CameraComponent>();
			main.GameObject.Components.Create<Fog>().Color = Color.Blue;
			var viewport = scene.CreateObject().Components.Create<CameraComponent>();
			viewport.IsSceneEditorCamera = true;
			scene.UpdateMainCamera();
			var (volume, fog) = CreateVolume( scene );
			volume.GameObject.Parent = main.GameObject;
			volume.WorldPosition = new Vector3( 500, 0, 0 );
			volume.BlendWeight = 0.2f;
			fog.Color = Color.Red;
			var child = scene.CreateObject();
			child.Parent = volume.GameObject;
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( child );

			scene.EditorTick( 0.1f, 0.1f );
			viewport.InitializeRendering();
			Assert.AreEqual( Color.Red, viewport.SceneCamera.Fog.Value.Color );
			Assert.AreEqual( 1.0f, viewport.SceneCamera.Fog.Value.Weight, "Selected volumes preview fully even when the camera is outside their bounds" );

			volume.EditorPreview = false;
			scene.EditorTick( 0.2f, 0.1f );
			viewport.InitializeRendering();
			Assert.IsNull( viewport.SceneCamera.Fog, "Disabling the nearest volume preview must not select its ancestor camera instead" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Editor viewports resolve their own position and global fog even when the scene has no gameplay camera.
	/// </summary>
	[TestMethod]
	public void EditorViewportsResolveFogWithoutGameplayCamera()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var inside = scene.CreateObject().Components.Create<CameraComponent>();
			inside.IsSceneEditorCamera = true;
			var outside = scene.CreateObject().Components.Create<CameraComponent>();
			outside.IsSceneEditorCamera = true;
			outside.WorldPosition = new Vector3( 500, 0, 0 );
			scene.UpdateMainCamera();
			var (volume, fog) = CreateVolume( scene );
			fog.Color = Color.Blue;

			scene.EditorTick( 0.1f, 0.1f );
			Assert.IsNull( scene.Camera );
			Assert.AreEqual( Color.Blue, ResolveFog( inside ).Value.Color );
			Assert.IsNull( ResolveFog( outside ) );

			var global = scene.CreateObject().Components.Create<Fog>();
			global.Color = Color.Red;
			scene.EditorTick( 0.2f, 0.1f );
			Assert.AreEqual( Color.Blue, ResolveFog( inside ).Value.Color );
			Assert.AreEqual( Color.Red, ResolveFog( outside ).Value.Color );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Disabled secondary cameras still receive current fog when they are explicitly prepared for a capture.
	/// </summary>
	[TestMethod]
	public void DisabledCaptureCameraReceivesCurrentFog()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		scene.CreateObject().Components.Create<CameraComponent>();
		var capture = scene.CreateObject().Components.Create<CameraComponent>();
		capture.IsMainCamera = false;
		var global = scene.CreateObject().Components.Create<Fog>();
		global.Color = Color.Green;
		var (volume, fog) = CreateVolume( scene );
		fog.Color = Color.Blue;

		scene.GameTick();
		capture.InitializeRendering();
		Assert.AreEqual( Color.Blue, capture.SceneCamera.Fog.Value.Color );

		capture.Enabled = false;
		fog.Color = Color.Red;
		scene.GameTick();
		capture.InitializeRendering();
		Assert.AreEqual( Color.Red, capture.SceneCamera.Fog.Value.Color, "A disabled manual camera must not retain the previous frame's fog" );

		capture.WorldPosition = new Vector3( 500, 0, 0 );
		scene.GameTick();
		capture.InitializeRendering();
		Assert.AreEqual( Color.Green, capture.SceneCamera.Fog.Value.Color, "Moving an inactive capture camera must select its current surrounding fog" );

		global.Enabled = false;
		fog.Enabled = false;
		scene.GameTick();
		capture.InitializeRendering();
		Assert.IsNull( capture.SceneCamera.Fog, "Removing the last contributing layer clears an inactive capture camera too" );
	}

	/// <summary>
	/// Selecting a scene-sky volume keeps each editor viewport's independent visibility filter.
	/// </summary>
	[TestMethod]
	public void SelectedVolumePreviewResolvesSkyForEachViewport()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var excluded = scene.CreateObject().Components.Create<CameraComponent>();
			excluded.IsSceneEditorCamera = true;
			excluded.IsMainCamera = false;
			excluded.RenderExcludeTags.Add( "skybox" );
			var visible = scene.CreateObject().Components.Create<CameraComponent>();
			visible.IsSceneEditorCamera = true;
			visible.IsMainCamera = false;
			var sky = scene.CreateObject().Components.Create<SkyBox2D>();
			AssignTestSkyCube( sky );
			var (volume, fog) = CreateVolume( scene );
			volume.WorldPosition = new Vector3( 500, 0, 0 );
			fog.ColorSource = FogColorSource.SceneSky;
			scene.UpdateMainCamera();
			scene.Editor = new FogEditorSession( scene );
			scene.Editor.Selection.Set( volume.GameObject );

			scene.EditorTick( 0.1f, 0.1f );
			excluded.InitializeRendering();
			visible.InitializeRendering();

			Assert.IsNotNull( excluded.SceneCamera.Fog );
			Assert.IsNotNull( visible.SceneCamera.Fog );
			Assert.IsNull( excluded.SceneCamera.Fog.Value.SkyTexture );
			Assert.AreEqual( sky.SkyTexture, visible.SceneCamera.Fog.Value.SkyTexture, "A viewport must not borrow the first viewport's sky visibility during volume preview" );

			scene.Editor.Selection.Clear();
			scene.EditorTick( 0.2f, 0.1f );
			excluded.InitializeRendering();
			visible.InitializeRendering();
			Assert.IsNull( excluded.SceneCamera.Fog );
			Assert.IsNull( visible.SceneCamera.Fog );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Fog and ordinary effects select volumes using the composed view, with explicit anchors taking precedence.
	/// </summary>
	[TestMethod]
	public void FogVolumesFollowComposedCameraPositionAndAnchor()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( 500, 0, 0 );
		var modifier = scene.CreateObject().Components.Create<FogTestCameraOffsetModifier>();
		modifier.Offset = new Vector3( -500, 0, 0 );
		var (volume, fog) = CreateVolume( scene );
		fog.Color = Color.Blue;
		camera.GameObject.Components.Create<Tonemapping>().ExposureCompensation = 1;
		volume.GameObject.Components.Create<Tonemapping>().ExposureCompensation = 3;

		scene.GameTick();
		camera.InitializeRendering();
		Assert.AreEqual( Vector3.Zero, camera.SceneCamera.Position );
		Assert.IsNotNull( ResolveFog( camera ), "A modifier can move the rendered view inside a volume without moving the camera object" );
		Assert.AreEqual( Color.Blue, ResolveFog( camera ).Value.Color );
		Assert.AreEqual( 3f, camera.AutoExposure.Compensation, "Ordinary effects must select the same volume as fog after camera modifiers" );

		var anchor = scene.CreateObject();
		anchor.WorldPosition = new Vector3( 500, 0, 0 );
		camera.PostProcessAnchor = anchor;
		scene.GameTick();
		Assert.IsNull( ResolveFog( camera ), "An explicit anchor still takes precedence over the composed view" );
		Assert.AreEqual( 1f, camera.AutoExposure.Compensation, "An explicit anchor must also override ordinary effects' volume selection" );

		camera.PostProcessAnchor = null;
		scene.GameTick();
		Assert.IsNotNull( ResolveFog( camera ) );
		Assert.AreEqual( 3f, camera.AutoExposure.Compensation );
		var modifyCalls = modifier.ModifyCalls;
		camera.WorldPosition = new Vector3( 1000, 0, 0 );
		camera.InitializeRendering();
		Assert.AreEqual( camera.WorldPosition, camera.SceneCamera.Position );
		Assert.IsNull( ResolveFog( camera ), "Changing the raw transform after composition must use the same fallback as rendering" );
		Assert.AreEqual( modifyCalls, modifier.ModifyCalls, "Resolving fog must not compose the camera a second time" );
	}

	/// <summary>
	/// Supplies selection to the real editor-scene tick without opening editor windows or invoking document operations.
	/// </summary>
	internal sealed class FogEditorSession( Scene scene ) : Scene.ISceneEditorSession
	{
		/// <inheritdoc />
		Scene Scene.ISceneEditorSession.Scene => scene;
		/// <inheritdoc />
		bool Scene.ISceneEditorSession.HasUnsavedChanges { get; set; }
		/// <inheritdoc />
		SelectionSystem Scene.ISceneEditorSession.Selection { get; } = new();
		/// <inheritdoc />
		BaseFileSystem Scene.ISceneEditorSession.TransientFilesystem => null;

		/// <inheritdoc />
		void Scene.ISceneEditorSession.AddSelectionUndo()
		{
		}

		/// <inheritdoc />
		void Scene.ISceneEditorSession.OnEditLog( string name, object source )
		{
		}

		/// <inheritdoc />
		void Scene.ISceneEditorSession.FrameTo( in BBox box )
		{
		}

		/// <inheritdoc />
		void Scene.ISceneEditorSession.Save( bool forceSaveAs )
		{
		}

		/// <inheritdoc />
		void Scene.ISceneEditorSession.RecordChange( SerializedProperty property )
		{
		}

		/// <inheritdoc />
		void Scene.ISceneEditorSession.AddUndo( string name, System.Action undo, System.Action redo )
		{
		}

		/// <inheritdoc />
		ISceneUndoScope Scene.ISceneEditorSession.UndoScope( string name ) => null;

		/// <inheritdoc />
		System.Collections.Generic.IEnumerable<object> Scene.ISceneEditorSession.GetSelection() => ((Scene.ISceneEditorSession)this).Selection;
	}
}
