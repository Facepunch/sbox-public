#define DEPTH_STATE_ALREADY_SET 1
#define BLEND_MODE_ALREADY_SET 1
#define CULL_MODE_ALREADY_SET 1
#include "common/pixel.hlsl"
#include "source1/frames.hlsl"
#include "source1/uv.hlsl"

BoolAttribute( bWantsFBCopyTexture, true );
BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
RenderState( DepthEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( DepthWriteEnable, F_SOURCE_DEPTH_WRITE != 0 );
RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );

SamplerState g_sNormal < Attribute( "g_tNormalSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sNormal2 < Attribute( "g_tNormal2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sTint < Attribute( "g_tSourceTintSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sColor < Attribute( "g_tColorSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sFlow < Attribute( "g_tSourceFlowSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sCoreColor < Attribute( "g_tSourceCoreColorSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
SamplerState g_sCube < Attribute( "g_tEnvCubeSampler" ); Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
SamplerState g_sClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
Texture2D g_tSourceScene < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

CreateInputTexture2D( SourceNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
CreateInputTexture2D( SourceNormal2, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
CreateInputTexture2D( SourceTint, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceColor, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceFlow, Linear, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTexture2D( SourceCoreColor, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
CreateInputTextureCube( SourceEnvCube, Srgb, 8, "None", "_cube", "Source 1", Default4( 0, 0, 0, 1 ) );
#if S_SOURCE_TEXTURE_ANIMATION
	Texture2DArray g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#else
	Texture2D g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
#endif
Texture2D g_tNormal2 < Channel( RGBA, Box( SourceNormal2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tSourceTint < Channel( RGBA, Box( SourceTint ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
Texture2D g_tSourceFlow < Channel( RGBA, Box( SourceFlow ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
Texture2D g_tSourceCoreColor < Channel( RGBA, Box( SourceCoreColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
CreateTextureCube( g_tEnvCube ) < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;

float3 g_vSourceNormalTransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceNormalTransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceNormalScroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceNormal2TransformU < Default3( 1, 0, 0 ); >;
float3 g_vSourceNormal2TransformV < Default3( 0, 1, 0 ); >;
float3 g_vSourceNormal2Scroll < Default3( 0, 0, 1 ); >;
float3 g_vSourceRefractTint < Default3( 1, 1, 1 ); >;
float3 g_vSourceEnvTint < Default3( 1, 1, 1 ); >;
float3 g_vSourceEnvContrast < Default3( 0, 0, 0 ); >;
float3 g_vSourceEnvSaturation < Default3( 1, 1, 1 ); >;
float g_flSourceRefractAmount < Default( 2 ); >;
float g_flSourceBlur < Default( 0 ); >;
float g_flSourceFadeSilhouette < Default( 0 ); >;
float g_flSourceVertexColor < Default( 0 ); >;
float g_flSourceNormalAlpha < Default( 0 ); >;
int g_nSourceNormalEncoding < Default( 0 ); >;
float g_flSourceHasNormal2 < Default( 0 ); >;
float g_flSourceHasTint < Default( 0 ); >;
float g_flSourceHasBase < Default( 0 ); >;
float g_flSourceHasEnvCube < Default( 0 ); >;
float g_flSourceSceneReflection < Default( 0 ); >;
float g_flSourceHasFlow < Default( 0 ); >;
float g_flSourceHasCoreColor < Default( 0 ); >;
float3 g_vSourceCoreOrigin < Default3( 2688, 12139, 5170 ); >;
float g_flSourceCoreEntityOrigin < Default( 0 ); >;
float g_flSourceCoreDiameter < Default( 430 ); >;
float2 g_vSourceFlowScroll < Default2( 0, 0 ); >;
float g_flSourceCoreOffset < Default( 0 ); >;
float g_flSourceCoreSine < Default( 0 ); >;
float4 g_vSourceCoreSine < Default4( 1, 0, 0, 1 ); >;
float3 g_vSourceSilhouetteColor < Default3( 0.3, 0.3, 0.5 ); >;
float g_flSourceSilhouetteThickness < Default( 0.2 ); >;
float2 g_vSourceGroundRange < Default2( -0.3, -0.1 ); >;
float g_flSourceShockBlur < Default( 0.01 ); >;
float g_flSourceEffectTime < Default( 0 ); >;
float g_flSourceEffectRuntime < Default( 1 ); >;

float3 SourceScene( float2 uv )
{
	return g_tSourceScene.SampleLevel( g_sClamp, saturate( uv ) * g_vFrameBufferCopyInvSizeAndUvScale.zw, 0 ).rgb;
}

float4 MainPs( PixelInput i ) : SV_Target0
{
	float2 uv = SourceUv( i.vTextureCoords.xy, g_vSourceNormalTransformU, g_vSourceNormalTransformV, g_vSourceNormalScroll, g_vSourceNormalProxyU, g_vSourceNormalProxyV );
	float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
	float3 view = normalize( g_vCameraPositionWs.xyz - positionWs );
	#if S_SOURCE_AFTERSHOCK
	{
		float time = lerp( g_flSourceEffectTime, g_flTime, g_flSourceEffectRuntime );
		float2 scroll = float2( 0, -time * 0.1 );
		float2 noise0 = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv * 3 + scroll, i.flSourceFrame ) ).xy;
		float2 noise1 = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv * 16 + scroll, i.flSourceFrame ) ).xy;
		float3 normal0 = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv + noise0 * 0.03, i.flSourceFrame ) ).xyz * 2 - 1;
		float3 normal1 = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv * 8 + noise1 * 0.03, i.flSourceFrame ) ).xyz * 2 - 1;
		float3 normalWs = (normal0.x + 0.5 * normal1.x) * i.vTangentUWs
			+ (normal0.y + 0.5 * normal1.y) * i.vTangentVWs
			+ (normal0.z + 0.5 * normal1.z) * i.vNormalWs;
		float mask = saturate( dot( view, normalize( i.vNormalWs ) ) * (2 - g_flSourceSilhouetteThickness) );
		mask *= smoothstep( g_vSourceGroundRange.x, g_vSourceGroundRange.y, i.vNormalWs.z );
		mask *= mask;
		float2 projected = Position3WsToPs( positionWs + normalWs ).xy - Position3WsToPs( positionWs ).xy;
		projected.y = -projected.y;
		float2 screenUv = CalculateViewportUv( i.vPositionSs.xy ) + projected * mask * g_flSourceRefractAmount;
		float radius = g_flSourceShockBlur * mask;
		float2 offsets[8] = {
			float2( -0.0876, 0.9703 ), float2( 0.4802, 0.5651 ),
			float2( 0.1851, 0.1580 ), float2( -0.2616, -0.0617 ),
			float2( -0.5477, -0.6603 ), float2( -0.5325, 0.0711 ),
			float2( -0.0751, -0.8954 ), float2( 0.6384, 0.4054 )
		};
		float3 color = SourceScene( screenUv );
		[unroll] for ( int tap = 0; tap < 8; tap++ )
			color += SourceScene( screenUv + offsets[tap] * radius );
		color *= lerp( float3( 1, 1, 1 ), g_vSourceRefractTint, mask ) / 9;
		color *= 1 + pow( saturate( mask * (1 - mask) * 4 ), 3 ) * g_vSourceSilhouetteColor;
		return float4( color, mask * saturate( i.flSourceAlpha ) );
	}
	#endif
	float normalizedLength = 1;
	float sphereAlpha = 1;
	#if S_SOURCE_CORE
	if ( g_flSourceHasFlow > 0.5 )
	{
		float3 origin = lerp( g_vSourceCoreOrigin, i.vSourceOriginWs, g_flSourceCoreEntityOrigin );
		float3 radial = positionWs - origin;
		float3 direction = normalize( radial );
		float3 coordinates = direction * 0.5 + 0.5;
		float3 weights = abs( direction );
		uv = weights.z * g_tSourceFlow.Sample( g_sFlow, coordinates.xy ).xy
			+ weights.y * g_tSourceFlow.Sample( g_sFlow, coordinates.xz ).xy
			+ weights.x * g_tSourceFlow.Sample( g_sFlow, coordinates.yz ).xy
			+ g_flTime * g_vSourceFlowScroll;
		float3 camera = g_vCameraPositionWs.xyz - origin;
		float projection = dot( camera, -view );
		float radiusSquared = dot( radial, radial ) * 1.1025;
		float discriminant = projection * projection - dot( camera, camera ) + radiusSquared;
		sphereAlpha = step( 0, discriminant );
		normalizedLength = 2 * sqrt( max( discriminant, 0 ) ) / g_flSourceCoreDiameter;
	}
	#endif
	float4 texel = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv, i.flSourceFrame ) );
	float3 normalTs = texel.xyz * 2 - 1;
	if ( g_nSourceNormalEncoding == 1 )
		normalTs.z = sqrt( saturate( 1 - dot( normalTs.xy, normalTs.xy ) ) );
	float mask = lerp( 1, texel.a, g_flSourceNormalAlpha );
	if ( g_flSourceHasNormal2 > 0.5 )
	{
		float2 secondUv = SourceUv( i.vTextureCoords.xy, g_vSourceNormal2TransformU, g_vSourceNormal2TransformV, g_vSourceNormal2Scroll, g_vSourceNormal2ProxyU, g_vSourceNormal2ProxyV );
		normalTs = normalize( normalTs + g_tNormal2.Sample( g_sNormal2, secondUv ).xyz * 2 - 1 );
	}
	float2 offset = normalTs.xy * mask * g_flSourceRefractAmount * normalizedLength;
	if ( g_flSourceVertexColor > 0.5 ) offset *= i.vSourceColor.a;
	float2 screenUv = CalculateViewportUv( i.vPositionSs.xy );
	float3 background = SourceScene( screenUv );
	float3 warped;
	if ( g_flSourceHasBase > 0.5 )
		warped = g_tColor.Sample( g_sColor, saturate( screenUv + offset ) ).rgb;
	else if ( g_flSourceBlur > 0 )
	{
		float2 stepUv = float2( 1.0 / 512, 1.0 / 512 );
		warped = SourceScene( screenUv + offset - stepUv * 0.5 ) * (4.0 / 9)
			+ SourceScene( screenUv + offset + stepUv * float2( 1, -0.5 ) ) * (2.0 / 9)
			+ SourceScene( screenUv + offset + stepUv * float2( -0.5, 1 ) ) * (2.0 / 9)
			+ SourceScene( screenUv + offset + stepUv ) * (1.0 / 9);
	}
	else
		warped = SourceScene( screenUv + offset );
	float3 tint = g_vSourceRefractTint;
	if ( g_flSourceHasTint > 0.5 ) tint *= 2 * g_tSourceTint.Sample( g_sTint, uv ).rgb;
	if ( g_flSourceVertexColor > 0.5 ) tint *= i.vSourceColor.rgb;
	float blend = g_flSourceFadeSilhouette > 0.5 ? pow( saturate( dot( view, normalize( i.vNormalWs ) ) ), 3 ) : 1;
	float3 result = lerp( background, warped * tint, blend );
	bool environment = g_flSourceHasEnvCube > 0.5 || g_flSourceSceneReflection > 0.5;
	float opacity = environment ? 1 : mask;
	if ( environment )
	{
		float3 normalWs = Vec3TsToWsNormalized( normalTs, normalize( i.vNormalWs ), normalize( i.vTangentUWs ), normalize( i.vTangentVWs ) );
		float3 reflected = g_flSourceHasEnvCube > 0.5
			? g_tEnvCube.SampleLevel( g_sCube, reflect( -view, normalWs ), 0 ).rgb
			: EnvMap::From( positionWs, i.vPositionSs, normalWs, float2( 0.02, 0.02 ) );
		reflected *= mask * g_vSourceEnvTint;
		reflected = lerp( reflected, reflected * reflected, g_vSourceEnvContrast );
		reflected = lerp( dot( reflected, float3( 0.299, 0.587, 0.114 ) ).xxx, reflected, g_vSourceEnvSaturation );
		result += reflected;
	}
	#if S_SOURCE_CORE
	if ( g_flSourceHasFlow > 0.5 && g_flSourceHasCoreColor > 0.5 )
	{
		float phase = g_flSourceCoreOffset;
		if ( g_flSourceCoreSine > 0.5 )
			phase = lerp( g_vSourceCoreSine.z, g_vSourceCoreSine.w,
				0.5 + 0.5 * sin( (g_flTime - g_vSourceCoreSine.y) * 6.28318530718 / g_vSourceCoreSine.x ) );
		float4 coreColor = g_tSourceCoreColor.Sample( g_sCoreColor, offset + float2( normalizedLength, phase ) );
		result = lerp( result, coreColor.rgb, coreColor.a );
		opacity = sphereAlpha;
	}
	#endif
	if ( g_bFogEnabled )
		result = Fog::Apply( positionWs, i.vPositionSs.xy, result );
	return float4( result, opacity * saturate( i.flSourceAlpha ) );
}
