namespace Borealis.Shadows;

internal partial class ShadowMapper
{
	/// <summary>
	/// How far from the camera an extra sun's shadow map reaches, in world units. Beyond it the sun is unshadowed.
	/// </summary>
	internal static float ExtraSunShadowDistance { get; set; } = 3000;

	static readonly string[] ExtraSunNames = ["Extra Sun 0", "Extra Sun 1", "Extra Sun 2", "Extra Sun 3", "Extra Sun 4", "Extra Sun 5", "Extra Sun 6", "Extra Sun 7"];

	/// <summary>
	/// Draw extra sun <paramref name="sun"/>'s shadow (<see cref="SceneLighting.ExtraSuns"/>): one orthographic map, fit to
	/// the view out to <see cref="ExtraSunShadowDistance"/> as a single cascade is and snapped to its texels, drawn every
	/// frame. A second sun's four cascades cost about what the sun's do, so this is the cheaper shadow. It's a projected
	/// shadow, which shaders read with <c>ProjectedShadow::GetSunVisibility</c>; returns its index, or none.
	/// </summary>
	internal uint FindOrCreateExtraSunShadow( int sun, in ShadowLight light )
	{
		if ( View.IsSkybox || View.NoShadows || sun >= ExtraSunNames.Length )
			return InvalidShadowIndex;

		if ( GPUProjectedShadows.Count >= ProjectedShadowBufferSize )
		{
			ProjectedShadowsCulled++;
			return InvalidShadowIndex;
		}

		int size = MaxCascadeResolution;
		var rotation = light.Direction.EulerAngles.ToRotation();
		Span<Cascade> fit = stackalloc Cascade[1];
		if ( GetCascades( fit, View, rotation, 1, 1.0f, ExtraSunShadowDistance, 0.0f, size ) == 0 )
			return InvalidShadowIndex;

		var cascade = fit[0];
		var target = Renderer.GetExtraSunTarget( sun, size );

		var shadowView = new ShadowViewDesc
		{
			Name = ExtraSunNames[sun],
			Position = cascade.Origin,
			Rotation = rotation,
			Orthographic = true,
			ZNear = cascade.Near,
			ZFar = cascade.Far,
			Width = cascade.Width,
			Height = cascade.Height,
			Resolution = size,
			Target = target,

			// Baked suns leave static casters to the lightmaps
			StaticFilter = light.Baked ? StaticFilter.NoStatic : StaticFilter.Any,
			DepthBias = ShadowDepthBias,
			SlopeScaledDepthBias = ShadowSlopeScale,
			TexelSized = TexelSizedCascades,
			TexelLods = TexelSizedCascades,
		};

		Matrix viewProjection = Renderer.RenderShadowView( shadowView );

		Matrix scaleBias = Matrix.Identity;
		scaleBias._numerics[0, 0] = 0.5f;
		scaleBias._numerics[1, 1] = -0.5f;
		scaleBias._numerics[0, 3] = 0.5f;
		scaleBias._numerics[1, 3] = 0.5f;

		GPUProjectedShadow shadow;
		shadow.WorldToShadowMatrix = scaleBias * viewProjection.Transpose();
		shadow.ShadowMapTextureIndex = 0; // filled in by ResolveTextureIndices
		shadow.ShadowHardness = 1.0f + light.Hardness * 4.0f;
		shadow.InvShadowMapRes = 1.0f / size;

		GPUProjectedShadows.Add( shadow );
		ProjectedShadowMaps.Add( target );
		ShadowsAllocated++;
		ProjectedShadowsRendered++;
		return (uint)(GPUProjectedShadows.Count - 1);
	}
}
