StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
StaticCombo( S_SOURCE_LIGHTMAP, F_SOURCE_LIGHTMAP, Sys( ALL ) );
StaticCombo( S_SOURCE_LIGHTMAP_HDR, F_SOURCE_LIGHTMAP_HDR, Sys( ALL ) );
StaticCombo( S_SOURCE_PROP_LIGHTING, F_SOURCE_PROP_LIGHTING, Sys( ALL ) );
StaticCombo( S_SOURCE_FRAMEBUFFER, F_SOURCE_FRAMEBUFFER, Sys( ALL ) );

#define BLEND_MODE_ALREADY_SET
#if S_MODE_DEPTH && S_TRANSLUCENT
	#define DEPTH_STATE_ALREADY_SET
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, true );
#endif
#define CULL_MODE_ALREADY_SET
#include "common/pixel.hlsl"
#include "source1/frames.hlsl"
#if S_MODE_DEPTH
	RenderState( BlendEnable, false );
#elif S_TRANSLUCENT
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	#if S_ADDITIVE_BLEND
		RenderState( DstBlend, ONE );
	#else
		RenderState( DstBlend, INV_SRC_ALPHA );
	#endif
#endif
#if S_ALPHA_TEST && !S_TRANSLUCENT
	RenderState( AlphaToCoverageEnable, S_MODE_DEPTH != 0 );
	#if !S_MODE_DEPTH
		RenderState( DepthFunc, EQUAL );
		RenderState( DepthWriteEnable, false );
	#endif
#endif
BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );

