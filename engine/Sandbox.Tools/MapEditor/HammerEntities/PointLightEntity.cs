namespace Editor.MapEditor.EntityDefinitions;

/// <summary>
/// An omni-directional light entity.
/// </summary>
[Library( "light_omni" ), HammerEntity]
[EditorModel( "models/editor/omni", "rgb(0, 255, 192)", "rgb(255, 64, 64)" )]
[Sphere( "lightsourceradius", IsLean = true ), Sphere( "range", 255, 255, 0 )]
[Light, VisGroup( VisGroup.Lighting ), CanBeClientsideOnly]
[HideProperty( "enable_shadows" )]
[Title( "Point Light" ), Category( "Lighting" ), Icon( "wb_incandescent" ), Description( "An omnidirectional light entity." )]
class PointLightEntity : HammerEntityDefinition
{
	/// <summary>
	/// Whether this light is enabled or not.
	/// </summary>
	[Property, DefaultValue( true )]
	public bool Enabled
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Color of this light.
	/// </summary>
	[Property, DefaultValue( "255 255 255" )]
	public Color Color
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Brightness of this light.
	/// </summary>
	[Property, DefaultValue( 1 )]
	public float Brightness
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Brightness multiplier.
	/// </summary>
	public float BrightnessMultiplier
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Distance range for light. 0=infinite
	/// </summary>
	[Property, DefaultValue( 512 ), Description( "Distance range for light. 0=infinite" )]
	public float Range
	{
		get => default;
		set { }
	}

	// TODO: Does not affect preview in Hammer/does not work in fast build/per pixel lights!! Does not work with lightcookies.
	/// <summary>
	/// Angular falloff exponent. Does not work with light cookies. Does not work with dynamic lighting.
	/// </summary>
	public float Falloff
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Flicker the light 8 times a second.
	/// </summary>
	public bool Flicker
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Enable or disable dynamic shadow casting.
	/// </summary>
	public bool DynamicShadows
	{
		get => default;
		set { }
	}



	internal enum VolumetricFogType
	{
		None,
		Baked,
		Dynamic,
		DynamicNoShadows
	}

	[Property( "fog_lighting" ), DefaultValue( VolumetricFogType.Baked ), Description( "Volumetric Fogging - How should light interact with volumetric fogging. Requires Volumetric Fog Cntroller entity be present to function." )]
	internal VolumetricFogType FogLighting
	{
		get => default;
		set { }
	}


	internal enum LightSourceShape
	{
		Sphere,
		Tube,
		Rectangle,
	};

	/// <summary>
	/// Overrides how much the light affects the fog. (if enabled)
	/// </summary>
	[Property( "fogcontributionstrength" ), DefaultValue( 1.0f ), Description( "Overrides how much the light affects the fog. (if enabled)" )]
	public float FogStrength
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Disable volumetric fog.
	/// </summary>
	public void UseNoFog() => FogLighting = VolumetricFogType.None;

	/// <summary>
	/// Enable dynamic volumetric fog.
	/// </summary>
	public void UseFog() => FogLighting = VolumetricFogType.Dynamic;

	/// <summary>
	/// Enable dynamic volumetric fog without shadows.
	/// </summary>
	public void UseFogNoShadows() => FogLighting = VolumetricFogType.DynamicNoShadows;



