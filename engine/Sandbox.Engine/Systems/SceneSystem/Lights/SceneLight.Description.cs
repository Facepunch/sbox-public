using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class SceneLight
{
	/// <summary>
	/// A light's description, as native's <c>LightDesc_t</c> holds it, with the light's modulator range. Field for field
	/// <c>SceneLightDescription_t</c> in <c>rendertools.cpp</c>, which reads it from the native light once
	/// (<see cref="Description"/>); managed code owns it from then on and Borealis packs it (<c>LightPacking</c>).
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct LightDescription
	{
		public int Type;
		public Vector3 Color;
		public float Range, Falloff, Attenuation0, Attenuation1, Attenuation2, Theta, Phi;
		public int CastShadows, ShadowWidth, ShadowHeight, ShadowCascadeCount;
		public float ShadowCascadeSplitRatio, CascadeDistanceScale;
		public int UsesIndexedBakedLighting, BakeLightIndex;
		public float BakeLightIndexScale;
		public int FogLightingMode;
		public float FogContributionStrength;
		public int RenderDiffuse, RenderSpecular, RenderTransmissive, Shape;
		public float LightSourceDim0, LightSourceDim1, LightSourceSize0, LightSourceSize1, PrecomputedMaxRange, NearClipPlane;
		public Vector3 Position, Direction, Up;
		public uint Flags;
		public float ThetaDot, PhiDot, Truncation;
		public uint LightModulatorOffset, LightModulatorCount;
	}

	// MATERIAL_LIGHT_* and LIGHTTYPE_* in native's lightdesc.h
	internal const int TypePoint = 1, TypeDirectional = 2, TypeSpot = 3;
	internal const uint FlagMixedShadows = 16, FlagBaked = 32;
	const uint HasAttenuation0 = 1, HasAttenuation1 = 2, HasAttenuation2 = 4, DerivedValuesCalculated = 8;

	LightDescription desc;
	bool described;

	/// <summary>
	/// The light's description. The first time anything asks it's read from the native light, which has what it was made
	/// with by then (native's defaults, or a map's settings); from then on managed code owns it, and the native light is
	/// written only while something reads it (<see cref="SceneObject.WritesNative"/>), apart from what its bounds come from.
	/// </summary>
	internal ref readonly LightDescription Description
	{
		get
		{
			Describe();
			return ref desc;
		}
	}

	unsafe void Describe()
	{
		if ( described || lightNative.IsNull ) return;

		LightDescription read = default;
		if ( !RenderTools.ReadSceneLightDescription( this, (IntPtr)(&read) ) ) return;

		desc = read;
		described = true;
	}

	/// <summary>
	/// Light as <paramref name="native"/> does, a light native code made and keeps changing (a native tool's, through
	/// <c>GetLightDescForModify</c>) that managed code only draws: its description read from native again and copied here
	/// whole (attenuation, falloff, range, cone, shadows), keeping this light's modulators. False when nothing changed.
	/// </summary>
	internal bool CopyNativeDescription( SceneLight native )
	{
		native.described = false;
		native.Describe();
		if ( !native.described ) return false;

		Describe();
		var copy = native.desc;
		copy.LightModulatorOffset = desc.LightModulatorOffset;
		copy.LightModulatorCount = desc.LightModulatorCount;
		if ( MemoryMarshal.AsBytes( new ReadOnlySpan<LightDescription>( in copy ) ).SequenceEqual( MemoryMarshal.AsBytes( new ReadOnlySpan<LightDescription>( in desc ) ) ) ) return false;

		desc = copy;
		described = true;
		NotifyChanged( Rendering.SceneObjectChange.Settings );
		return true;
	}

	/// <summary>The description to change: read from native first if it hasn't been.</summary>
	ref LightDescription Change()
	{
		Describe();
		return ref desc;
	}

	/// <summary>
	/// What native works out from the description when one of its bounds-affecting values changes
	/// (<c>LightDesc_t::RecalculateDerivedValues</c> with no truncation tolerance): the attenuation flags, the cone cosines, a
	/// directional light's far-away position and the falloff truncation. The bounds stay the native light's.
	/// </summary>
	void Recalculate()
	{
		ref var d = ref desc;
		d.Flags &= ~(HasAttenuation0 | HasAttenuation1 | HasAttenuation2);
		d.Flags |= DerivedValuesCalculated;
		if ( d.Attenuation0 != 0 ) d.Flags |= HasAttenuation0;
		if ( d.Attenuation1 != 0 ) d.Flags |= HasAttenuation1;
		if ( d.Attenuation2 != 0 ) d.Flags |= HasAttenuation2;

		if ( d.Type == TypeSpot )
		{
			d.ThetaDot = MathF.Cos( d.Theta * (MathF.PI / 180.0f) );
			d.PhiDot = MathF.Cos( d.Phi * (MathF.PI / 180.0f) );
		}
		else
		{
			d.ThetaDot = 1.0f;
			d.PhiDot = 1.0f;
		}

		if ( d.Type == TypeDirectional ) d.Position = d.Direction * 2.0e6f;

		d.Truncation = d.Attenuation0 > 0.0f || d.Range <= 0.0f ? 0.0f : 1.0f / (1.1920929e-7f + d.Range * d.Attenuation1 + d.Range * d.Range * d.Attenuation2);
	}

	/// <summary>A spot light's cone and falloff, as native's <c>SetTheta</c>, <c>SetPhi</c> and <c>SetFallOff</c> set them.</summary>
	internal void SetSpot( float theta, float phi, float falloff )
	{
		ref var d = ref Change();
		d.Theta = theta;
		d.Falloff = falloff;
		var phiChanged = d.Phi != phi;
		d.Phi = phi;
		Recalculate();

		if ( WritesNative )
		{
			lightNative.SetTheta( theta );
			lightNative.SetFallOff( falloff );
		}

		// The outer cone is what a spot light's bounds come from
		if ( phiChanged || WritesNative ) lightNative.SetPhi( phi );
	}

	/// <summary>A directional light's cascades, as native's setters set them (the count clamped to 0 to 4).</summary>
	internal void SetCascades( int count, float splitRatio, float distanceScale )
	{
		ref var d = ref Change();
		d.ShadowCascadeCount = Math.Clamp( count, 0, 4 );
		d.ShadowCascadeSplitRatio = splitRatio;
		d.CascadeDistanceScale = distanceScale;
		if ( !WritesNative ) return;
		lightNative.SetShadowCascades( count );
		lightNative.SetShadowCascadeSplitRatio( splitRatio );
		lightNative.SetCascadeDistanceScale( distanceScale );
	}

	/// <summary>Point the light as native's <c>CSceneLightObject::SetWorldDirection</c> does.</summary>
	internal void SetWorldDirection( Rotation rotation )
	{
		ref var d = ref Change();
		var forward = rotation.Forward;
		if ( d.Type == TypeDirectional ) forward = -forward;
		d.Direction = NormalizedSafe( forward, new Vector3( 1, 0, 0 ) );
		d.Up = NormalizedSafe( rotation.Up, new Vector3( 0, 0, 1 ) );
		Recalculate();
		lightNative.SetWorldDirection( rotation );
		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	static Vector3 NormalizedSafe( Vector3 v, Vector3 fallback )
	{
		var length = v.Length;
		return length > 0.0f ? v / length : fallback;
	}

	/// <summary>
	/// Map light settings that the components don't expose (<see cref="Light.LegacyLightData"/>): baking flags and index,
	/// fog, diffuse and specular, and attenuation, falloff and cookie as native takes them.
	/// </summary>
	internal void ApplyLegacy( in Light.LegacyLightData data )
	{
		ref var d = ref Change();

		// The lightbinner gates realtime shadow maps for baked lights on MIXED_SHADOWS, so only Stationary may set it - Baked
		// gets its shadows from the lightmap
		switch ( data.DirectLight )
		{
			case 3: // HAMMER_DIRECT_LIGHT_STATIONARY
				d.Flags |= FlagMixedShadows | FlagBaked;
				break;
			case 1: // HAMMER_DIRECT_LIGHT_BAKED
				d.Flags |= FlagBaked;
				break;
		}

		d.BakeLightIndex = data.BakeLightIndex;
		d.BakeLightIndexScale = data.BakeLightIndexScale;
		d.UsesIndexedBakedLighting = data.BakedLightIndexing ? 1 : 0;
		d.FogContributionStrength = data.FogContributionStrength;
		d.FogLightingMode = data.FogLighting;
		d.RenderDiffuse = data.RenderDiffuse ? 1 : 0;
		d.RenderSpecular = data.RenderSpecular ? 1 : 0;
		_renderDiffuse = data.RenderDiffuse;
		_renderSpecular = data.RenderSpecular;

		var recalculate = false;
		if ( data.ConstantAttenuation is { } constant ) { d.Attenuation0 = constant; recalculate = true; }
		if ( data.LinearAttenuation is { } linear ) { d.Attenuation1 = linear; recalculate = true; }
		if ( data.QuadraticAttenuation is { } quadratic ) { d.Attenuation2 = quadratic; recalculate = true; }
		if ( data.FallOff is { } fallOff ) { d.Falloff = fallOff; recalculate = true; }
		if ( recalculate ) Recalculate();
		if ( data.Cookie is not null ) _lightCookie = data.Cookie;

		if ( WritesNative )
		{
			var light = lightNative;
			light.SetLightFlags( d.Flags );
			light.SetBakeLightIndex( d.BakeLightIndex );
			light.SetBakeLightIndexScale( d.BakeLightIndexScale );
			light.SetUsesIndexedBakedLighting( data.BakedLightIndexing );
			light.SetFogContributionStength( d.FogContributionStrength );
			light.SetRenderDiffuse( data.RenderDiffuse );
			light.SetRenderSpecular( data.RenderSpecular );
			light.SetFogLightingMode( d.FogLightingMode );
			if ( data.ConstantAttenuation is { } c ) light.SetConstantAttn( c );
			if ( data.LinearAttenuation is { } l ) light.SetLinearAttn( l );
			if ( data.QuadraticAttenuation is { } q ) light.SetQuadraticAttn( q );
			if ( data.FallOff is { } f ) light.SetFallOff( f );
			if ( data.Cookie is not null ) light.SetLightCookie( data.Cookie.native );
		}

		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}
}
