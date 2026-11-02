using Sandbox.Rendering;
using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// The source of color for a distance or height fog layer.
/// </summary>
[Expose]
public enum FogColorSource
{
	/// <summary>
	/// Use a constant colour.
	/// </summary>
	Constant,
	/// <summary>
	/// Sample the visible scene sky, including its tint and rotation and any active 3D skybox world.
	/// </summary>
	SceneSky,
	/// <summary>
	/// Sample the selected sky material, oriented using this component's rotation.
	/// </summary>
	CustomSky
}

/// <summary>
/// Authors a fog layer's distance, height and colour settings, with optional directional colour from a sky material.
/// Settings are resolved separately for each camera so scene sky colours follow that camera's visibility.
/// </summary>
[Title( "Fog" ), Category( "Rendering" ), Icon( "foggy" )]
public sealed partial class Fog : BasePostProcess<Fog>
{
	/// <inheritdoc />
	public override int ComponentVersion => 1;

	/// <summary>
	/// Move existing authored values to the properties edited by the inspector.
	/// </summary>
	[Expose, JsonUpgrader( typeof( Fog ), 1 )]
	static void UpgradeProperties( JsonObject json )
	{
		json[nameof( DistanceRange )] ??= Json.ToNode( new Vector2(
			json[nameof( StartDistance )]?.GetValue<float>() ?? 0,
			json["EndDistance"]?.GetValue<float>() ?? 4096 ) );

		if ( json["BaseHeight"] is { } height )
		{
			json[nameof( HeightOffset )] ??= height.DeepClone();
		}

		if ( json["HeightFalloff"] is { } gradient )
		{
			json[nameof( HeightGradientLength )] ??= gradient.DeepClone();
		}

		json.Remove( "EndDistance" );
		json.Remove( "BaseHeight" );
		json.Remove( "HeightFalloff" );
	}

	/// <summary>
	/// Whether this layer draws fog. False explicitly suppresses fog; disabling the component instead inherits surrounding fog.
	/// </summary>
	[Property]
	public bool FogEnabled { get; set; } = true;

	/// <summary>
	/// Whether distance fog contributes. Height fog can remain enabled independently.
	/// </summary>
	[Property, ToggleGroup( nameof( DistanceEnabled ), Label = "Distance" )]
	public bool DistanceEnabled { get; set; } = true;

	/// <summary>
	/// Distance uses a start/end ramp with a configurable exponent; exponential uses attenuation distance.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) )]
	public FogMode Mode { get; set; } = FogMode.Exponential;

	/// <summary>
	/// Measure distance radially, or along the camera's forward axis.
	/// </summary>
	[Property, Advanced, Group( nameof( DistanceEnabled ) )]
	public FogDistanceMetric DistanceMetric { get; set; }

	/// <summary>
	/// Distance from the camera before exponential fog starts, in engine units.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) ), ShowIf( nameof( Mode ), FogMode.Exponential ), Range( 0, 10000 )]
	public float StartDistance { get; set; }

	/// <summary>
	/// Clear and full-fog endpoints of the distance ramp, in engine units. Signed and reversed ranges are supported.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) ), Title( "Start / End" )]
	[ShowIf( nameof( Mode ), FogMode.Distance ), Step( 1 )]
	public Vector2 DistanceRange { get; set; } = new( 0, 4096 );

	/// <summary>
	/// Exponent of the artistic distance ramp.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) ), ShowIf( nameof( Mode ), FogMode.Distance ), Range( 0.1f, 8 )]
	public float FalloffExponent { get; set; } = 1;

	/// <summary>
	/// Distance removing approximately 63 percent of background light, in engine units.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) ), ShowIf( nameof( Mode ), FogMode.Exponential )]
	public float AttenuationDistance { get; set; } = 2000;

	/// <summary>
	/// Upper limit on distance fog opacity after applying the curve, independent of the colour's alpha.
	/// </summary>
	[Property, Group( nameof( DistanceEnabled ) ), Range( 0, 1 )]
	public float MaximumOpacity { get; set; } = 1;

	/// <summary>
	/// Draw a height gradient independently of distance fog.
	/// </summary>
	[Property, ToggleGroup( nameof( HeightEnabled ), Label = "Height Fog" )]
	public bool HeightEnabled { get; set; }