	/// <summary>
	/// Distance at which the light starts to fade. (less than 0 = use 'Fade Distance Max')
	/// </summary>
	[Property( "fademindist" ), Category( "Fade Distance" ), DefaultValue( -250 ), Description( "Distance at which the light starts to fade. (less than 0 = use 'Fade Distance Max')" )]
	public float FadeDistanceMin
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Maximum distance at which the light is visible. (0 = don't fade out)
	/// </summary>
	[Property( "fademaxdist" ), Category( "Fade Distance" ), DefaultValue( 1250 ), Description( "Maximum distance at which the light is visible. (0 = don't fade out)" )]
	public float FadeDistanceMax
	{
		get => default;
		set { }
	}



	internal enum ShadowType
	{
		No,
		Yes
	}

	[Property, Category( "Shadows" ), DefaultValue( ShadowType.Yes ), Description( "Whether this light casts shadows." )]
	internal ShadowType CastShadows { get; set; } = ShadowType.Yes;

	[Property( "nearclipplane" ), Category( "Shadows" ), DefaultValue( 1.0f ), Description( "Distance for near clip plane for shadow map." )]
	internal float ShadowNearClipPlane { get; set; } = 1.0f;

	/// <summary>
	/// Distance at which the shadow starts to fade. (less than 0 = use 'Shadow End Fade Dist')
	/// </summary>
	public float ShadowFadeDistanceMin
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Maximum distance at which the shadow is visible. (0 = don't fade out)
	/// </summary>
	public float ShadowFadeDistanceMax
	{
		get => default;
		set { }
	}



	// Internal thing used only during compile.
	[Property, DefaultValue( true ), Category( "Advanced" ), Description( "If true, this light renders into baked cube maps." )]
	internal bool RenderToCubemaps { get; set; } = true;

	[Property, DefaultValue( 0 ), Category( "Advanced" ), Description( "When the number of visible lights exceeds the rendering budget, higher priority lights are chosen for rendering first." )]
	internal int Priority { get; set; } = 0;

	[Property, Category( "Advanced" ), Description( "Semicolon-delimited list of light groups to affect." )]
	internal string LightGroup { get; set; }

	// TODO: What does this even do?
	[Property, DefaultValue( 2.0f ), Category( "Advanced" ), Description( "The radius of the light source in game units." )]
	internal float LightSourceRadius { get; set; } = 2.0f;

	[Property( "baked_light_indexing" ), Category( "Advanced" ), DefaultValue( true ), Description( "Allows direct light to be indexed if baked. Indexed lights have per-pixel quality specular lighting and normal map response" )]
	internal bool BakedLightIndexing { get; set; } = true;

	[Property, Category( "Animation" )]
	[Description( "Controls how the animation loops. This is useful if you wish to make first part be the \"turn on animation\" and the remaining part of the curve be the \"looping animation\"<br/> If 0 or above - Loop from given point in animation (X axis)<br/>If below 0 - Do not loop." )]
	internal float AnimationLoop { set; get; }

	[Property( "attenuation1" ), DefaultValue( 0 ), Category( "Advanced" )]
	public float LinearAttenuation
	{
		get => default;
		set { }
	}

	[Property( "attenuation2" ), DefaultValue( 1 ), Category( "Advanced" )]
	public float QuadraticAttenuation
	{
		get => default;
		set { }
	}
	internal enum DirectLightMode
	{
		/// <summary>
		/// Disabled for direct lighting.
		/// </summary>
		None,
		/// <summary>
		/// Fully baked into lightmaps. No real-time shadow maps are generated.
		/// </summary>
		Baked,
		/// <summary>
		/// Fully dynamic with real-time shadow maps that include all objects. Not baked into lightmaps.
		/// </summary>
		Dynamic,
		/// <summary>
		/// Baked into lightmaps but also generates real-time shadow maps for dynamic objects only.
		/// Static objects are excluded from shadow maps since their shadows come from lightmaps.
		/// </summary>
		Stationary
	}

	[Property, Description( "Specifies the mode of direct lighting to be used." ), DefaultValue( DirectLightMode.Baked )]
	internal DirectLightMode DirectLight { get; set; } = DirectLightMode.Baked;

	internal enum IndirectLightMode
	{
		None,
		Baked
	}

	[Property, Description( "Specifies the mode of indirect lighting to be used." ), DefaultValue( IndirectLightMode.Baked )]
	internal IndirectLightMode IndirectLight { get; set; } = IndirectLightMode.Baked;

	[Property( "bouncescale" ), DefaultValue( 1.0f ), Range( 0.0f, 1.0f ), Category( "Advanced" ), Description( "Scale for the brightness of light bounces, values beyond 1.0f are not energy conserving." )]
	internal float IndirectLightScale { get; set; } = 1.0f;
}
