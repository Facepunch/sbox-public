#pragma warning disable CS0618 // Tests intentionally exercise legacy fog compatibility.

using Sandbox.Rendering;

namespace SceneTests.Components;

/// <summary>
/// Checks authored fog settings and serialization independently of camera and volume resolution.
/// </summary>
[TestClass]
public class FogAuthoringTests
{
	/// <summary>
	/// The height plane follows the complete world transform while its signed length remains in world units.
	/// </summary>
	[TestMethod]
	public void HeightGradientFollowsObjectAndKeepsItsOwnColor()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var parent = scene.CreateObject();
			parent.WorldPosition = new Vector3( 0, 0, 300 );
			var go = scene.CreateObject();
			go.Parent = parent;
			go.LocalPosition = new Vector3( 0, 0, 50 );
			var fog = go.Components.Create<Fog>();
			fog.DistanceEnabled = false;
			fog.HeightEnabled = true;
			fog.HeightOffset = 20;
			fog.HeightGradientLength = -100;
			fog.HeightExponent = 2;
			fog.HeightColor = Color.Red.WithAlpha( 0.4f );
			fog.Color = Color.Blue;

			var settings = fog.GetSettings( camera );
			Assert.AreEqual( 370f, settings.BaseHeight );
			Assert.AreEqual( -100f, settings.HeightFalloff );
			Assert.AreEqual( fog.HeightColor, settings.HeightColor );
			Assert.AreEqual( 2f, settings.HeightExponent );
			Assert.IsFalse( settings.DistanceEnabled );
			parent.WorldPosition += Vector3.Up * 200;
			Assert.AreEqual( 570f, fog.GetSettings( camera ).BaseHeight );

			var constants = settings.ToConstants( 16, 100 );
			Assert.AreEqual( -6.25f, constants.Height.y );
			Assert.AreEqual( new Vector2( 0, 2 ), constants.LayerParams );
			Assert.AreEqual( new Vector4( 1, 0, 0, 0.4f ), constants.HeightColor );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Invalid authored endpoints are sanitized before blending without rewriting the saved component values.
	/// </summary>
	[TestMethod]
	public void InvalidEndpointsAreClampedWithoutChangingAuthoring()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.StartDistance = -100;
			fog.DistanceRange = new Vector2( fog.StartDistance, -50 );
			fog.AttenuationDistance = 0;
			fog.FalloffExponent = -2;
			fog.HeightGradientLength = 0;
			fog.MaximumOpacity = 2;
			fog.SkyAffect = -1;
			fog.SkyBlur = 3;
			fog.MaxDistance = -1;
			fog.HeightOffset = -400;
			fog.Color = new Color( 2, 0.5f, 0.25f, 0 );

			var settings = fog.GetSettings( camera );
			Assert.AreEqual( 0.0f, settings.StartDistance );
			Assert.AreEqual( 0.0f, settings.EndDistance );
			Assert.IsTrue( settings.AttenuationDistance > 0 );
			Assert.IsTrue( settings.FalloffExponent > 0 );
			Assert.AreEqual( 0.0f, settings.HeightFalloff, "Zero length is a hard height boundary" );
			Assert.IsTrue( settings.MaxDistance > 0 );
			Assert.AreEqual( 1.0f, settings.MaximumOpacity );
			Assert.AreEqual( 0.0f, settings.SkyAffect );
			Assert.AreEqual( 1.0f, settings.SkyBlur );
			Assert.AreEqual( -400.0f, settings.BaseHeight );
			Assert.AreEqual( new Color( 2, 0.5f, 0.25f, 1 ), settings.Color, "HDR color is preserved and alpha does not control density" );
			Assert.AreEqual( -100.0f, fog.StartDistance );
			Assert.AreEqual( 0.0f, fog.AttenuationDistance );
			Assert.AreEqual( 2.0f, fog.MaximumOpacity );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Missing or unsupported custom skies use constant color; valid skies use the fog object's world orientation.
	/// </summary>
	[TestMethod]
	public void CustomSkyFallbackAndRotation()
	{
		var scene = new Scene();
		Material material = null;
		Texture cube = null;
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.CustomSky;
			fog.Color = Color.Red;
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			Assert.AreEqual( Color.Red, fog.GetSettings( camera ).Color );

			material = Material.Load( "materials/skybox/skybox_day_01.vmat" ).CreateCopy();
			material.Set( "g_tSkyTexture", Texture.White );
			fog.Sky = material;
			Assert.IsNull( fog.GetSettings( camera ).SkyTexture );
			Assert.AreEqual( 0.0f, fog.GetSettings( camera ).SkyContribution );

			cube = Texture.CreateCube( 1, 1 ).WithData( new byte[24] ).Finish();
			material.Set( "g_tSkyTexture", cube );
			fog.WorldRotation = Rotation.From( 20, 60, 10 );
			var settings = fog.GetSettings( camera );
			Assert.AreEqual( cube, settings.SkyTexture );
			Assert.AreEqual( 1.0f, settings.SkyContribution );
			Assert.AreEqual( fog.WorldRotation.Conjugate, settings.SkyRotation );
			Assert.AreEqual( Color.Red, settings.Color );

			material.Destroy();
			settings = fog.GetSettings( camera );
			Assert.IsNull( settings.SkyTexture );
			Assert.AreEqual( 0.0f, settings.SkyContribution );
			Assert.AreEqual( Color.Red, settings.Color );
		}
		finally
		{
			scene.Destroy();
			if ( material is { IsValid: true } )
				material.Destroy();
			cube?.Dispose();
		}
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
	/// Constant color ignores sky textures; selecting a sky source resolves it without additional overrides.
	/// </summary>
	[TestMethod]
	[DataRow( FogColorSource.SceneSky )]
	[DataRow( FogColorSource.CustomSky )]
	public void ColorSourceControlsSkyResolution( FogColorSource source )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var sky = scene.CreateObject().Components.Create<SkyBox2D>();
			AssignTestSkyCube( sky );
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.Constant;
			fog.Sky = sky.SkyMaterial;
			fog.AttenuationDistance = 750;

