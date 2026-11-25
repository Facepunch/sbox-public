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
	internal const int TypePoint = 1, TypeDirectional = 2, TypeSpot = 3, TypeEnvironmentProbe = 5;
	internal const uint FlagMixedShadows = 16, FlagBaked = 32;
	const uint HasAttenuation0 = 1, HasAttenuation1 = 2, HasAttenuation2 = 4, DerivedValuesCalculated = 8;

	LightDescription desc;
	bool described;

	/// <summary>
	/// The light's description. A light managed code makes starts with native's defaults (<see cref="NewDescription"/>). For
	/// one native code made, the first time anything asks it's read from the native light; from then on managed code owns it,
	/// and the native light gets only what its bounds come from.
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
	/// directional light's far-away position and the falloff truncation. The bounds stay the native light's; a light without
	/// one works them out here as native's <c>UpdateBounds</c> would.
	/// </summary>
	void Recalculate()
	{
		RecalculateDerivedValues( ref desc );
		if ( !HasLight && described ) KeepBounds( DescriptionBounds( desc ) );
	}

	static void RecalculateDerivedValues( ref LightDescription d )
	{
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

		// The outer cone is what a native spot light's bounds come from
		if ( HasLight && phiChanged ) lightNative.SetPhi( phi );
	}

	/// <summary>A directional light's cascades, as native's setters set them (the count clamped to 0 to 4).</summary>
	internal void SetCascades( int count, float splitRatio, float distanceScale )
	{
		ref var d = ref Change();
		d.ShadowCascadeCount = Math.Clamp( count, 0, 4 );
		d.ShadowCascadeSplitRatio = splitRatio;
		d.CascadeDistanceScale = distanceScale;
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
		if ( HasLight ) lightNative.SetWorldDirection( rotation );
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

		NotifyChanged( Rendering.SceneObjectChange.Settings );
	}

	/// <summary>
	/// The description native made a light of <paramref name="type"/> with, for one managed code makes: <c>LightDesc_t::Clear</c>,
	/// then what <c>g_pSceneSystem</c>'s <c>CreatePointLight</c>, <c>CreateSpotLight</c>, <c>CreateDirectionalLight</c> (towards
	/// <paramref name="direction"/>) and <c>CreateEnvMap</c> do to it: <c>InitPoint</c>, <c>InitSpot</c> or <c>InitDirectional</c>
	/// with their colour, cone and constant attenuation (derived values worked out, then the constant attenuation cleared again
	/// for point and spot lights), three cascades for a sun, <c>InitEnvironmentProbe</c> with a 512 range for a cubemap.
	/// </summary>
	internal static LightDescription NewDescription( int type, Vector3 direction = default )
	{
		var d = new LightDescription
		{
			Type = type,
			CastShadows = 1,
			ShadowCascadeCount = 1,
			ShadowCascadeSplitRatio = 0.91f,
			CascadeDistanceScale = 1.0f,
			UsesIndexedBakedLighting = 1,
			BakeLightIndex = -1,
			BakeLightIndexScale = 1.0f,
			FogContributionStrength = 1.0f,
			RenderDiffuse = 1,
			RenderSpecular = 1,
			RenderTransmissive = 1,
			NearClipPlane = 1.0f,
		};

		if ( type == TypeEnvironmentProbe )
		{
			// InitEnvironmentProbe sets no attenuation; RecalculateDerivedValues with the default tolerance leaves its range
			d.Range = 512.0f;
			RecalculateDerivedValues( ref d );
			return d;
		}

		d.Color = new Vector3( 100, 0, 100 );
		d.Attenuation0 = 1.0f;

		if ( type == TypeSpot )
		{
			d.Direction = new Vector3( 1, 0, 0 ); // towards ( 10, 0, 0 ) from the origin
			d.Falloff = 5.0f;
			d.Theta = 15.0f;
			d.Phi = 30.0f;
		}
		else if ( type == TypeDirectional )
		{
			d.Direction = direction;
		}

		// With the constant attenuation on, the default truncation tolerance doesn't shorten the range
		RecalculateDerivedValues( ref d );

		if ( type == TypeDirectional ) d.ShadowCascadeCount = 3;
		else d.Attenuation0 = 0.0f;

		return d;
	}

	/// <summary>
	/// <c>LightDesc_t::RecalculateDerivedValues</c>'s bounds: a sphere's box for a point light or cubemap (grown by the light
	/// source's size), the cone's box clipped to its range's for a spot light, half the float range every way for a sun.
	/// </summary>
	internal static BBox DescriptionBounds( in LightDescription d )
	{
		switch ( d.Type )
		{
			case TypePoint:
			case TypeEnvironmentProbe:
				{
					var size = new Vector3( d.Range + d.LightSourceDim0 );
					var line = d.Direction * d.LightSourceDim1;
					line = new Vector3( MathF.Abs( line.x ), MathF.Abs( line.y ), MathF.Abs( line.z ) );
					return new BBox( d.Position - size - line, d.Position + size + line );
				}

			case TypeSpot:
				{
					VectorVectorsFLU( d.Direction, out var left, out var up );
					var (sinPhi, cosPhi) = MathF.SinCos( d.Phi * (MathF.PI / 180.0f) );
					var extents = d.Range * sinPhi;
					var mid = d.Position + d.Direction * d.Range * cosPhi;
					var far = d.Position + d.Direction * d.Range;

					var mins = d.Position;
					var maxs = d.Position;
					if ( d.Shape == 0 )
					{
						mins -= new Vector3( d.LightSourceDim0 );
						maxs += new Vector3( d.LightSourceDim0 );
					}

					for ( int c = 0; c < 2; c++ )
					{
						var centre = c == 0 ? mid : far;
						for ( int i = 0; i < 4; i++ )
						{
							var point = centre + left * ((i & 1) == 0 ? -extents : extents) + up * ((i & 2) == 0 ? -extents : extents);
							mins = Vector3.Min( point, mins );
							maxs = Vector3.Max( point, maxs );
						}
					}

					var radius = new Vector3( d.Range );
					return new BBox( Vector3.Max( mins, d.Position - radius ), Vector3.Min( maxs, d.Position + radius ) );
				}

			case TypeDirectional:
				return new BBox( new Vector3( -float.MaxValue / 2 ), new Vector3( float.MaxValue / 2 ) );
		}

		return new BBox( d.Position, d.Position );
	}

	/// <summary>Native's <c>VectorVectorsFLU</c>: left and up for a forward direction, z up unless it points straight up or down.</summary>
	static void VectorVectorsFLU( Vector3 forward, out Vector3 left, out Vector3 up )
	{
		if ( MathF.Abs( forward.x ) < 1e-3f && MathF.Abs( forward.y ) < 1e-3f )
		{
			left = new Vector3( 0, 1, 0 );
			up = new Vector3( -forward.z, 0, 0 );
			return;
		}

		left = Vector3.Cross( new Vector3( 0, 0, 1 ), forward ).Normal;
		up = Vector3.Cross( forward, left ).Normal;
	}
}
