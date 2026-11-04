using NativeEngine;
using System.Text.Json.Nodes;
using static Sandbox.Component;

namespace Sandbox;

[Expose]
public abstract partial class Light : Component, IColorProvider, ExecuteInEditor, ITintable
{
	SceneLight _sceneObject;
	Color? _lastAppliedLightColor;

	internal SceneLight SceneObject => _sceneObject;

	/// <summary>
	/// Versions light defaults so existing scenes retain their original tint.
	/// </summary>
	public override int ComponentVersion => 1;

	/// <summary>
	/// Preserve the original tint when it was omitted from a legacy point light.
	/// </summary>
	[Expose, JsonUpgrader( typeof( PointLight ), 1 )]
	static void UpgradePointLightTint( JsonObject obj ) => UpgradeLegacyTint( obj );

	/// <summary>
	/// Preserve the original tint when it was omitted from a legacy spot light.
	/// </summary>
	[Expose, JsonUpgrader( typeof( SpotLight ), 1 )]
	static void UpgradeSpotLightTint( JsonObject obj ) => UpgradeLegacyTint( obj );

	/// <summary>
	/// Preserve the original tint when it was omitted from a legacy directional light.
	/// </summary>
	[Expose, JsonUpgrader( typeof( DirectionalLight ), 1 )]
	static void UpgradeDirectionalLightTint( JsonObject obj ) => UpgradeLegacyTint( obj );

	/// <summary>
	/// Supply the legacy default without rounding its channels through the color string serializer.
	/// </summary>
	static void UpgradeLegacyTint( JsonObject obj )
	{
		var tint = (Color)"#E9FAFF";
		obj[nameof( LightColor )] ??= new JsonArray( tint.r, tint.g, tint.b, tint.a );
	}