			var settings = fog.GetSettings( camera );
			Assert.IsNull( settings.SkyTexture, "Constant color must not resolve an unused sky texture" );
			Assert.AreEqual( 0.0f, settings.SkyContribution );
			Assert.AreEqual( 750.0f, settings.AttenuationDistance );

			fog.ColorSource = source;
			settings = fog.GetSettings( camera );
			Assert.AreEqual( sky.SkyTexture, settings.SkyTexture );
			Assert.AreEqual( 1.0f, settings.SkyContribution );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Height sky controls appear only when both enablement and color source require them.
	/// </summary>
	[TestMethod]
	[DataRow( false, FogColorSource.Constant, false, false )]
	[DataRow( false, FogColorSource.SceneSky, false, false )]
	[DataRow( false, FogColorSource.CustomSky, false, false )]
	[DataRow( true, FogColorSource.Constant, false, false )]
	[DataRow( true, FogColorSource.SceneSky, false, true )]
	[DataRow( true, FogColorSource.CustomSky, true, true )]
	public void HeightSkyControlsFollowSource( bool enabled, FogColorSource source, bool materialVisible, bool blurVisible )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.HeightEnabled = enabled;
			fog.HeightColorSource = source;
			var serialized = Game.TypeLibrary.GetSerializedObject( fog );
			Assert.AreEqual( materialVisible, serialized.GetProperty( nameof( Fog.HeightSky ) ).ShouldShow() );
			Assert.AreEqual( blurVisible, serialized.GetProperty( nameof( Fog.HeightSkyBlur ) ).ShouldShow() );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Height fog resolves its source independently, retaining alpha and rejecting unsafe sample reuse.
	/// </summary>
	[TestMethod]
	[DataRow( FogColorSource.SceneSky )]
	[DataRow( FogColorSource.CustomSky )]
	public void HeightSkySourceIsIndependent( FogColorSource source )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var sky = scene.CreateObject().Components.Create<SkyBox2D>();
			AssignTestSkyCube( sky );
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.DistanceEnabled = false;
			fog.HeightEnabled = true;
			fog.HeightColor = Color.White.WithAlpha( 0.4f );
			fog.ColorSource = FogColorSource.Constant;
			fog.SkyBlur = 0.5f;
			fog.SkyBlurDistanceEnabled = false;
			fog.HeightColorSource = source;
			fog.HeightSky = sky.SkyMaterial;
			var settings = fog.GetSettings( camera );
			Assert.IsNull( settings.SkyTexture );
			Assert.AreEqual( sky.SkyTexture, settings.HeightSkyTexture );
			Assert.AreEqual( 0.4f, settings.HeightColor.a );

			fog.ColorSource = source;
			fog.Sky = sky.SkyMaterial;
			settings = fog.GetSettings( camera );
			var constants = settings.ToConstants();
			Assert.AreEqual( 1.0f, constants.HeightSkyShared );
			var expectedIndex = g_pRenderDevice.GetTextureViewIndex( sky.SkyTexture.native,
				(byte)NativeEngine.RenderColorSpace.RENDER_SRGB, NativeEngine.RenderTextureDimension.RENDER_TEXTURE_DIMENSION_INVALID );
			Assert.AreEqual( expectedIndex, constants.HeightSkyTextureIndex );