	/// <summary>
	/// Offset above the GameObject's world Z position where height fog reaches full opacity.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) )]
	public float HeightOffset { get; set; }

	/// <summary>
	/// World distance from full fog to clear. Positive fades upward; negative fades downward.
	/// Zero creates a hard boundary at HeightOffset.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Gradient Length" )]
	public float HeightGradientLength { get; set; } = 1024;

	/// <summary>
	/// Curve applied to the height gradient, independently of the distance curve.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Exponent" ), Range( 0.1f, 8 )]
	public float HeightExponent { get; set; } = 1;

	/// <summary>
	/// Constant color, or directional color sampled from a sky independently of distance fog.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Color Source" )]
	public FogColorSource HeightColorSource { get; set; }

	/// <summary>
	/// Sky material used by height fog's Custom Sky source. Missing textures use the constant color.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Sky" ), ShowIf( nameof( ShowHeightSkyMaterial ), true )]
	public Material HeightSky { get; set; }

	/// <summary>
	/// Blur applied to the height fog sky source, independently of distance fog.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Sky Blur" ), ShowIf( nameof( ShowHeightSkyBlur ), true ), Range( 0, 1 )]
	public float HeightSkyBlur { get; set; } = 0.5f;

	/// <summary>
	/// Show the custom material only while height fog uses it.
	/// </summary>
	[Expose, Hide]
	internal bool ShowHeightSkyMaterial => HeightEnabled && HeightColorSource == FogColorSource.CustomSky;

	/// <summary>
	/// Show sky blur only while height fog samples a sky.
	/// </summary>
	[Expose, Hide]
	internal bool ShowHeightSkyBlur => HeightEnabled && HeightColorSource != FogColorSource.Constant;

	/// <summary>
	/// Height fog color or sky tint. Alpha sets its maximum opacity independently of distance fog.
	/// </summary>
	[Property, Group( nameof( HeightEnabled ) ), Title( "Color" )]
	public Color HeightColor { get; set; } = Color.White;

	/// <summary>
	/// Constant colour, or directional colour sampled from a sky material.
	/// </summary>
	[Property, Group( "Color" )]
	public FogColorSource ColorSource { get; set; } = FogColorSource.SceneSky;

	/// <summary>
	/// Fog colour, or tint applied to the sky colour. Alpha does not control opacity.
	/// </summary>
	[Property, Group( "Color" )]
	public Color Color { get; set; } = Color.White;

	/// <summary>
	/// Blend between two fog colors using the camera's forward direction.
	/// </summary>
	[Property, Advanced, Group( "Color" )]
	public bool DirectionalColorEnabled { get; set; }

	/// <summary>
	/// Fog color when looking opposite ColorDirection.
	/// </summary>
	[Property, Advanced, Group( "Color" ), ShowIf( nameof( DirectionalColorEnabled ), true )]
	public Color SecondaryColor { get; set; } = Color.White;

	/// <summary>
	/// Direction of the primary color, relative to this object's rotation.
	/// </summary>
	[Property, Advanced, Group( "Color" ), ShowIf( nameof( DirectionalColorEnabled ), true )]
	public Vector3 ColorDirection { get; set; } = Vector3.Forward;

	/// <summary>
	/// Nonnegative color multiplier applied after directional blending in linear space.
	/// </summary>
	[Property, Advanced, Group( "Color" )]
	public float ColorScale { get; set; } = 1;

	/// <summary>
	/// Sky material used by Custom Sky. Missing textures fall back to the constant colour.
	/// </summary>
	[Property, Group( "Color" ), ShowIf( nameof( ColorSource ), FogColorSource.CustomSky )]
	public Material Sky { get; set; }

	/// <summary>
	/// Blur applied when sampling sky colours, or the near blur when the distance ramp is enabled.
	/// </summary>
	[Property, Group( "Color" ), HideIf( nameof( ColorSource ), FogColorSource.Constant ), Range( 0, 1 )]
	public float SkyBlur { get; set; } = 1;

	/// <summary>
	/// Vary sky blur with distance instead of using a fixed value.
	/// </summary>
	[Property, ToggleGroup( nameof( SkyBlurDistanceEnabled ), Label = "Blur Ramp" ), HideIf( nameof( ColorSource ), FogColorSource.Constant )]
	public bool SkyBlurDistanceEnabled { get; set; } = true;

