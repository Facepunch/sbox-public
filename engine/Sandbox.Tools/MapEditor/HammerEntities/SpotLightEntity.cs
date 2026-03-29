namespace Editor.MapEditor.EntityDefinitions;

/// <summary>
/// A directional spot light entity.
/// </summary>
[Library( "light_spot" ), HammerEntity]
[EditorModel( "models/editor/spot", "rgb(0, 255, 192)", "rgb(255, 64, 64)" )]
[Sphere( "lightsourceradius", IsLean = true )]
[VisGroup( VisGroup.Lighting )]
[Light, LightCone, CanBeClientsideOnly]
[HideProperty( "enable_shadows" )]
[Title( "Spot Light" ), Category( "Lighting" ), Icon( "flashlight_on" ), Description( "A directional spot light entity." )]
class SpotLightEntity : HammerEntityDefinition
{
	/// <inheritdoc cref="PointLightEntity.Enabled"/>
	[Property, DefaultValue( true )]
	public bool Enabled
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Color"/>
	[Property, DefaultValue( "255 255 255" )]
	public Color Color
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Brightness"/>
	[Property, DefaultValue( 1 )]
	public float Brightness
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.BrightnessMultiplier"/>
	public float BrightnessMultiplier
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Range"/>
	[Property, DefaultValue( 512 ), Description( "Distance range for light. 0=infinite" )]
	public float Range
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Falloff"/>
	[Property, DefaultValue( 1.0f ), Description( "Angular falloff exponent. Does not work with light cookies. Does not work with dynamic lighting." )]
	public float Falloff
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Inner cone angle. No angular falloff within this cone.
	/// </summary>
	[Property, DefaultValue( 45 ), Description( "Inner cone angle. No angular falloff within this cone." )]
	public float InnerConeAngle
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Outer cone angle.
	/// </summary>
	[Property, DefaultValue( 60 ), Description( "Outer cone angle." )]
	public float OuterConeAngle
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="OrthoLightEntity.LightCookie"/>
	[Property]
	public Texture LightCookie
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Flicker"/>
	public bool Flicker
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.DynamicShadows"/>
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

	/// <inheritdoc cref="PointLightEntity.FogStrength"/>
	[Property( "fogcontributionstrength" ), DefaultValue( 1.0f ), Description( "Overrides how much the light affects the fog. (if enabled)" )]
	public float FogStrength
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.UseNoFog"/>
	public void UseNoFog() => FogLighting = VolumetricFogType.None;

	/// <inheritdoc cref="PointLightEntity.UseFog"/>
	public void UseFog() => FogLighting = VolumetricFogType.Dynamic;

	/// <inheritdoc cref="PointLightEntity.UseFogNoShadows"/>
	public void UseFogNoShadows() => FogLighting = VolumetricFogType.DynamicNoShadows;




	/// <inheritdoc cref="PointLightEntity.FadeDistanceMin"/>
	[Property( "fademindist" ), Category( "Fade Distance" ), DefaultValue( -250 ), Description( "Distance at which the light starts to fade. (less than 0 = use 'Fade Distance Max')" )]
	public float FadeDistanceMin
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.FadeDistanceMax"/>
	[Property( "fademaxdist" ), Category( "Fade Distance" ), DefaultValue( 1250 ), Description( "Maximum distance at which the light is visible. (0 = don't fade out)" )]
	public float FadeDistanceMax
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.ShadowFadeDistanceMin"/>
	[Property( "shadowfademindist" ), Category( "Shadows" ), DefaultValue( -250 ), Title( "Shadow Start Fade Distance" ), Description( "Distance at which the shadow starts to fade. (less than 0 = use 'Shadow End Fade Dist')" )]
	public float ShadowFadeDistanceMin
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.ShadowFadeDistanceMax"/>
	[Property( "shadowfademaxdist" ), Category( "Shadows" ), DefaultValue( 1000 ), Title( "Shadow End Fade Distance" ), Description( "Maximum distance at which the shadow is visible. (0 = don't fade out)" )]
	public float ShadowFadeDistanceMax
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

	[Property, Category( "Shadows" ), DefaultValue( 0 ), Description( "0 = use default texture resolution" )]
	internal int ShadowTextureWidth { get; set; } = 0;

	[Property, Category( "Shadows" ), DefaultValue( 0 ), Description( "0 = use default texture resolution" )]
	internal int ShadowTextureHeight { get; set; } = 0;

	// TODO: This was in the fgd, but I found no references to it anywhere
	//[Property, Category( "Shadows" ), DefaultValue( false ), Display( Name = "Transmit Shadow Casters to Client", Description = "When this light is visible to a player, add its shadow casters to the player's PVS." )]
	//internal bool pvs_modify_entity { get; set; } = false;

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

	[Property( "baked_light_indexing" ), Category( "Advanced" ), DefaultValue( true ), Description( "Allows direct light to be indexed if baked. Indexed lights have per-pixel quality specular lighting and normal map response." )]
	internal bool BakedLightIndexing { get; set; } = true;

	[Property( "lightsourcedim0" ), Category( "Shape" ), DefaultValue( 0.00 ), MinMax( 0, 128 ), Description( "Sphere radius of the light." )]
	internal float LightSize { get; set; } = 0.0f;

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