	/// <summary>
	/// Index used by baked probe/lightmap direct-light textures, or -1 for a dynamic light.
	/// </summary>
	[Property, Hide]
	public int BakeLightIndex
	{
		get;
		set
		{
			if ( value is < -1 or > 254 ) throw new ArgumentOutOfRangeException( nameof( value ) );
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() ) _sceneObject.SetBakedLightIndex( value, MixedShadows );
		}
	} = -1;

	/// <summary>
	/// Baked lighting with realtime shadows from dynamic objects, like Hammer's stationary lights.
	/// Requires a baked light index and enabled shadows.
	/// </summary>
	[Property, Group( "Shadows", Order = 2 ), Order( 2 ), Advanced]
	public bool MixedShadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( _sceneObject.IsValid() && BakeLightIndex >= 0 )
				_sceneObject.SetBakedLightIndex( BakeLightIndex, value );
		}
	}

	/// <summary>
	/// Advanced settings imported from legacy map data that don't have public properties yet
	/// (baking, bounce, shadow texture size, raw attenuation). Applied to the native light when it's
	/// created. Internal for now - we may expose some of these as proper properties later.
	/// </summary>
	internal LegacyLightData? LegacyData { get; set; }

	/// <summary>
	/// Backend carrier for map light settings that aren't exposed as component properties yet.
	/// </summary>
	internal struct LegacyLightData
	{
		public int DirectLight;
		public int BakeLightIndex;
		public float BakeLightIndexScale;
		public bool BakedLightIndexing;
		public float FogContributionStrength;
		public bool RenderDiffuse;
		public bool RenderSpecular;
		public int FogLighting;

		// Only set by light types whose components don't expose these (falloff, constant attn, point cookie).
		public float? ConstantAttenuation;
		public float? LinearAttenuation;
		public float? QuadraticAttenuation;
		public float? FallOff;
		public Texture Cookie;

		public readonly void ApplyTo( CSceneLightObject light )
		{
			// The lightbinner gates realtime shadow maps for baked lights on MIXED_SHADOWS, so only
			// Stationary may set it - Baked gets its shadows from the lightmap.
			switch ( DirectLight )
			{
				case 3: // HAMMER_DIRECT_LIGHT_STATIONARY
					light.SetLightFlags( light.GetLightFlags() | 16 ); // LIGHTTYPE_FLAGS_MIXED_SHADOWS
					light.SetLightFlags( light.GetLightFlags() | 32 ); // LIGHTTYPE_FLAGS_BAKED
					break;
				case 1: // HAMMER_DIRECT_LIGHT_BAKED
					light.SetLightFlags( light.GetLightFlags() | 32 ); // LIGHTTYPE_FLAGS_BAKED
					break;
			}

			light.SetBakeLightIndex( BakeLightIndex );
			light.SetBakeLightIndexScale( BakeLightIndexScale );
			light.SetUsesIndexedBakedLighting( BakedLightIndexing );
			light.SetFogContributionStength( FogContributionStrength );
			light.SetRenderDiffuse( RenderDiffuse );
			light.SetRenderSpecular( RenderSpecular );
			light.SetFogLightingMode( FogLighting );

			if ( ConstantAttenuation is { } constant ) light.SetConstantAttn( constant );
			if ( LinearAttenuation is { } linear ) light.SetLinearAttn( linear );
			if ( QuadraticAttenuation is { } quadratic ) light.SetQuadraticAttn( quadratic );
			if ( FallOff is { } fallOff ) light.SetFallOff( fallOff );
			if ( Cookie is not null ) light.SetLightCookie( Cookie.native );
		}
	}

	/// <summary>
	/// Tint multiplied by the light's intensity and color temperature.
	/// </summary>
	// Retain the property name for existing scenes, scripts and animations.
	[Property, Group( "Color", Order = -1 ), Title( "Tint" ), Order( -2 )]
	public Color LightColor
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			ApplyLightColor();
		}
	} = Color.White;

	/// <summary>
	/// Brightness multiplier applied to the tint. One preserves the original brightness,
	/// zero emits no light, and values above one produce brighter HDR lighting.
	/// </summary>
	[Property, Group( "Color", Order = -1 ), Range( 0, 10 ), Order( -1 ), SliderGradient( "#000", "#fff" )]
	public float Intensity
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? MathF.Max( 0, value ) : 1.0f;
			if ( field == value ) return;
			field = value;

			ApplyLightColor();
		}
	} = 1.0f;

	/// <summary>
	/// Color temperature in Kelvin. The slider covers 1000 to 12000; values up to 40000 can
	/// be entered directly. Lower values are warmer and higher values are cooler.
	/// The default of 6500 K is neutral white and preserves the tint exactly.
	/// </summary>
	[Property, Group( "Color", Order = -1 ), Title( "Temperature (K)" ), Range( 1000, 12000 ), Order( -3 )]
	// Samples of the normalized temperature curve, positioned within the Kelvin range.
	[SliderGradient( "#ff4400", "#ff890e", "#ffb270", "#ffdabf", "#ffffff", "#cadbff", "#bfd4ff",
		Positions = new float[] { 0, 1000f / 11000, 2000f / 11000, 3500f / 11000, 0.5f, 9000f / 11000, 1 } )]
	public float Temperature
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? Math.Clamp( value, 1000, 40000 ) : 6500.0f;
			if ( field == value ) return;
			field = value;

			ApplyLightColor();
		}
	} = 6500.0f;

	/// <summary>
	/// Reference white used to normalize temperature tints around 6500 K.
	/// </summary>
	static readonly Color TemperatureWhite = TemperatureColor( 6500.0f );

	/// <summary>
	/// Update the rendered color without changing the serialized tint or its HDR values.
	/// </summary>
	void ApplyLightColor()
	{
		if ( !_sceneObject.IsValid() ) return;

		var color = LightColor * Intensity;
		var temperature = TemperatureColor( Temperature );

		// Normalize the whole curve, so 6500 K is exactly white without a discontinuity around it.
		color *= new Color( temperature.r / TemperatureWhite.r, temperature.g / TemperatureWhite.g, temperature.b / TemperatureWhite.b );

		if ( AnimationEnabled )
		{
			color *= EvaluateAnimation( Scene.TimeNow );
		}

		color = color.WithAlpha( LightColor.a );
		if ( _lastAppliedLightColor == color ) return;

		_sceneObject.LightColor = color;
		_lastAppliedLightColor = color;
	}

	/// <summary>
	/// Approximate a blackbody tint using the same RGB fit as ColorGrading.shader,
	/// converted from sRGB to linear for multiplication with the light's energy.
	/// </summary>
	static Color TemperatureColor( float kelvin )
	{
		var temperature = kelvin * 0.01f;
		float red;
		float green;
		float blue;

		if ( temperature <= 66.0f )
		{
			red = 1.0f;
			green = 0.39008158f * MathF.Log( temperature ) - 0.63184144f;
		}
		else
		{
			red = 1.29293619f * MathF.Pow( temperature - 60.0f, -0.13320476f );
			green = 1.12989086f * MathF.Pow( temperature - 60.0f, -0.07551485f );
		}

		if ( temperature >= 66.0f )
		{
			blue = 1.0f;
		}
		else if ( temperature <= 19.0f )
		{
			blue = 0.0f;
		}
		else
		{
			blue = 0.54320679f * MathF.Log( temperature - 10.0f ) - 1.19625409f;
		}

		return new Color( red.Clamp( 0, 1 ), green.Clamp( 0, 1 ), blue.Clamp( 0, 1 ) ).ToLinear();
	}

	/// <summary>
	/// Controls whether this light illuminates fog and whether that lighting includes shadows.
	/// </summary>
	[Property, Group( "Fog", Order = 3 ), Order( 0 )]
	public FogInfluence FogMode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.FogLighting = (SceneLight.FogLightingMode)value;
		}
	} = FogInfluence.Enabled;

	/// <summary>
	/// Strength of this light's contribution to fog lighting.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Fog", Order = 3 ), Order( 1 )]
	public float FogStrength
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.FogStrength = value;
		}
	} = 1.0f;

	/// <summary>
	/// Should this light cast shadows?
	/// </summary>
	[Property, ToggleGroup( nameof( Shadows ), Order = 2 ), Order( -10 )]
	public bool Shadows
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowsEnabled = value;
		}
	} = true;

	/// <summary>
	/// Bias applied to shadow depth comparisons to reduce self-shadowing artifacts.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Shadows", Order = 2 ), Order( 1 ), Advanced]
	public float ShadowBias
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowBias = value;
		}
	} = 0.0f;

	/// <summary>
	/// Controls the hardness of shadow edges.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Shadows", Order = 2 ), Order( 0 )]
	public float ShadowHardness
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			if ( _sceneObject.IsValid() )
				_sceneObject.ShadowHardness = value;
		}
	} = 0.0f;

	/// <summary>
	/// Which lighting terms this light is allowed to contribute to. For example,
	/// turn off <see cref="LightContribution.Specular"/> to stop a light producing highlights.
	/// </summary>
	[Property, Group( "Influence", Order = 0 ), Order( 2 ), EnumButtonGroup, Title( "Contributes" )]
	public LightContribution Contribution
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;

			ApplyContribution();
		}
	} = LightContribution.Diffuse | LightContribution.Specular | LightContribution.Transmissive;

	void ApplyContribution()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.RenderDiffuse = Contribution.HasFlag( LightContribution.Diffuse );
		_sceneObject.RenderSpecular = Contribution.HasFlag( LightContribution.Specular );
		_sceneObject.RenderTransmissive = Contribution.HasFlag( LightContribution.Transmissive );
	}

	Color IColorProvider.ComponentColor => LightColor;

	Color ITintable.Color { get => LightColor; set => LightColor = value; }

	public enum FogInfluence
	{
		[Icon( "blur_off" )]
		Disabled = SceneLight.FogLightingMode.None,
		[Icon( "blur_linear" )]
		Enabled = SceneLight.FogLightingMode.Dynamic,
		[Icon( "blur_on" )]
		WithoutShadows = SceneLight.FogLightingMode.DynamicNoShadows
	}

	/// <summary>
	/// Which lighting terms a light is allowed to contribute to.
	/// </summary>
	[Flags]
	public enum LightContribution
	{
		/// <summary>
		/// Soft, even shading across a surface.
		/// </summary>
		[Icon( "wb_sunny" )]
		Diffuse = 1,

		/// <summary>
		/// Glossy highlights and reflections.
		/// </summary>
		[Icon( "auto_awesome" )]
		Specular = 2,

		/// <summary>
		/// Light passing through surfaces (translucency / subsurface).
		/// </summary>
		[Icon( "opacity" )]
		Transmissive = 4
	}

	protected override void OnAwake()
	{
		Tags.Add( "light" );

		base.OnAwake();
	}

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid(), "_sceneObject should be null" );
		Assert.NotNull( Scene, "Scene should not be null" );

		_sceneObject = CreateSceneObject();
		_lastAppliedLightColor = null;

		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Component = this;
			ApplyLightColor();
			_sceneObject.ShadowsEnabled = Shadows;
			_sceneObject.FogLighting = (SceneLight.FogLightingMode)FogMode;
			_sceneObject.FogStrength = FogStrength;
			_sceneObject.ShadowBias = ShadowBias;
			_sceneObject.ShadowHardness = ShadowHardness;
			ApplyContribution();

			// Advanced map settings not covered by properties - overrides native state set above.
			if ( LegacyData is { } legacy )
				legacy.ApplyTo( _sceneObject.lightNative );

			if ( BakeLightIndex >= 0 )
				_sceneObject.SetBakedLightIndex( BakeLightIndex, MixedShadows );

			OnTransformChanged();
			OnTagsChanged();

			Transform.OnTransformChanged += OnTransformChanged;
		}
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= OnTransformChanged;

		_sceneObject?.Delete();
		_sceneObject = null;
	}

	protected abstract SceneLight CreateSceneObject();

	void OnTransformChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject.Transform = WorldTransform;
	}

	/// <summary>
	/// Tags have been updated - lets update our light's tags
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( !_sceneObject.IsValid() )
			return;

		_sceneObject?.Tags.SetFrom( Tags );
	}
}
