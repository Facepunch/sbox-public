// fur.shader written as a surface: albedo, normal and roughness-metalness-occlusion with a fur noise that darkens the
// albedo and occlusion and cuts the strands out of the mesh's colour (always alpha-tested), a rim light from the view, and
// wind that moves vertices by the mesh's red channel and the view's blue noise. The mesh's colour is the surface's
// Varyings, linear or converted from sRGB per vertex as the material says.
//
// Both texture packings: materials authored before F_NEW_TEXTURE_PACKING (six textures, all but the noise read as sRGB,
// the normal map too) and the new one (colour with the noise in its alpha, normal, roughness-metalness-occlusion).
import sbox.surface;

// common_samplers.fxc's g_sBilinearWrap, for the wind's noise
SamplerState FurBilinearWrap < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

public struct FurVaryings
{
	public float4 Color : COLOR0;
}

[Material, AlphaTest]
public struct Fur : IInterpolatedVertexSurface
{
	typealias Varyings = FurVaryings;

	// Features
	[Group( "Features" ), Source( "F_NEW_TEXTURE_PACKING" )] bool NewTexturePacking;
	[Group( "Features" ), Source( "F_RENDER_BACKFACES" ), RenderState( "TwoSided" )] bool RenderBackfaces;

	// The new packing
	[Group( "Material" ), Srgb] Texture2D BaseColor;   // rgb albedo, a fur noise (both packings' albedo)
	[Group( "Material" )] Texture2D Normal;
	[Group( "Material" )] Texture2D RMA;

	// The old packing: one texture each, the normal map and masks read as sRGB as the .shader declares them
	[Group( "Legacy" )] Texture2D FurNoise;
	[Group( "Legacy" ), Srgb, Source( "g_tNormal" )] Texture2D LegacyNormal;
	[Group( "Legacy" ), Srgb] Texture2D Roughness;
	[Group( "Legacy" ), Srgb] Texture2D Metalness;
	[Group( "Legacy" ), Srgb] Texture2D AO;

	[Group( "Material" ), Default( 0 ), Range( 0, 1 )] float AOAmount;
	[Group( "Material" ), Default( 0 ), Range( 0, 1 )] float NoiseAOAmount;

	[Group( "Fur" ), Default( 1 ), Range( 0, 64 )] float NoiseTiling;
	[Group( "Fur" ), Default( 0 ), Range( 0, 1 )] float NoiseAlbedoMultiply;
	[Group( "Fur" ), Default( 0.01 ), Range( 0, 1 )] float MinClipFudge;

	[Group( "Rim Lighting" ), Default3( 0.25, 0.25, 0.25 )] float3 RimColour;
	[Group( "Rim Lighting" ), Default( 0.84 ), Range( 0, 10 )] float RimPower;
	[Group( "Rim Lighting" ), Default( 0.01 ), Range( 0.01, 1 )] float RimFudge;
	[Group( "Rim Lighting" ), Default( 1 )] bool UseGeometryNormals;

	// Wind
	[Group( "Wind Displacement" ), Default( 0.36 ), Range( 0, 100 )] float WindFreq;
	[Group( "Wind Displacement" ), Default( 0.36 ), Range( 0, 10 )] float WindNoise;
	[Group( "Wind Displacement" ), Default( 0.36 ), Range( 0, 100 )] float Wind;

	// The mesh's colours were exported in sRGB (alpha stays as it is)
	[Group( "Vertex Colors" ), Default( 0 )] bool SrgbToLinearColorSpace;

	public FurVaryings Vertex( FurVaryings v )
	{
		if ( SrgbToLinearColorSpace ) v.Color.rgb = SrgbGammaToLinear( v.Color.rgb );
		return v;
	}

	public FurVaryings Displace( inout WorldVertexInput v, FurVaryings values )
	{
		float2 noiseUv = v.Uv * WindFreq + v.Time * 0.025 * WindNoise;
		float noise = v.BlueNoise.SampleLevel( FurBilinearWrap, noiseUv, 0 ).r;
		v.Position += values.Color.r * noise * Wind;
		return values;
	}

	public Material Evaluate( SurfaceInput i, FurVaryings v )
	{
		float2 scaledUv = i.Uv * NoiseTiling;

		float fur;
		float3 albedo = i.Sample( BaseColor, i.Uv ).rgb;
		float3 normal;
		float3 rma;
		if ( NewTexturePacking )
		{
			fur = i.Sample( BaseColor, scaledUv ).a;
			normal = i.Sample( Normal, i.Uv ).rgb;
			rma = i.Sample( RMA, i.Uv ).rgb;
		}
		else
		{
			fur = i.Sample( FurNoise, scaledUv ).r;
			normal = i.Sample( LegacyNormal, i.Uv ).rgb;
			rma = float3( i.Sample( Roughness, i.Uv ).r, i.Sample( Metalness, i.Uv ).r, i.Sample( AO, i.Uv ).r );
		}

		// Ambient occlusion contributors
		rma.b = lerp( 1, rma.b, AOAmount );
		rma.b = lerp( rma.b, rma.b * fur, NoiseAOAmount );

		float3 n = TransformNormal( DecodeNormal( normal ), i.Normal, i.TangentU, i.TangentV );

		// Rim light from the view
		float3 fresnelNormal = UseGeometryNormals ? i.Normal : n;
		float fresnel = pow( 1.0 - dot( fresnelNormal, i.View ), RimPower );
		float3 rim = RimColour * saturate( fresnel / RimFudge );

		albedo = lerp( albedo, albedo * fur, NoiseAlbedoMultiply );
		albedo += rim;
		albedo *= i.Color.rgb;

		Material m = Material.Init( i );
		m.Albedo = albedo;
		m.Normal = n;
		m.Roughness = rma.r;
		m.Metalness = rma.g;
		m.AmbientOcclusion = rma.b;

		// The strands: the noise against the mesh's red channel
		float clipFudge = fur * ( 1 - MinClipFudge ) + MinClipFudge;
		m.Opacity = saturate( clipFudge - v.Color.r + 0.5 );
		return m;
	}
}
