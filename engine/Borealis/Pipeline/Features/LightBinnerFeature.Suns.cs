using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// The extra suns (<see cref="SceneLighting.ExtraSuns"/>): a short list after the binned lights that every pixel loops over,
/// <c>NumLights.x + NumLights.y</c> on, as many as <c>g_DirectionalLightDirection.w</c> says. A baked one also gets an entry
/// its bake index maps to, after them, and is flagged indexed in the list: baked surfaces reach it by its index instead.
/// </summary>
internal sealed partial class LightBinnerFeature
{
	const uint LightTypeDirectional = 2;
	const uint IndexedLight = 0x100;

	/// <summary>
	/// <c>LightFlags::ExtraSun</c> in lightbinner.hlsl: a directional light that isn't the sun, shadowed by its own map
	/// (<see cref="GpuLight.ShadowMapIndex"/>, a projected shadow) instead of the sun's cascades.
	/// </summary>
	internal const uint ExtraSunFlag = 0x400;

	/// <summary>
	/// How far towards the light a sun sits, as native places one (<c>SceneLight.Description</c>'s position).
	/// </summary>
	const float SunDistance = 2.0e6f;

	/// <summary>
	/// Extra suns packed for the current view, after the binned lights.
	/// </summary>
	public int ExtraSunCount { get; private set; }

	readonly int[] extraSunSources = new int[SceneLighting.MaxExtraSuns];
	readonly int[] extraSunBakedSlots = new int[SceneLighting.MaxExtraSuns];

	/// <summary>
	/// Where extra sun <paramref name="sun"/> is in <see cref="SceneLighting.ExtraSuns"/>.
	/// </summary>
	internal int ExtraSunSource( int sun ) => extraSunSources[sun];

	/// <summary>
	/// Assign extra sun <paramref name="sun"/>'s shadow (a projected shadow index), to its baked entry too.
	/// </summary>
	internal void SetExtraSunShadow( int sun, uint shadowIndex )
	{
		lights[Count + sun].ShadowMapIndex = shadowIndex;
		if ( extraSunBakedSlots[sun] >= 0 ) lights[extraSunBakedSlots[sun]].ShadowMapIndex = shadowIndex;
	}

	/// <summary>
	/// Pack the enabled extra suns after the binned lights, then the baked ones' entries, mapping their bake indices.
	/// </summary>
	void PackExtraSuns( SceneLighting lighting )
	{
		var suns = CollectionsMarshal.AsSpan( lighting.ExtraSuns );
		var count = 0;
		for ( int i = 0; i < suns.Length && count < SceneLighting.MaxExtraSuns; i++ )
		{
			if ( !suns[i].Enabled ) continue;
			lights[Count + extraLights++] = PackExtraSun( suns[i] );
			extraSunSources[count++] = i;
		}

		ExtraSunCount = count;

		for ( int k = 0; k < count; k++ )
		{
			ref readonly var sun = ref suns[extraSunSources[k]];
			extraSunBakedSlots[k] = -1;
			if ( sun.Baked is not { } baked || sun.BakeIndex < 0 || sun.BakeIndex >= bakedIndexMapping.Length ) continue;

			var slot = Count + extraLights++;
			baked.Flags |= ExtraSunFlag;
			baked.ShadowMapIndex = 0xFFFFFFFF;
			lights[slot] = baked;
			bakedIndexMapping[sun.BakeIndex] = slot;
			extraSunBakedSlots[k] = slot;
			lights[Count + k].Flags |= IndexedLight;
		}
	}

	/// <summary>
	/// An extra sun laid out as <c>PackBakedDirectionalGPULight</c> lays a sun out: forward towards the light, placed far
	/// along it. The light loop reads its colour and forward, as the sun's own path reads the sun's constants.
	/// </summary>
	internal static GpuLight PackExtraSun( in ExtraSun sun )
	{
		var forward = -sun.Direction.Normal;
		var up = MathF.Abs( forward.z ) < 0.999f ? Vector3.Up : Vector3.Forward;
		var left = Vector3.Cross( up, forward ).Normal;
		up = Vector3.Cross( forward, left );
		var position = forward * SunDistance;

		return new GpuLight
		{
			Type = LightTypeDirectional,
			Flags = DiffuseEnabled | SpecularEnabled | TransmissiveEnabled | ExtraSunFlag,
			Row0 = new Vector4( forward.x, left.x, up.x, position.x ),
			Row1 = new Vector4( forward.y, left.y, up.y, position.y ),
			Row2 = new Vector4( forward.z, left.z, up.z, position.z ),
			Row3 = new Vector4( 0, 0, 0, 1 ),
			Color = new Vector3( sun.Color.r, sun.Color.g, sun.Color.b ),
			Radius = float.MaxValue,
			RadiusSquared = float.MaxValue,
			SpotLightInnerOuterConeCosines = new Vector4( 0.0f, -1.0f, 1.0f, 0.0f ),
			ShadowMapIndex = 0xFFFFFFFF,
			LightCookieTextureIndex = 0xFFFFFFFF,
		};
	}
}
