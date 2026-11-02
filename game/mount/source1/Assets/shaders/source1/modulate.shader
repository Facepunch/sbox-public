HEADER
{
	Description = "Source 1 model modulation";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Transparency" );
	Feature( F_SOURCE_MOD2X, 0..1, "Source 1" );
	Feature( F_SOURCE_WRITEZ, 0..1, "Source 1" );
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
	#define CUSTOM_MATERIAL_INPUTS
	#define S_UNLIT 1
	#define S_TRANSLUCENT 1
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
	nointerpolation float flSourceFrame : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"
	#include "source1/uv.hlsl"
	#include "source1/alpha.hlsl"
	#include "source1/frames.hlsl"

	float3 g_vSourceColorTint < Default3( 1, 1, 1 ); >;
	float g_flSourceVertexColor < Default( 0 ); >;
	float3 g_vSourceBaseTransformU < Default3( 1, 0, 0 ); >;
	float3 g_vSourceBaseTransformV < Default3( 0, 1, 0 ); >;
	float3 g_vSourceBaseScroll < Default3( 0, 0, 1 ); >;

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.flSourceFrame = SourceFrame();
		i.vTextureCoords.xy = SourceUv( i.vTextureCoords.xy, g_vSourceBaseTransformU, g_vSourceBaseTransformV, g_vSourceBaseScroll, g_vSourceBaseProxyU, g_vSourceBaseProxyV );
		i.vSourceColor = float4( SourceProxyVector( g_vSourceColorTint, g_vSourceColorTintProxy ), SourceAlpha() )
			* lerp( float4( 1, 1, 1, 1 ), v.vSourceColor, g_flSourceVertexColor );
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_SOURCE_MOD2X, F_SOURCE_MOD2X, Sys( ALL ) );
	StaticCombo( S_SOURCE_WRITEZ, F_SOURCE_WRITEZ, Sys( ALL ) );
	#define DEPTH_STATE_ALREADY_SET
	#define BLEND_MODE_ALREADY_SET
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	#include "source1/frames.hlsl"

	BoolAttribute( DoNotCastShadows, true );
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, S_SOURCE_WRITEZ != 0 );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, DEST_COLOR );
	#if S_SOURCE_MOD2X
		RenderState( DstBlend, SRC_COLOR );
	#else
		RenderState( DstBlend, ZERO );
	#endif
	// Preserve destination alpha; Source's optional depth-in-alpha is not used here.
	RenderState( ColorWriteEnable0, RGB );

	SamplerState g_sSourceWrap < Attribute( "g_tColorSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( SourceColor, Linear, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	#if S_SOURCE_TEXTURE_ANIMATION
		Texture2DArray g_tColor < Channel( RGBA, Box( SourceColor ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#else
		Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	#endif
	TextureAttribute( RepresentativeTexture, g_tColor );
	float g_flSourceTextureAlphaPresent < Default( 1 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 texel = g_tColor.Sample( g_sSourceWrap, SourceFrameUv( i.vTextureCoords.xy, i.flSourceFrame ) );
		texel.a = lerp( 1, texel.a, g_flSourceTextureAlphaPresent );
		float4 result = saturate( texel * i.vSourceColor );
		// Both Source blend modes mix alpha against half gray.
		result.rgb = lerp( float3( 0.5, 0.5, 0.5 ), result.rgb, result.a );
		if ( g_bFogEnabled )
		{
			float3 world = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
			float visibility = Fog::ModulationTransmittance( world, i.vPositionSs.xy );
			result.rgb = lerp( S_SOURCE_MOD2X ? 0.5 : 1.0, result.rgb, saturate( visibility ) );
		}
		return result;
	}
}