			settings.HeightSkyRotation = Rotation.FromYaw( 90 );
			constants = settings.ToConstants();
			Assert.AreEqual( 0.0f, constants.HeightSkyShared );
			settings = fog.GetSettings( camera );
			settings.HeightSkyBlur = 0;
			constants = settings.ToConstants();
			Assert.AreEqual( 0.0f, constants.HeightSkyShared );
			settings = fog.GetSettings( camera );
			settings.HeightSkyEncoding = FogSkyEncoding.Rgbm;
			constants = settings.ToConstants();
			Assert.AreEqual( 0.0f, constants.HeightSkyShared );

			settings.HeightSkyTexture = Texture.White;
			constants = settings.ToConstants();
			Assert.AreEqual( -1, constants.HeightSkyTextureIndex );
			Assert.AreEqual( 0.0f, constants.HeightSky.x );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// A sky material's actual encoding feature is retained in fog settings and shader constants.
	/// </summary>
	[TestMethod]
	[DataRow( 0, FogSkyEncoding.Rgb )]
	[DataRow( 1, FogSkyEncoding.Rgb )]
	[DataRow( 3, FogSkyEncoding.Rgb )]
	[DataRow( 2, FogSkyEncoding.YCoCg )]
	[DataRow( 4, FogSkyEncoding.Rgbm )]
	[DataRow( 5, FogSkyEncoding.Rgbm )]
	[DataRow( 6, FogSkyEncoding.Rgb )]
	public void SkyMaterialEncodingReachesFogAttributes( int feature, FogSkyEncoding expected )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			var sky = scene.CreateObject().Components.Create<SkyBox2D>();
			AssignTestSkyCube( sky );
			sky.SkyMaterial.SetFeature( "F_TEXTURE_FORMAT2", feature );
			Assert.AreEqual( feature, sky.SkyMaterial.GetFeature( "F_TEXTURE_FORMAT2" ), "The fixture must use the requested material encoding" );
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.ColorSource = FogColorSource.CustomSky;
			fog.Sky = sky.SkyMaterial;
			fog.HeightColorSource = FogColorSource.CustomSky;
			fog.HeightSky = sky.SkyMaterial;

			var settings = fog.GetSettings( camera );
			Assert.AreEqual( expected, settings.SkyEncoding );
			Assert.AreEqual( expected, settings.HeightSkyEncoding );
			var constants = settings.ToConstants();
			Assert.AreEqual( (float)expected, constants.SkyFormat.x );
			Assert.AreEqual( (float)expected, constants.HeightSkyFormat.x );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// The component round-trips all settings and tolerates the removed Overrides field in existing scenes.
	/// </summary>
	[TestMethod]
	public void FogSettingsSerializeWithoutLegacyConversion()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var fog = scene.CreateObject().Components.Create<Fog>();
			fog.FogEnabled = false;
			fog.Mode = FogMode.Distance;
			fog.DistanceRange = new Vector2( 24, 1234 );
			fog.AttenuationDistance = 321;
			fog.FalloffExponent = 2.5f;
			fog.HeightEnabled = true;
			fog.DistanceEnabled = false;
			fog.HeightColor = Color.Red.WithAlpha( 0.4f );
			fog.HeightExponent = 2;
			fog.HeightOffset = -100;
			fog.HeightGradientLength = 200;
			fog.MaximumOpacity = 0.7f;
			fog.ColorSource = FogColorSource.CustomSky;
			fog.Sky = Material.Load( "materials/skybox/skybox_day_01.vmat" );
			fog.HeightColorSource = FogColorSource.CustomSky;
			fog.HeightSky = fog.Sky;
			fog.HeightSkyBlur = 0.2f;
			fog.Color = new Color( 0.2f, 0.4f, 0.6f );
			fog.SkyBlur = 0.8f;
			fog.SkyBlurDistanceEnabled = false;
			fog.SkyBlurDistanceRange = new( 100, 3000 );
			fog.SkyBlurFar = 0.1f;
			fog.SunLightingEnabled = false;
			fog.SunIntensity = 0.4f;
			fog.SunLobeWidth = 45;
			fog.SunStartDistance = 500;
			fog.SunSaturation = 1.5f;
			fog.SunFadeDistance = 2000;
			fog.SkyAffect = 0.2f;
			fog.MaxDistance = 50000;