	/// <summary>
	/// Distances where sky blur starts and finishes changing, in world units.
	/// </summary>
	[Property, Group( nameof( SkyBlurDistanceEnabled ) ), Title( "Start / End" ), MinMaxSlider( 0, 10000 )]
	public Vector2 SkyBlurDistanceRange { get; set; } = new( 0, 2000 );

	/// <summary>
	/// Sky blur at the end of the ramp. Values below SkyBlur sharpen with distance; values above it blur with distance.
	/// </summary>
	[Property, Group( nameof( SkyBlurDistanceEnabled ) ), Title( "Far Blur" ), Range( 0, 1 )]
	public float SkyBlurFar { get; set; }

	/// <summary>
	/// Add an unshadowed sun-facing glow to distance fog, independently of volumetric lighting.
	/// </summary>
	[Property, ToggleGroup( nameof( SunLightingEnabled ), Label = "Sun Lighting" )]
	public bool SunLightingEnabled { get; set; } = true;

	/// <summary>
	/// Multiplier for the dominant sun's color. Reduce this when the sky texture already contains a bright sun.
	/// </summary>
	[Property, Group( nameof( SunLightingEnabled ) ), Title( "Intensity" ), Range( 0, 4 )]
	public float SunIntensity { get; set; } = 1;

	/// <summary>
	/// Angle from the sun where the glow reaches half intensity, in degrees. Larger angles give a broader glow.
	/// </summary>
	[Property, Group( nameof( SunLightingEnabled ) ), Title( "Lobe Width" ), Range( 1, 45 )]
	public float SunLobeWidth { get; set; } = 35;

	/// <summary>
	/// Distance from the camera where the sun glow starts fading in, in world units.
	/// </summary>
	[Property, Group( nameof( SunLightingEnabled ) ), Title( "Start Distance" ), Range( 0, 10000 )]
	public float SunStartDistance { get; set; }

	/// <summary>
	/// Distance over which sun lighting smoothly fades in. Zero gives a hard start; negative values reverse the fade.
	/// </summary>
	[Property, Group( nameof( SunLightingEnabled ) ), Title( "Fade Distance" ), Range( -10000, 10000 )]
	public float SunFadeDistance { get; set; }

	/// <summary>
	/// Sun glow saturation. Zero is greyscale, one preserves the light's color, and higher values strengthen it.
	/// </summary>
	[Property, Group( nameof( SunLightingEnabled ) ), Title( "Saturation" ), Range( 0, 2 )]
	public float SunSaturation { get; set; } = 1;

	/// <summary>
	/// How strongly fog covers the sky, independently of using sky colours for fog.
	/// </summary>
	[Property, Group( "Sky" ), Range( 0, 1 )]
	public float SkyAffect { get; set; } = 1;

	/// <summary>
	/// Distance used to evaluate background fog where the sky has no finite surface depth.
	/// </summary>
	[Property, Hide]
	public float MaxDistance { get; set; } = 100000;

	/// <summary>
	/// Resolve the complete authored layer for this camera.
	/// </summary>
	internal FogSetup GetSettings( CameraComponent camera ) => GetSettings( Scene.SceneWorld, camera.RenderTags, camera.RenderExcludeTags );

