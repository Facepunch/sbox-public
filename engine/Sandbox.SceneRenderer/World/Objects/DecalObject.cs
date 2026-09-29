namespace Sandbox.SceneRenderer;

/// <summary>
/// Transformed unit-cube decal (<c>CDecalSceneObject</c>), culled and clustered before forward shaders apply it
/// (<c>Decals.hlsl</c>, <c>CDecalSceneObjectDesc::OnSceneObjectVisibleInView</c>). Has no standalone draw.
/// </summary>
public sealed class DecalObject : RenderObject
{
	/// <summary>
	/// A decal at <paramref name="transform"/>, whose scale is the box's size.
	/// </summary>
	public DecalObject( Transform transform )
	{
		Transform = transform;

		// CDecalSceneObjectDesc::UpdateBoundingBoxToMatchTransform: the unit cube, transformed
		LocalBounds = new BBox( new Vector3( -0.5f ), new Vector3( 0.5f ) );
	}

	/// <summary>
	/// Enable decal projection.
	/// </summary>
	public bool Visible { get; set; } = true;

	/// <summary>
	/// Optional sRGB colour map. Other maps are optional and sampled linearly.
	/// </summary>
	public Texture Color { get; set; }

	/// <summary>
	/// The normal map it projects.
	/// </summary>
	public Texture Normal { get; set; }

	/// <summary>
	/// Roughness, metalness and occlusion in one map.
	/// </summary>
	public Texture RoughnessMetalnessOcclusion { get; set; }

	/// <summary>
	/// A height map, for parallax and coverage.
	/// </summary>
	public Texture Height { get; set; }

	/// <summary>
	/// An emission map, scaled by <see cref="EmissionEnergy"/>.
	/// </summary>
	public Texture Emission { get; set; }

	/// <summary>
	/// Multiplies the colour; alpha is its opacity.
	/// </summary>
	public global::Color Tint { get; set; } = global::Color.White;

	/// <summary>
	/// Decals apply in this order, lowest first.
	/// </summary>
	public uint SortOrder { get; set; }

	/// <summary>
	/// Surfaces whose materials have any of these bits don't take the decal.
	/// </summary>
	public uint ExclusionBitMask { get; set; }

	/// <summary>
	/// How far from facing the decal a surface can turn before it fades out, 0 to 1.
	/// </summary>
	public float AttenuationAngle { get; set; } = 1.0f;

	/// <summary>
	/// How bright the emission map is.
	/// </summary>
	public float EmissionEnergy { get; set; } = 1.0f;

	/// <summary>
	/// The sequence of a sheeted colour texture to play.
	/// </summary>
	public uint SequenceIndex { get; set; }

	/// <summary>
	/// How much the decal's colour replaces the surface's, 0 to 1.
	/// </summary>
	public float ColorMix { get; set; } = 1.0f;

	/// <summary>
	/// How deep the height map's parallax reaches.
	/// </summary>
	public float ParallaxStrength { get; set; } = 0.25f;

	/// <summary>
	/// A bindless sampler to sample with, or -1 for the default.
	/// </summary>
	public int SamplerIndex { get; set; }

	/// <summary>
	/// With a height map: how much of the surface it covers, 0 to 1.
	/// </summary>
	public float CoverageAmount { get; set; } = 1.0f;

	/// <summary>
	/// With a height map: how soft the coverage edge is, 0 to 0.5.
	/// </summary>
	public float CoverageRange { get; set; } = 0.07f;
}
