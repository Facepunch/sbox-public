HEADER
{
	Description = "Source 1 unlit two-texture model base pass";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Blending" );
	Feature( F_SOURCE_ALPHA_BLEND, 0..1, "Source 1" );
	Feature( F_SOURCE_ALPHA_WRITE, 0..1, "Source 1" );
	Feature( F_SOURCE_IGNORE_Z, 0..1, "Source 1" );
	Feature( F_SOURCE_TEXTURE_ANIMATION, 0..1, "Source 1" );
	Feature( F_SOURCE_PROXIES, 0..1, "Source 1" );
	FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires blending" );
	FeatureRule( Requires1( F_SOURCE_ALPHA_BLEND, F_TRANSLUCENT ), "Alpha blending requires translucency" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	nointerpolation float flSourceFrame : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"
	#include "source1/frames.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vVertexColor.rgb = i.vVertexColor.rgb > 1
			? i.vVertexColor.rgb : pow( max( i.vVertexColor.rgb, 0 ), 2.2 );
		i.flSourceFrame = SourceFrame();
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
	StaticCombo( S_SOURCE_ALPHA_BLEND, F_SOURCE_ALPHA_BLEND, Sys( ALL ) );
	StaticCombo( S_SOURCE_ALPHA_WRITE, F_SOURCE_ALPHA_WRITE, Sys( ALL ) );
	StaticCombo( S_SOURCE_IGNORE_Z, F_SOURCE_IGNORE_Z, Sys( ALL ) );
	#define BLEND_MODE_ALREADY_SET
	#define DEPTH_STATE_ALREADY_SET
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "source1/uv.hlsl"
	#include "source1/frames.hlsl"

	RenderState( DepthEnable, !S_SOURCE_IGNORE_Z );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, !S_TRANSLUCENT && !S_SOURCE_IGNORE_Z );
	RenderState( BlendEnable, S_TRANSLUCENT );
	#if S_SOURCE_ALPHA_BLEND
		RenderState( SrcBlend, SRC_ALPHA );
	#else
		RenderState( SrcBlend, ONE );
	#endif
	#if S_ADDITIVE_BLEND
		RenderState( DstBlend, ONE );
	#elif S_SOURCE_ALPHA_BLEND
		RenderState( DstBlend, INV_SRC_ALPHA );
	#else
		RenderState( DstBlend, ZERO );
	#endif
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, INV_SRC_ALPHA );
	#if S_SOURCE_ALPHA_WRITE
		RenderState( ColorWriteEnable0, RGBA );
	#else
		RenderState( ColorWriteEnable0, RGB );
	#endif
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );

	SamplerState g_sColor < Attribute( "g_tColorSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sTexture2 < Attribute( "g_tTexture2Sampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( SourceColor, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceTexture2, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	#if S_SOURCE_TEXTURE_ANIMATION
		Texture2DArray g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	#else
		Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	#endif
	Texture2D g_tTexture2 < Channel( RGBA, Box( SourceTexture2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	TextureAttribute( RepresentativeTexture, g_tColor );

	float3 g_vSourceColorTint < Default3( 1, 1, 1 ); >;
	float g_flSourceAlpha < Default( 1 ); >;
	float3 g_vSourceBaseTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceBaseTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceTexture2TransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceTexture2TransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceBaseScroll < Default3( 0, 0, 1 ); >;
	float3 g_vSourceTexture2Scroll < Default3( 0, 0, 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		#if S_MODE_DEPTH
			return float4( 0, 0, 0, 1 );
		#else
			float2 uv = SourceUv( i.vTextureCoords.xy, g_vSourceBaseTransformU, g_vSourceBaseTransformV, g_vSourceBaseScroll, g_vSourceBaseProxyU, g_vSourceBaseProxyV );
			float2 uv2 = SourceUv( i.vTextureCoords.xy, g_vSourceTexture2TransformU, g_vSourceTexture2TransformV, g_vSourceTexture2Scroll, g_vSourceTexture2ProxyU, g_vSourceTexture2ProxyV );
			float4 color = g_tColor.Sample( g_sColor, SourceFrameUv( uv, i.flSourceFrame ) )
				* g_tTexture2.Sample( g_sTexture2, uv2 )
				* float4( SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy ), saturate( SourceProxyScalar( g_flSourceAlpha, g_flSourceAlphaProxy ) ) ) * i.vVertexColor;
			#if !S_SOURCE_ALPHA_BLEND
				color.a = 1;
			#endif
			if ( g_bFogEnabled )
			{
				float3 world = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
				#if S_ADDITIVE_BLEND
					color.rgb *= Fog::Transmittance( world, i.vPositionSs.xy );
				#else
					color.rgb = Fog::Apply( world, i.vPositionSs.xy, color.rgb );
				#endif
			}
			return color;
		#endif
	}
}
