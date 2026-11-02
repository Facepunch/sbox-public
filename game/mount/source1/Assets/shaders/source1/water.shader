HEADER
{
	Description = "Source 1 refraction and water";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_SOURCE_WATER, 0..1, "Source 1" );
	Feature( F_SOURCE_ALPHA_SINE, 0..1, "Source 1" );
	Feature( F_SOURCE_PROXIES, 0..1, "Source 1" );
	Feature( F_SOURCE_TEXTURE_ANIMATION, 0..1, "Source 1" );
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"
	#include "source1/alpha.hlsl"
	#include "source1/frames.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.flSourceAlpha = SourceAlpha();
		i.flSourceFrame = SourceFrame();
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_SOURCE_WATER, F_SOURCE_WATER, Sys( ALL ) );
	DynamicCombo( D_SKYBOX, 0..1, Sys( PC ) );
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#include "common/pixel.hlsl"
	#include "source1/uv.hlsl"
	#include "source1/frames.hlsl"
	#include "common/classes/Depth.hlsl"

	BoolAttribute( bWantsFBCopyTexture, true );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );

	SamplerState g_sNormal < Attribute( "g_tNormalSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sNormal2 < Attribute( "g_tNormal2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceTint < Attribute( "g_tSourceTintSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceCube < Attribute( "g_tSourceEnvCubeSampler" ); Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	SamplerState g_sSourceClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	Texture2D g_tSourceScene < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	CreateInputTexture2D( SourceNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1.0, 1.0 ) );
	CreateInputTexture2D( SourceNormal2, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1.0, 1.0 ) );
	CreateInputTexture2D( SourceTint, Srgb, 8, "None", "_color", "Source 1", Default4( 1.0, 1.0, 1.0, 1.0 ) );
	CreateInputTextureCube( SourceEnvCube, Srgb, 8, "None", "_cube", "Source 1", Default4( 0, 0, 0, 1 ) );
	#if S_SOURCE_TEXTURE_ANIMATION
		Texture2DArray g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#else
		Texture2D g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#endif
	Texture2D g_tNormal2 < Channel( RGBA, Box( SourceNormal2 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tSourceTint < Channel( RGBA, Box( SourceTint ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	CreateTextureCube( g_tSourceEnvCube ) < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;

	float3 g_vSourceNormalTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceNormalTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceNormalScroll < Default3( 0, 0, 1 ); >;
	float4 g_vSourceNormalLayerScroll < Default4( 0, 0, 0, 0 ); >;
	float3 g_vSourceRefractTint < Default3( 1, 1, 1 ); >;
	float3 g_vSourceReflectTint < Default3( 1, 1, 1 ); >;
	float3 g_vSourceFogColor < Default3( 0, 0, 0 ); >;
	float g_flSourceRefractAmount < Default( 0.02 ); >;
	float g_flSourceReflectAmount < Default( 1 ); >;
	float g_flSourceFogStart < Default( 0 ); >;
	float g_flSourceFogEnd < Default( 1000 ); >;
	float g_flSourceBlur < Default( 0 ); >;
	float g_flSourceHasNormal2 < Default( 0 ); >;
	float g_flSourceHasTint < Default( 0 ); >;
	float g_flSourceHasEnvCube < Default( 0 ); >;
	float g_flSourceSceneReflection < Default( 0 ); >;
	float g_flSourceEnvAlphaMask < Default( 0 ); >;
	float g_flSourceFresnel < Default( 1 ); >;
	float g_flSourceAboveWater < Default( 1 ); >;
	float g_flSourceCheapWater < Default( 0 ); >;
	float g_flSourceWaterBlend < Default( 1 ); >;
	float g_flSourceBlurRefract < Default( 0 ); >;
	float g_flSourceWaterMultiTexture < Default( 0 ); >;

	float3 SourceSampleScene( float2 position )
	{
		float2 uv = saturate( CalculateViewportUv( position ) ) * g_vFrameBufferCopyInvSizeAndUvScale.zw;
		return g_tSourceScene.SampleLevel( g_sSourceClamp, uv, 0 ).rgb;
	}

	float SourceSceneDepth( float2 position )
	{
		position = clamp( position, 0, 1 / g_vInvViewportSize - 1 );
		#if D_SKYBOX
			return Depth::Linearize( 0, position );
		#else
			return Depth::Linearize( Depth::GetNormalized( position ), position );
		#endif
	}

	float SourceWaterDepth( float2 position, float waterHeight )
	{
		#if D_SKYBOX
			return 1;
		#else
			if ( g_flSourceAboveWater < 0.5 ) return 1;
			position = clamp( position, 0, 1 / g_vInvViewportSize - 1 );
			if ( Depth::GetNormalized( position ) <= 0 ) return 1;
			float3 receiver = Depth::GetWorldPosition( position );
			float height = g_vCameraPositionWs.z - receiver.z;
			if ( abs( height ) < 0.0001 ) return 0;
			float fraction = saturate( (waterHeight - receiver.z) / height );
			return saturate( fraction * SourceSceneDepth( position ) / max( g_flSourceFogEnd - g_flSourceFogStart, 0.001 ) );
		#endif
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = SourceUv( i.vTextureCoords.xy, g_vSourceNormalTransformU, g_vSourceNormalTransformV, g_vSourceNormalScroll, g_vSourceNormalProxyU, g_vSourceNormalProxyV );
		#if S_SOURCE_WATER
			if ( g_flSourceCheapWater > 0.5 )
				uv = i.vTextureCoords.xy;
		#endif
		float4 normalSample = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv, i.flSourceFrame ) );
		#if S_SOURCE_WATER
			if ( g_flSourceWaterMultiTexture > 0.5 )
			{
				float2 rawUv = i.vTextureCoords.xy;
				float2 rotatedUv = float2( rawUv.x + rawUv.y, rawUv.y - rawUv.x ) * 0.1;
				float2 swappedUv = rawUv.yx * 0.45;
				normalSample = 0.33 * (normalSample
					+ g_tNormal.Sample( g_sNormal, SourceFrameUv( rotatedUv + g_vSourceNormalLayerScroll.xy * g_flTime, i.flSourceFrame ) )
					+ g_tNormal.Sample( g_sNormal, SourceFrameUv( swappedUv + g_vSourceNormalLayerScroll.zw * g_flTime, i.flSourceFrame ) ));
			}
			float3 normalTs = normalSample.xyz * 2 - 1;
		#else
			normalSample = g_tNormal.Sample( g_sNormal, SourceFrameUv( uv + g_vSourceNormalLayerScroll.xy * g_flTime, i.flSourceFrame ) );
			float3 normalTs = normalize( normalSample.xyz * 2 - 1 );
		#endif
		if ( g_flSourceHasNormal2 > 0.5 )
		{
			float3 second = g_tNormal2.Sample( g_sNormal2, uv + g_vSourceNormalLayerScroll.zw * g_flTime ).xyz * 2 - 1;
			normalTs = normalize( float3( normalTs.xy + second.xy, normalTs.z * second.z ) );
		}

		float alpha = saturate( i.flSourceAlpha );
		float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 perturbation = Vector3WsToVs( normalTs.x * i.vTangentUWs + normalTs.y * i.vTangentVWs );
		float2 offset = float2( perturbation.x, -perturbation.y ) * g_flSourceRefractAmount * alpha;
		#if S_SOURCE_WATER
			float unwarpedWaterDepth = SourceWaterDepth( i.vPositionSs.xy, positionWs.z );
			offset = normalTs.xy * normalSample.a * g_flSourceRefractAmount * alpha;
			if ( g_flSourceBlurRefract < 0.5 )
				offset *= unwarpedWaterDepth;
			if ( g_flSourceCheapWater > 0.5 )
				offset = 0;
			if ( g_flSourceCheapWater < 0.5 )
				alpha *= saturate( g_flSourceWaterBlend );
		#endif
		float2 distortedPosition = i.vPositionSs.xy + offset / g_vInvViewportSize;
		float surfaceDepth = Depth::Linearize( Depth::Normalize( i.vPositionSs.z ), i.vPositionSs.xy );
		float sceneDepth = SourceSceneDepth( distortedPosition );
		if ( sceneDepth < surfaceDepth )
		{
			distortedPosition = i.vPositionSs.xy;
			sceneDepth = SourceSceneDepth( distortedPosition );
		}

		float3 scene = SourceSampleScene( distortedPosition );
		#if S_SOURCE_WATER
			float waterDepth = SourceWaterDepth( distortedPosition, positionWs.z );
			if ( g_flSourceBlurRefract > 0.5 && g_flSourceCheapWater < 0.5 )
			{
				scene = 0;
				waterDepth = 0;
				for ( int x = -2; x <= 2; x++ )
					for ( int y = -2; y <= 2; y++ )
					{
						float2 samplePosition = distortedPosition + float2( x, y ) * 0.005 / g_vInvViewportSize;
						scene += SourceSampleScene( samplePosition );
						waterDepth += SourceWaterDepth( samplePosition, positionWs.z );
					}
				scene /= 25;
				waterDepth /= 25;
			}
		#endif
		if ( g_flSourceBlur > 0 )
		{
			float blur = min( g_flSourceBlur, 8 );
			scene = (scene * 2
				+ SourceSampleScene( distortedPosition + float2( blur, 0 ) )
				+ SourceSampleScene( distortedPosition - float2( blur, 0 ) )
				+ SourceSampleScene( distortedPosition + float2( 0, blur ) )
				+ SourceSampleScene( distortedPosition - float2( 0, blur ) )) / 6;
		}
		float3 tint = g_vSourceRefractTint;
		if ( g_flSourceHasTint > 0.5 )
			tint *= g_tSourceTint.Sample( g_sSourceTint, uv ).rgb;
		float3 color = scene * tint;
		if ( g_bFogEnabled )
			color += Fog::Apply( positionWs, i.vPositionSs.xy, float3( 0, 0, 0 ) ) * (1 - tint);

		#if S_SOURCE_WATER
			float fog = g_flSourceAboveWater > 0.5 ? saturate( waterDepth - 0.05 )
				: saturate( (surfaceDepth - g_flSourceFogStart) / max( g_flSourceFogEnd - g_flSourceFogStart, 0.001 ) );
			if ( g_flSourceCheapWater > 0.5 ) fog = 1;
			float3 waterColor = g_vSourceFogColor;
			if ( g_bFogEnabled )
				waterColor = Fog::Apply( positionWs, i.vPositionSs.xy, waterColor );
			color = lerp( color, waterColor, fog );
		#endif

		if ( g_flSourceHasEnvCube > 0.5 || g_flSourceSceneReflection > 0.5 )
		{
			float3 normalWs = Vec3TsToWsNormalized( normalTs, normalize( i.vNormalWs ), normalize( i.vTangentUWs ), normalize( i.vTangentVWs ) );
			float3 view = normalize( g_vCameraPositionWs.xyz - positionWs );
			float fresnel = lerp( 1, 0.02 + 0.98 * pow( 1 - saturate( dot( view, normalWs ) ), 5 ), g_flSourceFresnel );
			#if S_SOURCE_WATER
				float3 viewTs = float3( dot( view, i.vTangentUWs ), dot( view, i.vTangentVWs ), dot( view, i.vNormalWs ) );
				fresnel = pow( 1 - saturate( dot( viewTs, normalTs ) ), 5 );
				if ( g_flSourceCheapWater > 0.5 )
					fresnel = lerp( saturate( g_flSourceWaterBlend ), fresnel, g_flSourceFresnel );
				else
					fresnel *= saturate( (waterDepth - 0.05) * 20 );
			#endif
			float3 reflection;
			if ( g_flSourceHasEnvCube > 0.5 )
			{
				reflection = g_tSourceEnvCube.SampleLevel( g_sSourceCube, reflect( -view, normalWs ), 0 ).rgb;
			}
			else
			{
				reflection = EnvMap::From( positionWs, i.vPositionSs, normalWs, float2( 0.02, 0.02 ) );
			}
			reflection *= g_vSourceReflectTint;
			if ( g_bFogEnabled )
				reflection = Fog::Apply( positionWs, i.vPositionSs.xy, reflection );
			float reflectionMask = lerp( 1, normalSample.a, g_flSourceEnvAlphaMask );
			#if S_SOURCE_WATER
				color = lerp( color, reflection, saturate( fresnel ) );
			#else
				color = lerp( color, reflection, saturate( fresnel * g_flSourceReflectAmount * reflectionMask ) );
			#endif
		}

		float2 screenUv = saturate( CalculateViewportUv( i.vPositionSs.xy ) ) * g_vFrameBufferCopyInvSizeAndUvScale.zw;
		float3 background = g_tSourceScene.SampleLevel( g_sSourceClamp, screenUv, 0 ).rgb;
		float4 output = float4( lerp( background, color, alpha ), 1 );
		return output;
	}
}
