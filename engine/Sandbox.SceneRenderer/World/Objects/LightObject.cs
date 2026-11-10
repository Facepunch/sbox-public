namespace Sandbox.SceneRenderer;

/// <summary>
/// Point or spot light, equivalent to native's <c>CSceneLightObject</c>.
/// Uses component units; spotlights point along the transform's forward axis.
/// </summary>
public sealed class LightObject : RenderObject
{
	/// <summary>
	/// Supported light shapes.
	/// </summary>
	public enum LightKind
	{
		/// <summary>
		/// Shines every way from its position, out to <see cref="Radius"/>. Its shadow is a cube map.
		/// </summary>
		Point,
		/// <summary>
		/// Shines a cone along its forward axis, <see cref="ConeOuter"/> wide. Its shadow is one projection.
		/// </summary>
		Spot,
	}

	LightKind kind;
	float radius = 400;

	internal override bool SizeCulled => false;
	float coneOuter = 45;

	/// <summary>
	/// A light of a kind, at a transform - a spot points along its forward axis.
	/// </summary>
	public LightObject( LightKind kind, Transform transform )
	{
		this.kind = kind;
		Transform = transform;
		UpdateBounds();
	}

	/// <summary>
	/// Point or spot. Changing it changes the bounds.
	/// </summary>
	public LightKind Kind
	{
		get => kind;
		set
		{
			kind = value;
			UpdateBounds();
		}
	}

	/// <summary>
	/// Linear color. Can go above one - it's the light's brightness too.
	/// </summary>
	public Color Color { get; set; } = Color.White;

	/// <summary>
	/// How far the light reaches. It fades to exactly zero here.
	/// </summary>
	public float Radius
	{
		get => radius;
		set
		{
			radius = MathF.Max( value, 0.0f );
			UpdateBounds();
		}
	}

	/// <summary>
	/// Quadratic falloff, scaled like <c>PointLight.Attenuation</c> - one is a sensible default.
	/// </summary>
	public float Attenuation { get; set; } = 1.0f;

	/// <summary>
	/// Enable shadow maps: cube for point lights, single projection for spots. Defaults to true.
	/// </summary>
	public bool CastShadows { get; set; } = true;

	/// <summary>
	/// Zero is the softest the shadow filter goes, one the sharpest - <c>Light.ShadowHardness</c>.
	/// </summary>
	public float ShadowHardness { get; set; }

	/// <summary>
	/// Baked light, indexed after dynamic lights instead of clustered (<c>LIGHTTYPE_FLAGS_BAKED</c>).
	/// Requires <see cref="MixedShadows"/> for runtime shadow maps.
	/// </summary>
	public bool Baked { get; set; }

	/// <summary>
	/// Enable runtime shadows for a baked light (<c>LIGHTTYPE_FLAGS_MIXED_SHADOWS</c>).
	/// </summary>
	public bool MixedShadows { get; set; }

	/// <summary>
	/// Baked-light texture index, or -1 for none (<c>BakedLightIndexMapping</c>).
	/// </summary>
	public int BakeIndex { get; set; } = -1;

	/// <summary>
	/// Whether runtime shadows are enabled (<c>CLightBinnerStandard::AddLight</c>).
	/// </summary>
	internal bool RendersShadowMap => CastShadows && (!Baked || MixedShadows);

	/// <summary>
	/// Optional bridge-provided native data (<c>RenderTools.PackSceneLight</c>); otherwise pack managed properties.
	/// </summary>
	internal Features.LightBinnerFeature.GpuLight? NativePacked { get; set; }

	/// <summary>
	/// Spot lights: angle from the axis, in degrees, where the light starts to fade.
	/// </summary>
	public float ConeInner { get; set; } = 15;

	/// <summary>
	/// Spot lights: angle from the axis, in degrees, where the light has faded out.
	/// </summary>
	public float ConeOuter
	{
		get => coneOuter;
		set
		{
			coneOuter = Math.Clamp( value, 0.0f, 89.0f );
			UpdateBounds();
		}
	}

	/// <summary>
	/// Fit influence bounds, using a tighter box for narrow spot cones.
	/// </summary>
	void UpdateBounds()
	{
		if ( kind == LightKind.Spot && coneOuter < 45 )
		{
			var width = radius * MathF.Sin( coneOuter.DegreeToRadian() );
			LocalBounds = new BBox( new Vector3( 0, -width, -width ), new Vector3( radius, width, width ) );
			return;
		}

		LocalBounds = new BBox( new Vector3( -radius ), new Vector3( radius ) );
	}
}