			// Inspector properties are serialized directly so prefab overrides use the same names.
			var serialized = (System.Text.Json.Nodes.JsonObject)fog.Serialize();
			Assert.IsTrue( serialized.ContainsKey( nameof( Fog.DistanceRange ) ) );
			Assert.AreEqual( -100f, serialized[nameof( Fog.HeightOffset )].GetValue<float>() );
			Assert.AreEqual( 200f, serialized[nameof( Fog.HeightGradientLength )].GetValue<float>() );
			Assert.IsFalse( serialized.ContainsKey( "EndDistance" ) );
			Assert.IsFalse( serialized.ContainsKey( "BaseHeight" ) );
			Assert.IsFalse( serialized.ContainsKey( "HeightFalloff" ) );
			// Older scenes can still contain the removed flags field.
			Assert.IsFalse( serialized.ContainsKey( "Overrides" ) );
			serialized["Overrides"] = "Enabled, Distance, Sky";
			var copy = scene.CreateObject().Components.Create<Fog>( false );
			copy.DeserializeImmediately( serialized );
			Assert.AreEqual( new Vector2( 24, 1234 ), copy.DistanceRange );

			Assert.IsFalse( copy.FogEnabled );
			Assert.AreEqual( fog.Mode, copy.Mode );
			Assert.AreEqual( fog.StartDistance, copy.StartDistance );
			Assert.AreEqual( fog.DistanceRange, copy.DistanceRange );
			Assert.AreEqual( fog.AttenuationDistance, copy.AttenuationDistance );
			Assert.AreEqual( fog.FalloffExponent, copy.FalloffExponent );
			Assert.IsTrue( copy.HeightEnabled );
			Assert.IsFalse( copy.DistanceEnabled );
			Assert.AreEqual( fog.HeightColor, copy.HeightColor );
			Assert.AreEqual( fog.HeightExponent, copy.HeightExponent );
			Assert.AreEqual( fog.HeightOffset, copy.HeightOffset );
			Assert.AreEqual( fog.HeightGradientLength, copy.HeightGradientLength );
			Assert.AreEqual( fog.MaximumOpacity, copy.MaximumOpacity );
			Assert.AreEqual( fog.ColorSource, copy.ColorSource );
			Assert.AreEqual( fog.Sky.Name, copy.Sky.Name );
			Assert.AreEqual( fog.HeightColorSource, copy.HeightColorSource );
			Assert.AreEqual( fog.HeightSky.Name, copy.HeightSky.Name );
			Assert.AreEqual( fog.HeightSkyBlur, copy.HeightSkyBlur );
			Assert.AreEqual( fog.Color, copy.Color );
			Assert.AreEqual( fog.SkyBlur, copy.SkyBlur );
			Assert.AreEqual( fog.SkyBlurDistanceEnabled, copy.SkyBlurDistanceEnabled );
			Assert.AreEqual( fog.SkyBlurDistanceRange, copy.SkyBlurDistanceRange );
			Assert.AreEqual( fog.SkyBlurFar, copy.SkyBlurFar );
			Assert.AreEqual( fog.SunLightingEnabled, copy.SunLightingEnabled );
			Assert.AreEqual( fog.SunIntensity, copy.SunIntensity );
			Assert.AreEqual( fog.SunLobeWidth, copy.SunLobeWidth );
			Assert.AreEqual( fog.SunStartDistance, copy.SunStartDistance );
			Assert.AreEqual( fog.SunSaturation, copy.SunSaturation );
			Assert.AreEqual( fog.SunFadeDistance, copy.SunFadeDistance );
			Assert.AreEqual( fog.SkyAffect, copy.SkyAffect );
			Assert.AreEqual( fog.MaxDistance, copy.MaxDistance );
			Assert.IsFalse( scene.GetAllComponents<GradientFog>().Any() );
			Assert.IsFalse( scene.GetAllComponents<CubemapFog>().Any() );

			JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
			var oldData = Json.ParseToJsonObject( """
				{"__type":"Fog","StartDistance":24,"EndDistance":1234,"BaseHeight":-100,"HeightFalloff":200}
				""" );
			var upgraded = scene.CreateObject().Components.Create<Fog>( false );
			upgraded.DeserializeImmediately( oldData );
			Assert.AreEqual( new Vector2( 24, 1234 ), upgraded.DistanceRange );
			Assert.AreEqual( 24f, upgraded.StartDistance );
			Assert.AreEqual( -100f, upgraded.HeightOffset );
			Assert.AreEqual( 200f, upgraded.HeightGradientLength );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
