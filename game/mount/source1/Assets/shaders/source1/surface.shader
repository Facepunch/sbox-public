HEADER
{
	Description = "Source 1 source1 model surfaces";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_SOURCE_UNLIT, 0..1, "Source 1" );
	Feature( F_SOURCE_ALPHA_SINE, 0..1, "Source 1" );
	Feature( F_SOURCE_TEXTURE_ANIMATION, 0..1, "Source 1" );
	Feature( F_SOURCE_FRAMEBUFFER, 0..1, "Source 1" );
	Feature( F_SOURCE_VORTWARP, 0..1, "Source 1" );
	Feature( F_ALPHA_TEST, 0..1, "Transparency" );
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires translucency" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#define S_UV2 1
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vSourceColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vSourceColor : COLOR0;
	nointerpolation float flSourceAlpha : TEXCOORD15;
	nointerpolation float flSourceFrame : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"
	#include "source1/alpha.hlsl"
	#include "source1/frames.hlsl"
	StaticCombo( S_SOURCE_VORTWARP, F_SOURCE_VORTWARP, Sys( ALL ) );
	float g_flSourceWarpParam < Default( 0 ); >;
	float3 g_vSourceWarpOrigin < Default3( 0, 0, 0 ); >;
	float g_flSourceWarpEntityOrigin < Default( 0 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		#if S_SOURCE_VORTWARP
		{
			float3 position = i.vPositionWs;
			float3 origin = lerp( g_vSourceWarpOrigin, mul( GetTransformMatrix( v.nInstanceTransformID ), float4( 0, 0, 0, 1 ) ).xyz, g_flSourceWarpEntityOrigin );
			float height = saturate( position.z / 72 );
			float spread = 3 * height * (1 - height) * (1 - height);
			float3 delta = (position - origin) * float3( spread, spread, 1 );
			delta.z += 5 + 5 * sin( position.z );
			float amount = pow( saturate( 1 - g_flSourceWarpParam ), 8 );
			i.vPositionWs = lerp( position, delta + origin, amount );
			i.vPositionPs = Position3WsToPs( i.vPositionWs );
		}
		#endif
		i.vSourceColor = v.vSourceColor;
		i.flSourceAlpha = SourceAlpha();
		i.flSourceFrame = SourceFrame();
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
	StaticCombo( S_SOURCE_UNLIT, F_SOURCE_UNLIT, Sys( ALL ) );
	StaticCombo( S_SOURCE_FRAMEBUFFER, F_SOURCE_FRAMEBUFFER, Sys( ALL ) );

	#define BLEND_MODE_ALREADY_SET
	// sbox_pixel supplies culling; suppress the duplicate declaration in vr_common_ps_code.
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "source1/frames.hlsl"

	// Explicit clipping gives the same cutoff in the forward and shadow/depth passes.
	#if S_TRANSLUCENT
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
	SamplerState g_sSourceCube < Attribute( "g_tEnvCubeSampler" ); Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	SamplerState g_sEnvSphere < Attribute( "g_tEnvSphereSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sWarpFlow < Attribute( "g_tSourceWarpFlowSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sWarpEmission < Attribute( "g_tSourceWarpEmissionSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sTexture2 < Attribute( "g_tTexture2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	BoolAttribute( bWantsFBCopyTexture, S_SOURCE_FRAMEBUFFER != 0 );
	Texture2D g_tSourceScene < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;

	CreateInputTexture2D( SourceColor, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( SourceDetail, Linear, 8, "None", "_detail", "Source 1", Default4( 0.5, 0.5, 0.5, 1 ) );
	CreateInputTexture2D( SourceEnvMask, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceSelfIllumMask, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourcePhongExponent, Linear, 8, "None", "_mask", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTextureCube( SourceEnvCube, Srgb, 8, "None", "_cube", "Source 1", Default4( 0, 0, 0, 1 ) );
	CreateInputTexture2D( SourceEnvSphere, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
	CreateInputTexture2D( SourceWarpFlow, Linear, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
	CreateInputTexture2D( SourceWarpEmission, Linear, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 1 ) );
	CreateInputTexture2D( SourceTexture2, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );

	#if S_SOURCE_TEXTURE_ANIMATION
		Texture2DArray g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	#else
		Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	#endif
	// Keep RGB and alpha intact: normal alpha is a separate specular mask, not normal data.
	Texture2D g_tNormal < Channel( RGBA, Box( SourceNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tDetail < Channel( RGBA, Box( SourceDetail ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tEnvMask < Channel( RGBA, Box( SourceEnvMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tSelfIllumMask < Channel( RGBA, Box( SourceSelfIllumMask ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tPhongExponent < Channel( RGBA, Box( SourcePhongExponent ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	CreateTextureCube( g_tEnvCube ) < Channel( RGBA, Box( SourceEnvCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( true ); >;
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

	TextureAttribute( RepresentativeTexture, g_tColor );
	TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );

	float3 g_vSourceBaseTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceBaseTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceNormalTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceNormalTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceDetailTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceDetailTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceEnvMaskTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceEnvMaskTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceBaseScroll < Default3( 0, 0, 1 ); >;
	float3 g_vSourceNormalScroll < Default3( 0, 0, 1 ); >;
	float3 g_vSourceDetailScroll < Default3( 0, 0, 1 ); >;
	float3 g_vSourceEnvMaskScroll < Default3( 0, 0, 1 ); >;

	float3 g_vSourceColorTint < Default3( 1, 1, 1 ); >;
	float g_flSourceAlphaTestReference < Default( 0.5 ); >;
	float g_flSourceUseBaseAlpha < Default( 1 ); >;
	float g_flSourceBlendTintByBaseAlpha < Default( 0 ); >;
	float g_flSourceBlendTintColorOverBase < Default( 0 ); >;
	// VTF channels without authored alpha may contain zero padding, not opacity or a specular mask.
	float4 g_vSourceTextureAlphaPresence < Default4( 1, 1, 1, 1 ); >;
	float g_flSourceDetailAlphaPresent < Default( 1 ); >;
	float g_flSourceVertexColor < Default( 0 ); >;
	float g_flSourceVertexAlpha < Default( 0 ); >;
	float g_flSourceHasNormal < Default( 0 ); >;
	float g_flSourceNormalScale < Default( 1 ); >;
	float g_flSourceNormalYSign < Default( 1 ); >;
	// 0 = RGB with authored alpha; 1 = XY/UV88 with reconstructed positive Z and implicit alpha 1.
	int g_nSourceNormalEncoding < Default( 0 ); >;
	float g_flSourceSsBump < Default( 0 ); >;

	float g_flSourceHasDetail < Default( 0 ); >;
	int g_nSourceDetailMode < Default( 0 ); >;
	float g_flSourceDetailBlendFactor < Default( 1 ); >;
	float3 g_vSourceDetailTint < Default3( 1, 1, 1 ); >;
	// Modulation, masks and SSBump use raw linear samples; colored detail modes can request sRGB decoding.
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
	// Mask sources: 0 = white, 1 = base alpha, 2 = normal alpha, 3 = base luminance.
	int g_nSourcePhongMask < Default( 2 ); >;
	// Source's invertphongmask affects the skin environment mask, never the direct Phong mask.
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
	float g_flSourceDiffuseBumpLighting < Default( 1 ); >;
	float g_flSourceDirectLightScale < Default( 1 ); >;

	#include "source1/uv.hlsl"

	float3 SourceNormalize( float3 normal )
	{
		float lengthSquared = dot( normal, normal );
		return lengthSquared > 1e-8 ? normal * rsqrt( max( lengthSquared, 1e-8 ) ) : float3( 0, 0, 1 );
	}

	float3 SourceSsBumpNormal( float3 weights )
	{
		// Source's three nonnegative directional weights are not signed RGB normal components.
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
		float amount = saturate( g_flSourceDetailBlendFactor );
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
				float3 detailNormal = SourceSsBumpNormal( detail.rgb );
				normal = SourceNormalize( lerp( normal, float3( normal.xy + detailNormal.xy, normal.z * detailNormal.z ), amount ) );
				color.rgb *= lerp( 1, dot( detail.rgb, float3( 0.66666667, 0.66666667, 0.66666667 ) ), amount );
				break;
			}
			case 11: color.rgb *= dot( detail.rgb, float3( 0.66666667, 0.66666667, 0.66666667 ) ); break;
		}
	}


	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float2 baseUv = SourceUv( uv, g_vSourceBaseTransformU, g_vSourceBaseTransformV, g_vSourceBaseScroll );
		float2 normalUv = SourceUv( uv, g_vSourceNormalTransformU, g_vSourceNormalTransformV, g_vSourceNormalScroll );
		#if S_SOURCE_FRAMEBUFFER
			float4 baseColor = float4( g_tSourceScene.Sample( g_sSourceClamp, saturate( baseUv ) * g_vFrameBufferCopyInvSizeAndUvScale.zw ).rgb, 1 );
		#else
			float4 baseColor = g_tColor.Sample( g_sColor, SourceFrameUv( baseUv, i.flSourceFrame ) );
		#endif
		if ( g_flSourceMonitor > 0.5 )
		{
			float2 secondUv = SourceUv( uv, g_vSourceTexture2TransformU, g_vSourceTexture2TransformV, g_vSourceTexture2Scroll );
			baseColor *= g_tTexture2.Sample( g_sTexture2, secondUv );
			baseColor.rgb = lerp( baseColor.rgb, baseColor.rgb * baseColor.rgb, g_vSourceMonitorContrast );
			baseColor.rgb = lerp( dot( baseColor.rgb, float3( 1.0 / 3, 1.0 / 3, 1.0 / 3 ) ).xxx, baseColor.rgb, g_vSourceMonitorSaturation ) * g_vSourceMonitorTint;
		}
		baseColor.a = lerp( 1, baseColor.a, saturate( g_vSourceTextureAlphaPresence.x ) );
		bool blendTint = SourceUsesBlendTint();
		if ( !blendTint )
			baseColor.rgb *= g_vSourceColorTint;
		float4 normalTexel = float4( 0.5, 0.5, 1, 1 );
		if ( g_flSourceHasNormal > 0.5 )
			normalTexel = g_tNormal.Sample( g_sNormal, normalUv );
		normalTexel.a = lerp( 1, normalTexel.a, saturate( g_vSourceTextureAlphaPresence.z ) );
		float3 normal = g_flSourceHasNormal > 0.5
			? SourceNormal( normalTexel, g_flSourceSsBump, g_flSourceNormalScale, g_nSourceNormalEncoding ) : float3( 0, 0, 1 );


		float4 color = baseColor;
		float3 detailEmission = 0;
		if ( g_flSourceHasDetail > 0.5 )
		{
			float2 detailUv = SourceUv( uv, g_vSourceDetailTransformU, g_vSourceDetailTransformV, g_vSourceDetailScroll );
			float4 detail = g_tDetail.Sample( g_sDetail, detailUv );
			detail.a = lerp( 1, detail.a, saturate( g_flSourceDetailAlphaPresent ) );
			if ( g_flSourceDetailSrgb > 0.5 )
				detail.rgb = SrgbGammaToLinear( detail.rgb );
			detail.rgb *= g_vSourceDetailTint;
			SourceDetail( color, detail, normal, detailEmission );
		}
		if ( blendTint )
		{
			// Paint follows detail; specular masks and albedo tint still see the unpainted composite.
			baseColor = color;
			color.rgb = SourceBlendTintColor( color.rgb, color.a, g_vSourceColorTint );
		}
		// Masks consume alpha independently of translucency, including in the depth pass.
		float useBaseAlpha = SourceOwnsBaseAlpha() ? 0 : saturate( g_flSourceUseBaseAlpha );
		color.a = lerp( 1, color.a, useBaseAlpha );
		if ( SourceUsesSkin() && g_nSourcePhongMask == 1 )
			normal = float3( 0, 0, 1 );
		float3 tint = i.vVertexColor.rgb * lerp( float3( 1, 1, 1 ), i.vSourceColor.rgb, saturate( g_flSourceVertexColor ) );
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

		float3 normalWs = TransformNormal( normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		#if S_RENDER_BACKFACES
			normalWs *= i.face ? 1 : -1;
		#endif
		{
			Material material = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
			material.Albedo = color.rgb;
			material.Normal = normalWs;
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
			// Standard squares perceptual roughness to obtain the GGX alpha.
			material.Roughness = g_flSourcePhong > 0
				? clamp( pow( 2 / ( max( 1, exponent ) + 2 ), 0.25 ), 0.02, 1 )
				: g_flSourceEnvMap > 0 ? clamp( g_flSourceEnvRoughness, 0.02, 1 ) : 1;
			#if S_MODE_DEPTH
				if ( DepthNormals::WantsDepthNormals() )
					return DepthNormals::Output( normalWs, material.Roughness, opacity );
				return float4( 0, 0, 0, opacity );
			#else
			material.Emission = detailEmission;
			if ( g_flSourceWarpEmission > 0.5 )
			{
				float2 flow = g_tSourceWarpFlow.Sample( g_sWarpFlow, baseUv ).xy + float2( 0.11, 0.124 ) * g_flTime;
				material.Emission += material.Albedo * g_vSourceSelfIllumTint * g_tSourceWarpEmission.Sample( g_sWarpEmission, flow ).rgb;
			}
			if ( g_flSourceSelfIllum > 0 )
			{
				float3 mask = baseColor.aaa;
				if ( g_flSourceHasSelfIllumMask > 0.5 )
					mask = g_tSelfIllumMask.Sample( g_sSelfIllumMask, baseUv ).rgb;
				if ( g_flSourceSelfIllumFresnel > 0.5 )
					mask *= lerp( g_vSourceSelfIllumFresnel.x, g_vSourceSelfIllumFresnel.y,
						pow( 1 - facing, max( g_vSourceSelfIllumFresnel.z, 0.001 ) ) );
				mask = saturate( mask * g_flSourceSelfIllum );
				material.Emission += material.Albedo * g_vSourceSelfIllumTint * mask;
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
			}
			normalWs = material.Normal;
			facing = saturate( dot( normalWs, view ) );
			CombinerInput combiner = ShadingModelStandard::MaterialToCombinerInput( material );
			#if !S_SOURCE_UNLIT
			{
				float phongMask = 1;
				if ( g_flSourcePhong > 0 )
				{
					phongMask = saturate( SourceMask( g_nSourcePhongMask, baseColor, normalTexel.a ) );
					float3 phongTint = float3( 1, 1, 1 );
					if ( any( g_vSourcePhongTint != 0 ) )
						phongTint = g_vSourcePhongTint;
					else if ( g_flSourcePhongAlbedoTint > 0.5 )
						phongTint = lerp( float3( 1, 1, 1 ), baseColor.rgb, exponentTexel.g );
					// Map Source's masks/tint/boost to dielectric reflectance, not metalness.
					combiner.vSpecularColor = saturate( 0.04 * phongTint * max( 0, g_flSourcePhongBoost ) );
				}

				ComputeDirectLighting( lighting, combiner );
				CalculateIndirectLighting( lighting, combiner );
				lighting.vDiffuse *= max( 0, g_flSourceDirectLightScale );
				// Mask the complete standard lobe, including its grazing-angle Fresnel.
				lighting.vSpecular *= phongMask * max( 0, g_flSourceDirectLightScale );
				if ( g_flSourceEnvMap <= 0 )
					lighting.vIndirectSpecular *= phongMask;
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
					float2 maskUv = SourceUv( uv, g_vSourceEnvMaskTransformU, g_vSourceEnvMaskTransformV, g_vSourceEnvMaskScroll );
					mask = g_tEnvMask.Sample( g_sEnvMask, maskUv ).rgb;
				}
				mask = lerp( saturate( mask ), 1 - saturate( mask ), saturate( g_flSourceEnvMaskInvert ) );
				float3 reflection;
				if ( g_flSourceHasEnvCube > 0.5 )
					reflection = g_tEnvCube.SampleLevel( g_sSourceCube, reflect( -view, normalWs ), max( 0, g_flSourceEnvMip ) ).rgb;
				else
				{
					#if S_SOURCE_UNLIT
					reflection = EnvMap::From( material.WorldPosition, i.vPositionSs, normalWs, saturate( g_flSourceEnvRoughness ).xx );
					#else
					reflection = lighting.vIndirectSpecular;
					#endif
				}
				reflection = lerp( reflection, reflection * reflection, saturate( g_vSourceEnvContrast ) );
				reflection = lerp( dot( reflection, float3( 0.299, 0.587, 0.114 ) ).xxx, reflection, g_vSourceEnvSaturation );
				#if !S_SOURCE_UNLIT
					if ( g_flSourceHasEnvCube > 0.5 )
						reflection *= CalcBRDFReflectionFactor( facing, material.Roughness, combiner.vSpecularColor );
				#endif
				if ( g_flSourceHasEnvSphere > 0.5 )
				{
					float2 envUv = normalize( reflect( -view, normalWs ) ).xy;
					lighting.vIndirectSpecular = 0.5 * g_tEnvSphere.Sample( g_sEnvSphere, envUv ).rgb
						* g_vSourceColorTint * (lighting.vDiffuse + lighting.vIndirectDiffuse);
				}
				else
					lighting.vIndirectSpecular = reflection * mask * g_vSourceEnvTint * g_flSourceEnvMap;
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
				+ lighting.vIndirectDiffuse * combiner.vDiffuseColor
				+ lighting.vSpecular + lighting.vIndirectSpecular + combiner.vEmissive;
			float4 output = float4( max( result, float3( 0, 0, 0 ) ), opacity );
			if ( DepthNormals::WantsDepthNormals() )
				return DepthNormals::Output( normalWs, material.Roughness, opacity );
			if ( g_bWireframeMode )
				return g_vWireframeColor;
			if ( ToolsVis::WantsToolsVis() )
				return ShadingModelStandard::DoToolsVis( output, material, lighting );
			return DoAtmospherics( material.WorldPosition, i.vPositionSs.xy, output, S_ADDITIVE_BLEND != 0 );
			#endif
		}
	}
}