SamplerState g_sColor < Attribute( "g_tColorSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sNormal < Attribute( "g_tNormalSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sDetail < Attribute( "g_tDetailSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sEnvMask < Attribute( "g_tEnvMaskSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sSelfIllumMask < Attribute( "g_tSelfIllumMaskSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sPhongExponent < Attribute( "g_tPhongExponentSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
#if S_SOURCE_TRANSITION
	SamplerState g_sColor2 < Attribute( "g_tColor2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sNormal2 < Attribute( "g_tNormal2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sBlendModulate < Attribute( "g_tBlendModulateSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
#endif
#define g_sSourceClamp g_sBilinearClamp
SamplerState g_sSourceCube < Attribute( "g_tEnvCubeSampler" ); Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
SamplerState g_sEnvSphere < Attribute( "g_tEnvSphereSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
#define g_sSourcePropLightmap g_sBilinearWrap
#if S_SOURCE_VORTWARP
	SamplerState g_sWarpFlow < Attribute( "g_tSourceWarpFlowSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sWarpEmission < Attribute( "g_tSourceWarpEmissionSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
#endif
#if S_SOURCE_MONITOR
	SamplerState g_sTexture2 < Attribute( "g_tTexture2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
#endif
BoolAttribute( bWantsFBCopyTexture, S_SOURCE_FRAMEBUFFER != 0 );
Texture2D g_tSourceScene < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

CreateInputTexture2D( SourceColor, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceColor2, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
CreateInputTexture2D( SourceNormal2, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
CreateInputTexture2D( SourceDetail, Linear, 8, "None", "_detail", "Source 1", Default4( 0.5, 0.5, 0.5, 1 ) );
CreateInputTexture2D( SourceEnvMask, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceSelfIllumMask, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourcePhongExponent, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceBlendModulate, Linear, 8, "None", "_mask", "Source 1", Default4( 0.5, 0.5, 0, 1 ) );
#if S_SOURCE_LIGHTMAP_HDR
	CreateInputTexture2D( SourceLightmap, Linear, 16, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap1, Linear, 16, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap2, Linear, 16, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap3, Linear, 16, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
#else
	CreateInputTexture2D( SourceLightmap, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap1, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap2, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceLightmap3, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
#endif
CreateInputTextureCube( SourceEnvCube, Srgb, 8, "None", "_cube", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceEnvSphere, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceWarpFlow, Linear, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceWarpEmission, Linear, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceTexture2, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourcePropLightmap, Linear, 16, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );

#if S_SOURCE_TEXTURE_ANIMATION
	Texture2DArray g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#else
	Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
#endif
Texture2D g_tColor2 < Channel( RGBA, Box( SourceColor2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
Texture2D g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tNormal2 < Channel( RGBA, Box( SourceNormal2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tDetail < Channel( RGBA, Box( SourceDetail ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tEnvMask < Channel( RGBA, Box( SourceEnvMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tSelfIllumMask < Channel( RGBA, Box( SourceSelfIllumMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tPhongExponent < Channel( RGBA, Box( SourcePhongExponent ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tBlendModulate < Channel( RGBA, Box( SourceBlendModulate ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#if S_SOURCE_LIGHTMAP_HDR
	Texture2D g_tSourceLightmap < Channel( RGBA, Box( SourceLightmap ), Linear ); OutputFormat( RGBA16161616 ); SrgbRead( false ); >;
	Texture2D g_tSourceLightmap1 < Channel( RGBA, Box( SourceLightmap1 ), Linear ); OutputFormat( RGBA16161616 ); SrgbRead( false ); >;
	Texture2D g_tSourceLightmap2 < Channel( RGBA, Box( SourceLightmap2 ), Linear ); OutputFormat( RGBA16161616 ); SrgbRead( false ); >;
	Texture2D g_tSourceLightmap3 < Channel( RGBA, Box( SourceLightmap3 ), Linear ); OutputFormat( RGBA16161616 ); SrgbRead( false ); >;
#else
	Texture2D g_tSourceLightmap < Channel( RGBA, Box( SourceLightmap ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
	Texture2D g_tSourceLightmap1 < Channel( RGBA, Box( SourceLightmap1 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
	Texture2D g_tSourceLightmap2 < Channel( RGBA, Box( SourceLightmap2 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
	Texture2D g_tSourceLightmap3 < Channel( RGBA, Box( SourceLightmap3 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
#endif
TextureCube g_tEnvCube < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
Texture2D g_tEnvSphere < Channel( RGBA, Box( SourceEnvSphere ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
float g_flSourceHasEnvSphere < Default( 0 ); >;
Texture2D g_tSourceWarpFlow < Channel( RGBA, Box( SourceWarpFlow ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tSourceWarpEmission < Channel( RGBA, Box( SourceWarpEmission ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
float g_flSourceWarpEmission < Default( 0 ); >;
Texture2D g_tTexture2 < Channel( RGBA, Box( SourceTexture2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
float g_flSourceMonitor < Default( 0 ); >;
float3 g_vSourceMonitorContrast < Default3( 0, 0, 0 ); >;
float3 g_vSourceMonitorSaturation < Default3( 1, 1, 1 ); >;
float3 g_vSourceMonitorTint < Default3( 1, 1, 1 ); >;
float3 g_vSourceTexture2TransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceTexture2TransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceTexture2Scroll < Default3( 0, 0, 1 ); >;
Texture2D g_tSourcePropLightmap < Channel( RGBA, Box( SourcePropLightmap ), Linear ); OutputFormat( RGBA16161616F ); SrgbRead( false ); >;

TextureAttribute( RepresentativeTexture, g_tColor );
TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

float3 g_vSourceBaseTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceBaseTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceBase2TransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceBase2TransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceNormalTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceNormalTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceNormal2TransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceNormal2TransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceDetailTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceDetailTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceEnvMaskTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceEnvMaskTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceBlendTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceBlendTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceBaseScroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceBase2Scroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceNormalScroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceNormal2Scroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceDetailScroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceEnvMaskScroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceBlendScroll < Default3( 0, 0, 1 ); >;

float3 g_vSourceColorTint < Default3( 1, 1, 1 ); >;
float g_flSourceBlendTintByBaseAlpha < Default( 0 ); >;
float g_flSourceBlendTintColorOverBase < Default( 0 ); >;
float g_flSourceAlphaTestReference < Default( 0.5 ); >;
float g_flSourceUseBaseAlpha < Default( 1 ); >;
float4 g_vSourceTextureAlphaPresence < Default4( 1, 1, 1, 1 ); >;
float g_flSourceDetailAlphaPresent < Default( 1 ); >;
float g_flSourceVertexColor < Default( 0 ); >;
float g_flSourceVertexAlpha < Default( 0 ); >;
float g_flSourceHasNormal < Default( 0 ); >;
float g_flSourceHasNormal2 < Default( 0 ); >;
float g_flSourceNormalScale < Default( 1 ); >;
float g_flSourceNormal2Scale < Default( 1 ); >;
float g_flSourceNormalYSign < Default( 1 ); >;
int g_nSourceNormalEncoding < Default( 0 ); >;
int g_nSourceNormal2Encoding < Default( 0 ); >;
float g_flSourceSsBump < Default( 0 ); >;
float g_flSourceSsBump2 < Default( 0 ); >;
float g_flSourceHasBlendModulate < Default( 0 ); >;
float g_flSourceBlendFactor < Default( 1 ); >;

float g_flSourceHasDetail < Default( 0 ); >;
int g_nSourceDetailMode < Default( 0 ); >;
float g_flSourceDetailBlendFactor < Default( 1 ); >;
float3 g_vSourceDetailTint < Default3( 1, 1, 1 ); >;
float g_flSourceDetailSrgb < Default( 0 ); >;

float g_flSourceSelfIllum < Default( 0 ); >;
float g_flSourceReceiveDecals < Default( 1 ); >;
float g_flSourceHasSelfIllumMask < Default( 0 ); >;
float3 g_vSourceSelfIllumTint < Default3( 1, 1, 1 ); >;
float g_flSourceSelfIllumFresnel < Default( 0 ); >;
float3 g_vSourceSelfIllumFresnel < Default3( 0, 1, 1 ); >;

float g_flSourceSkin < Default( 0 ); >;
float g_flSourcePhong < Default( 0 ); >;
float g_flSourcePhongExponent < Default( 0 ); >;
float g_flSourcePhongExponentFactor < Default( 0 ); >;
float g_flSourceHasPhongExponent < Default( 0 ); >;
float g_flSourcePhongBoost < Default( 1 ); >;
float3 g_vSourcePhongTint < Default3( 0, 0, 0 ); >;
float g_flSourcePhongAlbedoTint < Default( 0 ); >;
int g_nSourcePhongMask < Default( 2 ); >;
float g_flSourcePhongMaskInvert < Default( 0 ); >;

float g_flSourceEnvMap < Default( 0 ); >;
float g_flSourceHasEnvCube < Default( 0 ); >;
float g_flSourceHasEnvMask < Default( 0 ); >;
int g_nSourceEnvMask < Default( 0 ); >;
float g_flSourceEnvMaskInvert < Default( 0 ); >;
float3 g_vSourceEnvTint < Default3( 1, 1, 1 ); >;
float3 g_vSourceEnvContrast < Default3( 0, 0, 0 ); >;
float3 g_vSourceEnvSaturation < Default3( 1, 1, 1 ); >;
float g_flSourceEnvRoughness < Default( 0 ); >;
float g_flSourceEnvMip < Default( 0 ); >;
float g_flSourceLightmapScale < Default( 1 ); >;
float g_flSourceHasBumpLightmap < Default( 0 ); >;
float g_flSourceDiffuseBumpLighting < Default( 1 ); >;
float g_flSourceDirectLightScale < Default( 1 ); >;
int g_nSourcePropLighting < Default( 0 ); >;
bool g_flSourceBakedSunShadows < Default( 0 ); >;
float3 g_vSourceBakedShadowTint < Default3( 0.73333333, 0.73333333, 0.73333333 ); >;

#include "source1/uv.hlsl"
#include "source1/map/prop_lighting.hlsl"

float3 SourceNormalize( float3 normal )
{
	float lengthSquared = dot( normal, normal );
	return lengthSquared > 1e-8 ? normal * rsqrt( max( lengthSquared, 1e-8 ) ) : float3( 0, 0, 1 );
}

float3 SourceSsBumpNormal( float3 weights )
{
	return SourceNormalize( float3( 0.81649658 * weights.r - 0.40824829 * ( weights.g + weights.b ),
		0.70710678 * ( weights.g - weights.b ), 0.57735027 * dot( weights, float3( 1, 1, 1 ) ) ) );
}

float3 SourceNormal( inout float4 texel, float ssbump, float strength, int encoding )
{
	float3 normal = ssbump > 0.5 ? SourceSsBumpNormal( texel.rgb ) : texel.rgb * 2 - 1;
	if ( ssbump <= 0.5 && encoding == 1 )
	{
		normal.z = sqrt( saturate( 1 - dot( normal.xy, normal.xy ) ) );
		texel.a = 1;
	}
	normal.xy *= float2( strength, strength * g_flSourceNormalYSign );
	return SourceNormalize( normal );
}

float SourceMask( int mode, float4 color, float normalAlpha )
{
	if ( mode == 1 ) return color.a;
	if ( mode == 2 ) return normalAlpha;
	if ( mode == 3 ) return dot( color.rgb, float3( 0.299, 0.587, 0.114 ) );
	return 1;
}

bool SourceUsesSkin()
{
	return g_flSourceSkin > 0.5 || g_flSourcePhong > 0;
}

bool SourceUsesBlendTint()
{
	return g_flSourceBlendTintByBaseAlpha > 0.5 && g_flSourceSelfIllum <= 0;
}

float3 SourceBlendTintColor( float3 albedo, float alpha, float3 tint )
{
	float3 tintedColor = lerp( albedo * tint, tint, g_flSourceBlendTintColorOverBase );
	return lerp( albedo, tintedColor, alpha );
}

bool SourceOwnsBaseAlpha()
{
	if ( SourceUsesBlendTint() )
		return true;
	if ( g_flSourceSelfIllum > 0 || ( SourceUsesSkin() && g_nSourcePhongMask == 1 ) )
		return true;
	if ( g_flSourceEnvMap <= 0 )
		return false;
	if ( SourceUsesSkin() )
		return g_nSourceEnvMask != 2 || g_nSourcePhongMask == 1;
	return g_nSourceEnvMask == 1;
}

void SourceDetail( inout float4 color, float4 detail, inout float3 normal, out float3 emission )
{
	float amount = saturate( SourceProxyScalar( g_flSourceDetailBlendFactor, g_flSourceDetailBlendProxy ) );
	emission = 0;
	switch ( g_nSourceDetailMode )
	{
		case 0: color.rgb *= lerp( float3( 1, 1, 1 ), detail.rgb * 2, amount ); break;
		case 1: color.rgb += detail.rgb * amount; break;
		case 2: color.rgb = lerp( color.rgb, detail.rgb, detail.a * amount ); break;
		case 3: color = lerp( color, detail, amount ); break;
		case 4:
			color.rgb = lerp( color.rgb, detail.rgb, ( 1 - color.a ) * amount );
			color.a = detail.a;
			break;
		case 5: emission = detail.rgb * amount; break;
		case 6:
		{
			float multiplier = amount >= 0.5 ? rcp( max( amount, 0.001 ) ) : 4 * amount;
			float offset = amount >= 0.5 ? 1 - multiplier : -0.5 * multiplier;
			emission = saturate( detail.rgb * multiplier + offset );
			break;
		}
		case 7: color.rgb *= lerp( 1, 2 * lerp( detail.r, detail.a, color.a ), amount ); break;
		case 8: color *= lerp( float4( 1, 1, 1, 1 ), detail, amount ); break;
		case 9: color.a *= lerp( 1, detail.a, amount ); break;
		case 10:
		{
			#if !S_SOURCE_LIGHTMAP
			float3 detailNormal = SourceSsBumpNormal( detail.rgb );
			normal = SourceNormalize( lerp( normal, float3( normal.xy + detailNormal.xy, normal.z * detailNormal.z ), amount ) );
			color.rgb *= lerp( 1, dot( detail.rgb, float3( 0.66666667, 0.66666667, 0.66666667 ) ), amount );
			#endif
			break;
		}
		case 11: color.rgb *= dot( detail.rgb, float3( 0.66666667, 0.66666667, 0.66666667 ) ); break;
		case 12: color.rgb = saturate( saturate( color.rgb * 2 ) * detail.a + 1 - detail.a ) * detail.rgb; break;
		case 13: color.rgb = lerp( color.rgb, detail.rgb, detail.a ); break;
	}
}

#include "source1/lighting.hlsl"

float3 SourceRadiosityWeights( float3 normal )
{
	float3 weights = saturate( float3(
		dot( normal, float3( 0.81649661064147949, 0, 0.57735025882720947 ) ),
		dot( normal, float3( -0.40824821591377258, 0.70710676908493042, 0.57735025882720947 ) ),
		dot( normal, float3( -0.40824821591377258, -0.70710676908493042, 0.57735025882720947 ) ) ) );
	return S_SOURCE_WORLD_TWO_TEXTURE ? weights : weights * weights;
}

float3 SourceBakedLighting( float2 uv, float3 normal, bool ssbump, float3 detail )
{
	float3 irradiance;
	if ( g_flSourceHasBumpLightmap > 0.5 && g_flSourceHasNormal > 0.5 && g_flSourceDiffuseBumpLighting > 0.5 )
	{
		float3 weights = ssbump ? normal : SourceRadiosityWeights( normal );
		if ( !ssbump && g_flSourceHasDetail > 0.5 && g_nSourceDetailMode == 10 )
			weights *= 2 * detail;
		irradiance = weights.x * g_tSourceLightmap1.Sample( g_sSourceClamp, uv ).rgb
			+ weights.y * g_tSourceLightmap2.Sample( g_sSourceClamp, uv ).rgb
			+ weights.z * g_tSourceLightmap3.Sample( g_sSourceClamp, uv ).rgb;
		if ( !ssbump )
			irradiance /= max( dot( weights, float3( 1, 1, 1 ) ), 1e-20 );
	}
	else
		irradiance = g_tSourceLightmap.Sample( g_sSourceClamp, uv ).rgb;
	#if S_SOURCE_LIGHTMAP_HDR
		irradiance *= S_SOURCE_WORLD_TWO_TEXTURE ? 32 : 16;
	#else
		irradiance *= S_SOURCE_WORLD_TWO_TEXTURE ? 2 : 4.594793796539307;
	#endif
	return irradiance * max( 0, g_flSourceLightmapScale );
}

float4 SourceSurfaceSample( Texture2D image, SamplerState sampling, float2 uv, PixelInput i )
{
	if ( g_flSourceSeamlessScale != 0 )
	{
		float3 coordinate = (i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz) * g_flSourceSeamlessScale;
		return image.Sample( sampling, coordinate.zy ) * i.vSourceSeamlessWeights.x
			+ image.Sample( sampling, coordinate.xz ) * i.vSourceSeamlessWeights.y
			+ image.Sample( sampling, coordinate.xy ) * i.vSourceSeamlessWeights.z;
	}
	return image.Sample( sampling, uv );
}

#if S_SOURCE_TEXTURE_ANIMATION
float4 SourceSurfaceSample( Texture2DArray image, SamplerState sampling, float2 uv, PixelInput i )
{
	if ( g_flSourceSeamlessScale != 0 )
	{
		float3 coordinate = (i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz) * g_flSourceSeamlessScale;
		return image.Sample( sampling, SourceFrameUv( coordinate.zy, i.flSourceFrame ) ) * i.vSourceSeamlessWeights.x
			+ image.Sample( sampling, SourceFrameUv( coordinate.xz, i.flSourceFrame ) ) * i.vSourceSeamlessWeights.y
			+ image.Sample( sampling, SourceFrameUv( coordinate.xy, i.flSourceFrame ) ) * i.vSourceSeamlessWeights.z;
	}
	return image.Sample( sampling, SourceFrameUv( uv, i.flSourceFrame ) );
}
#endif

float4 MainPs( PixelInput i ) : SV_Target0
{
	float2 uv = i.vTextureCoords.xy;
	float2 baseUv = SourceUv( uv, g_vSourceBaseTransformU, g_vSourceBaseTransformV, g_vSourceBaseScroll, g_vSourceBaseProxyU, g_vSourceBaseProxyV );
	float2 normalUv = SourceUv( uv, g_vSourceNormalTransformU, g_vSourceNormalTransformV, g_vSourceNormalScroll, g_vSourceNormalProxyU, g_vSourceNormalProxyV );
	#if S_SOURCE_FRAMEBUFFER
		float4 baseColor = float4( g_tSourceScene.Sample( g_sSourceClamp, saturate( baseUv ) * g_vFrameBufferCopyInvSizeAndUvScale.zw ).rgb, 1 );
	#else
		float4 baseColor = SourceSurfaceSample( g_tColor, g_sColor, baseUv, i );
	#endif
	#if S_SOURCE_MONITOR
	{
		float2 secondUv = SourceUv( uv, g_vSourceTexture2TransformU, g_vSourceTexture2TransformV, g_vSourceTexture2Scroll, g_vSourceTexture2ProxyU, g_vSourceTexture2ProxyV );
		baseColor *= g_tTexture2.Sample( g_sTexture2, secondUv );
		baseColor.rgb = lerp( baseColor.rgb, baseColor.rgb * baseColor.rgb, g_vSourceMonitorContrast );
		baseColor.rgb = lerp( dot( baseColor.rgb, float3( 1.0 / 3, 1.0 / 3, 1.0 / 3 ) ).xxx, baseColor.rgb, g_vSourceMonitorSaturation ) * g_vSourceMonitorTint;
	}
	#endif
	baseColor.a = lerp( 1, baseColor.a, saturate( g_vSourceTextureAlphaPresence.x ) );
	bool blendTint = SourceUsesBlendTint();
	if ( !blendTint && !S_SOURCE_WORLD_TWO_TEXTURE && !S_SOURCE_TRANSITION && g_flSourceSeamlessScale == 0 )
		baseColor.rgb *= SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy );
	float4 normalTexel = float4( 0.5, 0.5, 1, 1 );
	if ( g_flSourceHasNormal > 0.5 )
		normalTexel = SourceSurfaceSample( g_tNormal, g_sNormal, normalUv, i );
	normalTexel.a = lerp( 1, normalTexel.a, saturate( g_vSourceTextureAlphaPresence.z ) );
	float3 normal = g_flSourceHasNormal > 0.5
		? SourceNormal( normalTexel, g_flSourceSsBump, g_flSourceNormalScale, g_nSourceNormalEncoding ) : float3( 0, 0, 1 );
	bool lightmapSsBump = g_flSourceSsBump > 0.5;
	float3 lightmapNormal = lightmapSsBump ? normalTexel.rgb : normalTexel.rgb * 2 - 1;
	if ( !lightmapSsBump && g_nSourceNormalEncoding == 1 )
		lightmapNormal.z = sqrt( saturate( 1 - dot( lightmapNormal.xy, lightmapNormal.xy ) ) );

	#if S_SOURCE_TRANSITION
	{
		float blend = saturate( i.flSourceBlend * g_flSourceBlendFactor );
		if ( g_flSourceHasBlendModulate > 0.5 )
		{
			float2 blendUv = SourceUv( uv, g_vSourceBlendTransformU, g_vSourceBlendTransformV, g_vSourceBlendScroll, g_vSourceBlendProxyU, g_vSourceBlendProxyV );
			float2 modulation = g_tBlendModulate.Sample( g_sBlendModulate, blendUv ).rg;
			float minBlend = saturate( modulation.g - modulation.r );
			float maxBlend = saturate( modulation.g + modulation.r );
			blend = minBlend < maxBlend ? smoothstep( minBlend, maxBlend, blend ) : step( minBlend, blend );
		}
		float2 base2Uv = SourceUv( uv, g_vSourceBase2TransformU, g_vSourceBase2TransformV, g_vSourceBase2Scroll, g_vSourceBase2ProxyU, g_vSourceBase2ProxyV );
		float4 color2 = SourceSurfaceSample( g_tColor2, g_sColor2, base2Uv, i );
		color2.a = lerp( 1, color2.a, saturate( g_vSourceTextureAlphaPresence.y ) );
		baseColor = lerp( baseColor, color2, blend );
		float4 normal2Texel = float4( 0.5, 0.5, 1, 1 );
		float3 normal2 = float3( 0, 0, 1 );
		if ( g_flSourceHasNormal2 > 0.5 )
		{
			float2 normal2Uv = SourceUv( uv, g_vSourceNormal2TransformU, g_vSourceNormal2TransformV, g_vSourceNormal2Scroll, g_vSourceNormal2ProxyU, g_vSourceNormal2ProxyV );
			normal2Texel = g_tNormal2.Sample( g_sNormal2, normal2Uv );
			normal2Texel.a = lerp( 1, normal2Texel.a, saturate( g_vSourceTextureAlphaPresence.w ) );
			normal2 = SourceNormal( normal2Texel, g_flSourceSsBump2, g_flSourceNormal2Scale, g_nSourceNormal2Encoding );
		}
		normal = SourceNormalize( lerp( normal, normal2, blend ) );
		float3 lightmapNormal2 = lightmapSsBump ? normal2Texel.rgb : normal2Texel.rgb * 2 - 1;
		if ( !lightmapSsBump && g_nSourceNormal2Encoding == 1 )
			lightmapNormal2.z = sqrt( saturate( 1 - dot( lightmapNormal2.xy, lightmapNormal2.xy ) ) );
		lightmapNormal = lerp( lightmapNormal, lightmapNormal2, blend );
		normalTexel.a = lerp( normalTexel.a, normal2Texel.a, blend );
	}
	#endif

	float4 color = baseColor;
	float3 detailEmission = 0;
	float3 lightmapDetail = 1;
	if ( S_SOURCE_WORLD_TWO_TEXTURE || g_flSourceHasDetail > 0.5 )
	{
		float2 detailUv = SourceUv( uv, g_vSourceDetailTransformU, g_vSourceDetailTransformV, g_vSourceDetailScroll, g_vSourceDetailProxyU, g_vSourceDetailProxyV );
		float4 detail = 1;
		if ( g_flSourceHasDetail > 0.5 )
			detail = g_tDetail.Sample( g_sDetail, detailUv );
		detail.a = lerp( 1, detail.a, saturate( g_flSourceDetailAlphaPresent ) );
		if ( g_flSourceDetailSrgb > 0.5 )
			detail.rgb = SrgbGammaToLinear( detail.rgb );
		detail.rgb *= g_vSourceDetailTint;
		lightmapDetail = detail.rgb;
		SourceDetail( color, detail, normal, detailEmission );
	}
	#if S_SOURCE_WORLD_TWO_TEXTURE || S_SOURCE_TRANSITION
		color.rgb *= SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy );
	#endif
	if ( blendTint )
	{
		baseColor = color;
		color.rgb = SourceBlendTintColor( color.rgb, color.a, SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy ) );
	}
	float useBaseAlpha = SourceOwnsBaseAlpha() ? 0 : saturate( g_flSourceUseBaseAlpha );
	color.a = lerp( 1, color.a, useBaseAlpha );
	if ( SourceUsesSkin() && g_nSourcePhongMask == 1 )
		normal = float3( 0, 0, 1 );
	float3 tint = i.vVertexColor.rgb * (g_flSourceSeamlessScale != 0 ? float3( 1, 1, 1 )
		: lerp( float3( 1, 1, 1 ), i.vSourceColor.rgb, saturate( g_flSourceVertexColor ) ));
	color.rgb *= tint;
	detailEmission *= tint;
	float opacity = saturate( color.a * i.flSourceAlpha * i.vVertexColor.a
		* lerp( 1, i.vSourceColor.a, saturate( g_flSourceVertexAlpha ) ) );
	#if S_ALPHA_TEST
		#if !S_TRANSLUCENT
			clip( opacity - 0.000001 );
			#if S_SOURCE_TEXTURE_ANIMATION
				float2 alphaTextureSize = float2( TextureDimensions2DArrayS( g_tColor, 0 ) );
			#else
				float2 alphaTextureSize = float2( TextureDimensions2DS( g_tColor, 0 ) );
			#endif
			if ( g_nMSAASampleCount > 1 )
			{
				float2 texelUv = baseUv * alphaTextureSize;
				float2 dx = ddx( texelUv );
				float2 dy = ddy( texelUv );
				float minification = saturate( 0.5 * log2( max( max( dot( dx, dx ), dot( dy, dy ) ), 1 ) ) );
				float edgeCoverage = saturate( 0.5 + (opacity - g_flSourceAlphaTestReference) / max( fwidth( opacity ), 0.000001 ) );
				opacity = lerp( edgeCoverage, opacity, minification );
				clip( opacity - 0.000001 );
			}
			else
			{
				clip( opacity - g_flSourceAlphaTestReference );
				opacity = 1;
			}
		#else
			clip( opacity - g_flSourceAlphaTestReference );
		#endif
	#endif
	#if !S_TRANSLUCENT && !S_ALPHA_TEST
		opacity = 1;
	#endif
	#if S_MODE_DEPTH && S_TRANSLUCENT
		#if S_ADDITIVE_BLEND
			clip( -1 );
		#elif !S_ALPHA_TEST
			clip( opacity - 0.5 );
		#endif
	#endif

	float3 geometricNormalWs = normalize( i.vNormalWs );
	float3 normalWs = Vec3TsToWsNormalized( normal, geometricNormalWs, normalize( i.vTangentUWs ), normalize( i.vTangentVWs ) );
	#if S_RENDER_BACKFACES
		normalWs *= i.face ? 1 : -1;
		geometricNormalWs *= i.face ? 1 : -1;
	#endif
	{
		Material material = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		material.Albedo = color.rgb;
		material.Normal = normalWs;
		material.GeometricNormal = geometricNormalWs;
		material.TangentNormal = normal;
		material.WorldTangentU = i.vTangentUWs;
		material.WorldTangentV = i.vTangentVWs;
		material.LightmapUV = i.vLightmapUV;
		material.TextureCoords = baseUv;
		material.Opacity = opacity;

		float3 view = CalculatePositionToCameraDirWs( material.WorldPosition );
		float facing = saturate( dot( normalWs, view ) );
		float4 exponentTexel = float4( 1, 1, 1, 1 );
		if ( g_flSourceHasPhongExponent > 0.5 )
			exponentTexel = g_tPhongExponent.Sample( g_sPhongExponent, baseUv );
		float exponent = g_flSourceHasPhongExponent > 0.5 ? 1 + 149 * exponentTexel.r : 5;
		if ( g_flSourcePhongExponent > 0 )
			exponent = g_flSourcePhongExponent;
		if ( g_flSourcePhongExponentFactor != 0 )
			exponent = 1 + g_flSourcePhongExponentFactor * exponentTexel.r;
		material.Roughness = g_flSourceEnvMap > 0 ? saturate( g_flSourceEnvRoughness ) : 1;
		float phongMask = saturate( SourceMask( g_nSourcePhongMask, baseColor, normalTexel.a ) );
		phongMask = lerp( phongMask, 1 - phongMask, saturate( g_flSourcePhongMaskInvert ) );
		if ( g_flSourcePhong > 0.5 )
			material.Roughness = lerp( 1, pow( 2 / (max( exponent, 0 ) + 2), 0.25 ), phongMask );
		#if S_MODE_DEPTH
			if ( DepthNormals::WantsDepthNormals() )
				return DepthNormals::Output( normalWs, material.Roughness, opacity );
			return float4( 0, 0, 0, opacity );
		#else
		material.Emission = detailEmission;
		#if S_SOURCE_VORTWARP
		if ( g_flSourceWarpEmission > 0.5 )
		{
			float2 flow = g_tSourceWarpFlow.Sample( g_sWarpFlow, baseUv ).xy + float2( 0.11, 0.124 ) * g_flTime;
			material.Emission += material.Albedo * SourceProxyVector( g_vSourceSelfIllumTint, g_vSourceSelfIllumTintProxy ) * g_tSourceWarpEmission.Sample( g_sWarpEmission, flow ).rgb;
		}
		#endif
		if ( g_flSourceSelfIllum > 0 )
		{
			float3 mask = baseColor.aaa;
			float brightness = 1;
			if ( g_flSourceHasSelfIllumMask > 0.5 )
				mask = g_tSelfIllumMask.Sample( g_sSelfIllumMask, baseUv ).rgb;
			if ( g_flSourceSelfIllumFresnel > 0.5 )
			{
				brightness = g_vSourceSelfIllumFresnel.y;
				float bias = brightness != 0 ? g_vSourceSelfIllumFresnel.x / brightness : 0;
				float vertexFacing = saturate( dot( normalize( i.vNormalWs ), view ) );
				mask = baseColor.aaa * saturate( pow( vertexFacing, g_vSourceSelfIllumFresnel.z ) * (1 - bias) + bias );
			}
			mask = saturate( mask * g_flSourceSelfIllum );
			material.Emission += material.Albedo * SourceProxyVector( g_vSourceSelfIllumTint, g_vSourceSelfIllumTintProxy ) * mask * brightness;
			material.Albedo *= 1 - mask;
		}

		LightingTerms_t lighting = InitLightingTerms();
		if ( g_flSourceReceiveDecals > 0.5 )
			Decals::Apply( material.WorldPosition, material );
		if ( any( material.Normal != normalWs ) )
		{
			material.TangentNormal = SourceNormalize( float3(
				dot( material.Normal, normalize( i.vTangentUWs ) ),
				dot( material.Normal, normalize( i.vTangentVWs ) ),
				dot( material.Normal, normalize( i.vNormalWs ) ) ) );
			lightmapNormal = material.TangentNormal * float3( 1, g_flSourceNormalYSign, 1 );
			lightmapSsBump = false;
		}
		normalWs = material.Normal;
		facing = saturate( dot( normalWs, view ) );
		CombinerInput combiner = ShadingModelStandard::MaterialToCombinerInput( material );
		if ( g_flSourcePhong > 0.5 )
		{
			float3 tint = 1;
			if ( any( g_vSourcePhongTint != 0 ) )
				tint = g_vSourcePhongTint;
			else if ( g_flSourcePhongAlbedoTint > 0.5 )
				tint = lerp( float3( 1, 1, 1 ), baseColor.rgb * g_flSourcePhongAlbedoBoost, exponentTexel.g );
			combiner.vSpecularColor = saturate( combiner.vSpecularColor * phongMask * max( g_flSourcePhongBoost, 0 ) * tint );
		}
		#if !S_SOURCE_UNLIT
		{
			#if S_SOURCE_LIGHTMAP
				SourceBakedDynamicLighting( lighting, combiner );
				#if !S_TRANSLUCENT
					lighting.flDynamicAmbientOcclusion = ScreenSpaceAmbientOcclusion::Sample( combiner.vPositionSs );
				#endif
				lighting.vIndirectDiffuse = SourceBakedLighting( i.vTextureCoords.zw, lightmapNormal, lightmapSsBump, lightmapDetail );
				lighting.flBakedAmbientOcclusion = 1;
			#else
				if ( g_nSourcePropLighting != 0 )
				{
					#if !S_SOURCE_PROP_LIGHTING
						CalculateIndirectLighting( lighting, combiner );
					#endif
					SourceBakedDynamicLighting( lighting, combiner );
					#if S_SOURCE_PROP_LIGHTING && !S_TRANSLUCENT
						lighting.flDynamicAmbientOcclusion = ScreenSpaceAmbientOcclusion::Sample( combiner.vPositionSs );
					#endif
					lighting.vIndirectDiffuse = g_nSourcePropLighting == 2
						? pow( max( 2 * g_tSourcePropLightmap.Sample( g_sSourcePropLightmap, baseUv ).rgb, 0 ), 2.2 )
						: g_nSourcePropLighting == 3
							? SourcePropLighting( material.WorldPosition, normalWs )
							: i.vSourcePropLighting;
					lighting.flBakedAmbientOcclusion = 1;
				}
				else
				{
					ComputeDirectLighting( lighting, combiner );
					CalculateIndirectLighting( lighting, combiner );
				}
			#endif
			if ( g_nSourcePropLighting == 2 )
				lighting.vDiffuse = 0;
			if ( g_flSourceBakedSunShadows && g_DirectionalLightCascadeCount > 0
				&& ( S_SOURCE_LIGHTMAP != 0 || g_nSourcePropLighting != 0 ) )
			{
				float visibility = DirectionalLightShadow::GetVisibility( material.WorldPosition,
					ComputeShadowReceiverNormal( material.WorldPosition ), combiner.vPositionSs );
				lighting.vIndirectDiffuse *= lerp( SrgbGammaToLinear( saturate( g_vSourceBakedShadowTint ) ), 1, visibility );
			}
			lighting.vDiffuse *= max( 0, g_flSourceDirectLightScale );
			lighting.vSpecular *= max( 0, g_flSourceDirectLightScale );
		}
		#else
			lighting.vDiffuse = 1;
		#endif

		if ( g_flSourceEnvMap > 0 )
		{
			float3 mask = float3( 1, 1, 1 );
			if ( SourceUsesSkin() )
			{
				mask = g_nSourceEnvMask == 2
					? SourceMask( g_nSourcePhongMask, baseColor, normalTexel.a ) : baseColor.a;
				mask = lerp( mask, 1 - mask, saturate( g_flSourcePhongMaskInvert ) );
			}
			else if ( g_nSourceEnvMask == 2 )
				mask = normalTexel.aaa;
			else if ( g_nSourceEnvMask == 1 )
				mask = 1 - baseColor.a;
			else if ( g_nSourceEnvMask == 3 )
				mask = SourceMask( 3, baseColor, normalTexel.a );
			else if ( g_flSourceHasEnvMask > 0.5 )
			{
				float2 maskUv = SourceUv( uv, g_vSourceEnvMaskTransformU, g_vSourceEnvMaskTransformV, g_vSourceEnvMaskScroll, g_vSourceEnvMaskProxyU, g_vSourceEnvMaskProxyV );
				mask = g_tEnvMask.Sample( g_sEnvMask, maskUv ).rgb;
			}
			mask = lerp( saturate( mask ), 1 - saturate( mask ), saturate( g_flSourceEnvMaskInvert ) );
			float3 reflection;
			if ( g_flSourceHasEnvCube > 0.5 )
				reflection = g_tEnvCube.SampleLevel( g_sSourceCube, reflect( -view, normalWs ), max( 0, g_flSourceEnvMip ) ).rgb;
			else
			{
				reflection = EnvMap::From( material.WorldPosition, i.vPositionSs, normalWs, saturate( g_flSourceEnvRoughness ).xx );
			}
			reflection *= mask * SourceProxyVector( g_vSourceEnvTint, g_vSourceEnvTintProxy ) * g_flSourceEnvMap;
			if ( SourceUsesSkin() )
				reflection *= lerp( 1, SourceFresnel( facing ), g_flSourceEnvFresnel );
			else
			{
				reflection = lerp( reflection, reflection * reflection, saturate( g_vSourceEnvContrast ) );
				reflection = lerp( dot( reflection, float3( 0.299, 0.587, 0.114 ) ).xxx, reflection, g_vSourceEnvSaturation );
				if ( g_flSourceLightmappedGeneric > 0.5 )
					reflection *= lerp( g_flSourceFresnelReflection, 1, pow( 1 - facing, 5 ) );
			}
			if ( g_flSourceHasEnvSphere > 0.5 )
			{
				float2 envUv = normalize( reflect( -view, normalWs ) ).xy;
				lighting.vIndirectSpecular = 0.5 * g_tEnvSphere.Sample( g_sEnvSphere, envUv ).rgb
					* SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy ) * (lighting.vDiffuse + lighting.vIndirectDiffuse);
			}
			else
				lighting.vIndirectSpecular = reflection;
		}

		#if !S_SOURCE_UNLIT
			float3 diffuseAO = CalculateDiffuseAmbientOcclusion( combiner, lighting );
			lighting.vIndirectDiffuse *= diffuseAO;
			lighting.vDiffuse *= lerp( float3( 1, 1, 1 ), diffuseAO, combiner.flAmbientOcclusionDirectDiffuse );
			float3 specularAO = CalculateSpecularAmbientOcclusion( combiner, lighting );
			lighting.vIndirectSpecular *= specularAO;
			lighting.vSpecular *= lerp( float3( 1, 1, 1 ), specularAO, combiner.flAmbientOcclusionDirectSpecular );
		#endif
		float3 result = lighting.vDiffuse * combiner.vDiffuseColor
			#if S_SOURCE_LIGHTMAP
				+ lighting.vIndirectDiffuse * material.Albedo
			#else
				+ lighting.vIndirectDiffuse * (g_nSourcePropLighting != 0 ? material.Albedo : combiner.vDiffuseColor)
			#endif
			+ lighting.vSpecular + lighting.vIndirectSpecular + combiner.vEmissive;
		float4 output = float4( max( result, float3( 0, 0, 0 ) ), opacity );
		if ( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( normalWs, material.Roughness, opacity );
		if ( g_bWireframeMode )
			return g_vWireframeColor;
		if ( ToolsVis::WantsToolsVis() )
			return ShadingModelStandard::DoToolsVis( output, material, lighting );
		if ( g_bFogEnabled )
		{
			#if S_ADDITIVE_BLEND
				output.rgb *= Fog::Transmittance( material.WorldPosition, i.vPositionSs.xy );
			#else
				output.rgb = Fog::Apply( material.WorldPosition, i.vPositionSs.xy, output.rgb );
			#endif
		}
		return output;
		#endif
	}
}
