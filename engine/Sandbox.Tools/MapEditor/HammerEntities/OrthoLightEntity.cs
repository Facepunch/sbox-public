namespace Editor.MapEditor.EntityDefinitions;

/// <summary>
/// A directional, orthographic light entity.
/// </summary>
[Library( "light_ortho" ), HammerEntity]
[EditorModel( "models/editor/ortho", "rgb(0, 255, 192)", "rgb(255, 64, 64)" )]
[OrthoBoundsHelper( "range", "ortholightwidth", "ortholightheight" )]
[Light, VisGroup( VisGroup.Lighting ), CanBeClientsideOnly]
[HideProperty( "enable_shadows" )]
[Title( "Orthographic Light" ), Category( "Lighting" ), Icon( "highlight" ), Description( "A directional, orthographic light entity." )]
class OrthoLightEntity : HammerEntityDefinition
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
	[Property, DefaultValue( 2048 ), Description( "Distance range for light. 0=infinite" )]
	public float Range
	{
		get => default;
		set { }
	}

	/// <inheritdoc cref="PointLightEntity.Falloff"/>
	public float Falloff
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Orthographic light rectangle width.
	/// </summary>
	[Property, DefaultValue( 512 ), Description( "Orthographic light rectangle width." )]
	public float OrthoLightWidth
	{
		get => default;
		set { }
	}

	/// <summary>
	/// Orthographic light rectangle height.
	/// </summary>
	[Property, DefaultValue( 512 ), Description( "Orthographic light rectangle height." )]
	public float OrthoLightHeight
	{
		get => default;
		set { }
	}

	/// <summary>
	/// The light cookie texture for this light. A light cookie is like a filter or a mask for the emitted light.
	/// </summary>
	[Property, Description( "The light cookie texture for this light. A light cookie is like a filter or a mask for the emitted light." )]
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

	internal enum VolumetricFogType
	{
		None,
		Baked,
		Dynamic,
		//BakedNoShadows, // Not supported by the engine.
		//DynamicNoShadows
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

	/// <inheritdoc cref="PointLightEntity.DynamicShadows"/>
	public bool DynamicShadows
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

	// Internal thing used only during compile.
	[Property, DefaultValue( true ), Category( "Advanced" ), Description( "If true, this light renders into baked cube maps." )]
	internal bool RenderToCubemaps { get; set; } = true;

	[Property, DefaultValue( 0 ), Category( "Advanced" ), Description( "When the number of visible lights exceeds the rendering budget, higher priority lights are chosen for rendering first." )]
	internal int Priority { get; set; } = 0;

	[Property, Category( "Advanced" ), Description( "Semicolon-delimited list of light groups to affect." )]
	internal string LightGroup { get; set; }

	[Property( "baked_light_indexing" ), Category( "Advanced" ), DefaultValue( true ), Description( "Allows direct light to be indexed if baked. Indexed lights have per-pixel quality specular lighting and normal map response." )]
	internal bool BakedLightIndexing { get; set; } = true;

	// TODO: Doesn't seem to do anything, but leaving this in for now
	[Property( "angulardiameter" ), Category( "Advanced" ), DefaultValue( 1.0f ), Description( "The angular extent of the sun for casting soft shadows. Higher numbers are more diffuse. 1 is a good starting value." )]
	internal float SunSpreadAngle { get; set; } = 1.0f;

	[Property, Category( "Animation" )]
	[Description( "Controls how the animation loops. This is useful if you wish to make first part be the \"turn on animation\" and the remaining part of the curve be the \"looping animation\"<br/> If 0 or above - Loop from given point in animation (X axis)<br/>If below 0 - Do not loop." )]
	internal float AnimationLoop { set; get; }


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
}
