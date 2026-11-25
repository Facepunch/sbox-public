using Borealis.Features;

namespace Sandbox.Rendering;

/// <summary>
/// A light packed for the light binner from its managed description (<see cref="SceneLight.Description"/>), as native's
/// <c>CLightBinnerStandard</c> packs a <c>LightDesc_t</c>: <c>PackGPULight</c>, or <c>PackBakedDirectionalGPULight</c> for a
/// directional light, shadows aside, with the light's modulator range.
/// </summary>
internal static class LightPacking
{
	// LightFlags in lightbinner_standard.h and lightbinner.hlsl
	const uint DiffuseEnabled = 0x2, SpecularEnabled = 0x4, TransmissiveEnabled = 0x8, LightCookiesEnabled = 0x40;
	const uint IndexedBakedLightingEnabled = 0x100, NoFogShadows = 0x200;

	/// <summary>LIGHTTYPE_FLAGS in native's lightdesc.h: a baked light's, and one whose shadows are mixed.</summary>
	internal const uint TypeFlagsMixedShadows = 16, TypeFlagsBaked = 32;

	// LightSourceShape_t and FogLightingMode_t in lightdesc.h
	const int ShapeSphere = 0;
	const int FogDynamic = 2, FogDynamicNoShadows = 3;

	/// <summary>
	/// Pack <paramref name="light"/>: its GPU light, LIGHTTYPE flags and bake light index. With a cookie the packed light
	/// says so, with no index: the binner sets the device's own from the texture.
	/// </summary>
	public static void Pack( SceneLight light, out LightBinnerFeature.GpuLight packed, out uint flags, out int bakeIndex )
	{
		ref readonly var d = ref light.Description;
		var cookie = light.LightCookie is { } texture && texture.IsValid() && !texture.IsError;
		packed = d.Type == SceneLight.TypeDirectional ? PackBakedDirectional( d ) : Pack( d, cookie );
		packed.LightModulatorOffset = d.LightModulatorOffset;
		packed.LightModulatorCount = d.LightModulatorCount;
		flags = d.Flags;
		bakeIndex = d.BakeLightIndex;
	}

	/// <summary><c>PackGPULight</c>.</summary>
	internal static LightBinnerFeature.GpuLight Pack( in SceneLight.LightDescription d, bool cookie )
	{
		var forward = d.Direction;
		var up = d.Up;
		var left = Normalized( Vector3.Cross( up, forward ) );

		var radius = d.PrecomputedMaxRange > 0.0f ? d.PrecomputedMaxRange : d.Range;

		uint lightFlags = 0;
		if ( d.RenderDiffuse != 0 ) lightFlags |= DiffuseEnabled;
		if ( d.RenderSpecular != 0 ) lightFlags |= SpecularEnabled;
		if ( d.RenderTransmissive != 0 ) lightFlags |= TransmissiveEnabled;
		if ( cookie ) lightFlags |= LightCookiesEnabled;
		if ( d.UsesIndexedBakedLighting != 0 ) lightFlags |= IndexedBakedLightingEnabled;

		var p = new LightBinnerFeature.GpuLight
		{
			LightCookieTextureIndex = 0xFFFFFFFF,
			Shape = (uint)d.Shape,
			Flags = lightFlags,
			Color = (d.Flags & SceneLight.FlagBaked) != 0 ? d.Color * d.BakeLightIndexScale : d.Color,
			Radius = radius,
			RadiusSquared = radius * radius,
			FalloffBias = d.Truncation,
			LinearFalloff = d.Attenuation1,
			QuadraticFalloff = d.Attenuation2,
			ShapeSize = new Vector2( d.LightSourceDim0, d.LightSourceDim1 ),
			Type = (uint)d.Type,
		};

		switch ( d.Type )
		{
			case SceneLight.TypeDirectional:
			case SceneLight.TypePoint:
				if ( d.Type == SceneLight.TypeDirectional )
				{
					p.Radius = float.MaxValue;
					p.RadiusSquared = float.MaxValue;
				}

				p.SpotLightInnerOuterConeCosines = new Vector4( 0.0f, -1.0f, 1.0f, 0.0f );

				// A pure point light has no rotation: its direction would vignette it
				if ( d.Shape == ShapeSphere )
				{
					forward = default;
					left = default;
					up = default;
				}
				break;

			case SceneLight.TypeSpot:
				p.SpotLightInnerOuterConeCosines = new Vector4( d.ThetaDot, d.PhiDot, 1.0f / MathF.Max( 0.01f, d.ThetaDot - d.PhiDot ), MathF.Tan( d.Phi * (MathF.PI / 180.0f) ) );
				break;
		}

		SetLightToWorld( ref p, forward, left, up, d.Position );

		p.FogIntensity = d.FogLightingMode is FogDynamic or FogDynamicNoShadows ? d.FogContributionStrength : 0.0f;
		if ( d.FogLightingMode == FogDynamicNoShadows ) p.Flags |= NoFogShadows;

		p.ShadowMapIndex = 0xFFFFFFFF;
		return p;
	}

	/// <summary><c>PackBakedDirectionalGPULight</c>: a directional light lightmaps and probes reference by its bake index.</summary>
	internal static LightBinnerFeature.GpuLight PackBakedDirectional( in SceneLight.LightDescription d )
	{
		uint lightFlags = 0;
		if ( d.RenderDiffuse != 0 ) lightFlags |= DiffuseEnabled;
		if ( d.RenderSpecular != 0 ) lightFlags |= SpecularEnabled;
		if ( d.RenderTransmissive != 0 ) lightFlags |= TransmissiveEnabled;

		var p = new LightBinnerFeature.GpuLight
		{
			Type = (uint)d.Type,
			Flags = lightFlags,
			Color = (d.Flags & SceneLight.FlagBaked) != 0 ? d.Color * d.BakeLightIndexScale : d.Color,
			Radius = float.MaxValue,
			RadiusSquared = float.MaxValue,
			SpotLightInnerOuterConeCosines = new Vector4( 0.0f, -1.0f, 1.0f, 0.0f ),
			ShadowMapIndex = 0xFFFFFFFF,
		};

		SetLightToWorld( ref p, d.Direction, Normalized( Vector3.Cross( d.Up, d.Direction ) ), d.Up, d.Position );
		return p;
	}

	/// <summary>Native's <c>VMatrix( forward, left, up )</c> with the position as its translation, row by row.</summary>
	static void SetLightToWorld( ref LightBinnerFeature.GpuLight p, Vector3 forward, Vector3 left, Vector3 up, Vector3 position )
	{
		p.Row0 = new Vector4( forward.x, left.x, up.x, position.x );
		p.Row1 = new Vector4( forward.y, left.y, up.y, position.y );
		p.Row2 = new Vector4( forward.z, left.z, up.z, position.z );
		p.Row3 = new Vector4( 0.0f, 0.0f, 0.0f, 1.0f );
	}

	/// <summary>Native's <c>Vector::Normalized</c>: a zero vector stays zero.</summary>
	static Vector3 Normalized( Vector3 v )
	{
		var length = v.Length;
		return length > 0.0f ? v / length : default;
	}
}