	internal FogSetup GetSettings( SceneWorld world, ITagSet renderTags, ITagSet excludeTags )
	{
		var distanceSky = ResolveSky( world, renderTags, excludeTags, ColorSource, Sky, Color.WithAlpha( 1 ) );
		var heightSky = ResolveSky( world, renderTags, excludeTags, HeightColorSource, HeightSky, HeightColor.WithAlpha( HeightColor.a.Clamp( 0, 1 ) ) );

		return new FogSetup
		{
			Enabled = FogEnabled,
			DistanceEnabled = DistanceEnabled,
			Mode = Mode,
			DistanceMetric = DistanceMetric,
			Color = distanceSky.Color,
			DirectionalColorEnabled = DirectionalColorEnabled,
			SecondaryColor = SecondaryColor.WithAlpha( 1 ),
			ColorDirection = WorldRotation * ColorDirection,
			ColorScale = ColorScale,
			StartDistance = Mode == FogMode.Distance ? DistanceRange.x : MathF.Max( 0, StartDistance ),
			EndDistance = Mode == FogMode.Distance ? DistanceRange.y : MathF.Max( 0, DistanceRange.y ),
			FalloffExponent = MathF.Max( FalloffExponent, 0.001f ),
			AttenuationDistance = MathF.Max( AttenuationDistance, 0.001f ),
			MaximumOpacity = MaximumOpacity.Clamp( 0, 1 ),
			HeightEnabled = HeightEnabled,
			BaseHeight = WorldPosition.z + HeightOffset,
			HeightFalloff = HeightGradientLength,
			HeightExponent = MathF.Max( HeightExponent, 0.001f ),
			HeightColor = heightSky.Color,
			HeightSkyTexture = heightSky.Texture,
			HeightSkyEncoding = heightSky.Encoding,
			HeightSkyRotation = heightSky.Rotation,
			HeightSkyContribution = heightSky.Texture is null ? 0 : 1,
			HeightSkyBlur = HeightSkyBlur.Clamp( 0, 1 ),
			SkyTexture = distanceSky.Texture,
			SkyEncoding = distanceSky.Encoding,
			SkyContribution = distanceSky.Texture is null ? 0 : 1,
			SkyRotation = distanceSky.Rotation,
			SkyBlur = SkyBlur.Clamp( 0, 1 ),
			SkyBlurDistanceEnabled = SkyBlurDistanceEnabled,
			SkyBlurDistanceRange = new( MathF.Max( 0, SkyBlurDistanceRange.x ), MathF.Max( 0, SkyBlurDistanceRange.y ) ),
			SkyBlurFar = SkyBlurFar.Clamp( 0, 1 ),
			SunLightingEnabled = SunLightingEnabled,
			SunIntensity = SunIntensity,
			SunLobeWidth = SunLobeWidth,
			SunStartDistance = SunStartDistance,
			SunFadeDistance = SunFadeDistance,
			SunSaturation = SunSaturation,
			SkyAffect = SkyAffect.Clamp( 0, 1 ),
			MaxDistance = MathF.Max( MaxDistance, 0.001f ),
			Weight = 1
		};
	}

	/// <summary>
	/// Resolve a sky's texture, tint, encoding and orientation using this camera's visibility.
	/// </summary>
	(Color Color, Texture Texture, FogSkyEncoding Encoding, Rotation Rotation) ResolveSky( SceneWorld world, ITagSet renderTags, ITagSet excludeTags, FogColorSource source, Material customSky, Color color )
	{
		Material skyMaterial = null;
		var rotation = WorldRotation.Conjugate;

		if ( source == FogColorSource.CustomSky )
		{
			skyMaterial = customSky;
		}
		else if ( source == FogColorSource.SceneSky &&
			(renderTags.GetTokens().Count == 0 || renderTags.Has( "skybox" )) &&
			!excludeTags.Has( "skybox" ) )
		{
			var skyboxWorld = world.GetRenderingSkybox()?.SkyboxWorld;
			var sky = skyboxWorld is not null ? skyboxWorld.GetSky() : world.GetSky( renderTags, excludeTags );

			if ( sky is not null )
			{
				skyMaterial = sky.Material;
				color *= sky.SkyTint.WithAlpha( 1 );
				rotation = sky.Transform.Rotation.Conjugate;
			}
		}

		if ( skyMaterial is { IsValid: false } )
			skyMaterial = null;

		var texture = skyMaterial?.GetTexture( "g_tSkyTexture" );
		if ( texture is not { IsValid: true } || !texture.Desc.IsCube )
			texture = null;

		var encoding = FogSkyEncoding.Rgb;
		if ( skyMaterial is not null && skyMaterial.ShaderName.Contains( "sky" ) )
		{
			encoding = skyMaterial.GetFeature( "F_TEXTURE_FORMAT2" ) switch
			{
				2 => FogSkyEncoding.YCoCg,
				4 or 5 => FogSkyEncoding.Rgbm,
				_ => FogSkyEncoding.Rgb
			};
		}

		return (color, texture, encoding, rotation);
	}
}
