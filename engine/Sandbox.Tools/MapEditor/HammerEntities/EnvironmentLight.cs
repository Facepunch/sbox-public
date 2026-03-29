namespace Editor.MapEditor.EntityDefinitions;

/// <summary>
/// An environment light entity. This acts as the sun.
/// </summary>
[Library( "light_environment" ), HammerEntity]
[EditorModel( "models/editor/sun", "yellow", "rgb(255, 64, 64)" )]
[Light, Global( "sun" ), VisGroup( VisGroup.Lighting )]
[BakeAmbientLight( "ambient_color" ), BakeAmbientOcclusion, BakeSkyLight]
[HideProperty( "enable_shadows" )]
[Title( "Environment Light" ), Category( "Lighting" ), Icon( "wb_sunny" )]
[Description( @"Sets the color and angle of the light from the sun and sky.<br/><br/>
Typical setup:<br/>
1. Create an <b>env_sky</b> entity to use as your skybox<br/>
2. Create a <b>light_environment</b> entity and set <b>Sky IBL Source</b> to the name of the <b>env_sky</b> entity<br/>
3. Right-click on your <b>light_environment</b> entity and select 'Selected environment light -> Estimate lighting from HDR skybox'<br/>
4. Adjust angle and brightness of the sunlight as you see fit" )]
class EnvironmentLightEntity : HammerEntityDefinition
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

	[Property( "angulardiameter" ), DefaultValue( 1.0f ), Description( "The angular extent of the sun for casting soft shadows. Higher numbers are more diffuse. 1 is a good starting value." )]
	internal float SunAngle { get; set; } = 1.0f;

	/// <summary>
	/// Whether this light should cast dynamic shadows.
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

	#region Shadow Properties

	internal enum ShadowType
	{
		No,
		Yes
	}

	[Property, Category( "Shadows" ), DefaultValue( ShadowType.Yes ), Description( "Whether this light casts shadows." )]
	internal ShadowType CastShadows { get; set; } = ShadowType.Yes;

	[Property( "nearclipplane" ), Category( "Shadows" ), DefaultValue( 1.0f ), Description( "Distance for near clip plane for shadow map." )]
	internal float ShadowNearClipPlane { get; set; } = 1.0f;

	[Property( "numcascades" ), Category( "Shadows" ), DefaultValue( 3 ), MinMax( 1, 3 ), Description( "Number of shadow cascades to use." )]
	internal int CascadeAmount { get; set; } = 3;

	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeDistance0 { get; set; } = 0.0f;
	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeDistance1 { get; set; } = 0.0f;
	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeDistance2 { get; set; } = 0.0f;
	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeResolution0 { get; set; } = 0.0f;
	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeResolution1 { get; set; } = 0.0f;
	[Property, Category( "Shadows" ), DefaultValue( 0.0f )] internal float ShadowCascadeResolution2 { get; set; } = 0.0f;

	#endregion

	// Internal thing used only during compile.
	[Property, DefaultValue( true ), Category( "Advanced" ), Description( "If true, this light renders into baked cube maps." )]
	internal bool RenderToCubemaps { get; set; } = true;

	[Property, DefaultValue( 0 ), Category( "Advanced" ), Description( "When the number of visible lights exceeds the rendering budget, higher priority lights are chosen for rendering first." )]
	internal int Priority { get; set; } = 0;

	[Property, Category( "Advanced" ), Description( "Semicolon-delimited list of light groups to affect." )]
	internal string LightGroup { get; set; }

	[Property( "baked_light_indexing" ), Category( "Advanced" ), DefaultValue( true ), Description( "Allows direct light to be indexed if baked. Indexed lights have per-pixel quality specular lighting and normal map response" )]
	internal bool BakedLightIndexing { get; set; } = true;

	#region Sky Light Properties

	/// <summary>
	/// Ambient light color outside of all light probes.
	/// </summary>
	[Property, DefaultValue( "255 255 255" ), Category( "Sky" ), Description( "Ambient light color outside of all light probes." )]
	public Color SkyColor
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Ambient light intensity outside of all light probes.
	/// </summary>
	[Property, DefaultValue( 1 ), Category( "Sky" ), Description( "Ambient light intensity outside of all light probes." )]
	public float SkyIntensity
	{
		get => default;
		set { }
	}

	[Property( "lower_hemisphere_is_black" ), Category( "Sky" ), DefaultValue( true )]
	internal bool LowerHemisphereIsBlack { get; set; } = true;

	[Property( "skytexture" ), FGDType( "target_destination" ), Category( "Sky" ), Title( "Sky IBL Source" ), Description( "env_sky entity, lat-long/h-cross/v-cross skybox image, or sky material to use for sky IBL." )]
	internal string SkyTexture { get; set; }

	[Property, Category( "Sky" ), DefaultValue( 1.0f ), Title( "Sky IBL Scale" ), Description( "Scale value for IBL intensity fine-tuning." )]
	internal float SkyTextureScale { get; set; } = 1.0f;

	[Property( "skyambientbounce" ), Category( "Ambient Lighting" ), DefaultValue( "147 147 147" )]
	internal Color SkyAmbientBounceColor { get; set; }

	[Property, Category( "Sky" ), DefaultValue( 32.0f ), Title( "Sun Light Minimum Brightness Threshold" ), Description( "Brightness beyond which pixels in the Sky IBL Source are considered to be coming from the sun." )]
	internal float SunLightMinBrightness { get; set; } = 32.0f;

	#endregion

	#region Ambient Light and Occlusion properties

	[Property( "ambient_occlusion" ), Category( "Ambient Occlusion" ), DefaultValue( false )]
	internal bool AmbientOcclusion { get; set; } = false;

	[Property( "max_occlusion_distance" ), Category( "Ambient Occlusion" ), DefaultValue( 16.0f )]
	internal float MaxOcclusionDistance { get; set; } = 16.0f;

	[Property( "fully_occluded_fraction" ), Category( "Ambient Occlusion" ), DefaultValue( 1.0f )]
	internal float FullyOccludedFraction { get; set; } = 1.0f;

	[Property( "occlusion_exponent" ), Category( "Ambient Occlusion" ), DefaultValue( 1.0f )]
	internal float OcclusionExponent { get; set; } = 1.0f;

	/// <summary>
	/// Ambient light color
	/// </summary>
	[Property( "ambient_color" ), Category( "Ambient Lighting" ), DefaultValue( "0 0 0" )]
	public Color AmbientColor
	{
		get => default;
		set { }
	}

	#endregion

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

	[Property( "directlight" ), Description( "Specifies the mode of direct lighting to be used." ), DefaultValue( DirectLightMode.Baked )]
	internal DirectLightMode DirectLight { get; set; } = DirectLightMode.Baked;

	internal enum IndirectLightMode
	{
		None,
		Baked
	}

	[Property( "indirectlight" ), Description( "Specifies the mode of indirect lighting to be used." ), DefaultValue( IndirectLightMode.Baked )]
	internal IndirectLightMode IndirectLight { get; set; } = IndirectLightMode.Baked;

	[Property( "bouncescale" ), DefaultValue( 1.0f ), Range( 0.0f, 1.0f ), Category( "Advanced" ), Description( "Scale for the brightness of light bounces, values beyond 1.0f are not energy conserving." )]
	internal float IndirectLightScale { get; set; } = 1.0f;
}
