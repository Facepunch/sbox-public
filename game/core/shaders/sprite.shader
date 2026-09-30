HEADER
{
	DevShader = true;
	Description = "Sprite Shader for S&box";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VS_INPUT
{
	float3 pos : POSITION < Semantic( None ); >;
	float4 uv : TEXCOORD0 < Semantic( None ); >;
	float4 tint : COLOR < Semantic( None ); >;
};

struct PS_INPUT
{
	float4 vPositionPs : SV_Position;
	float4 tint : TEXCOORD9;
	float4 sheetUv : TEXCOORD3;
	float sheetBlend : TEXCOORD4;
};

VS
{
	float4 g_SheetData < Attribute( "BaseTextureSheet" ); >;

	PS_INPUT MainVs( const VS_INPUT v )
	{
		PixelInput i;
		i.vPositionPs = Position3WsToPs( v.pos );
		i.tint = v.tint;

		Sheet::Blended( g_SheetData, v.uv.z, v.uv.w, v.uv.xy, i.sheetUv.xy, i.sheetUv.zw, i.sheetBlend );

		return i;
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS 1
	#include "common/pixel.hlsl"

	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );

	Texture2D g_ColorTexture < Attribute( "BaseTexture" ); SrgbRead( true ); >;

	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, true );

	#if ( D_BLEND == 1 ) 
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, ONE );
		RenderState( DepthWriteEnable, false );
	#else 
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( BlendOpAlpha, ADD );
	#endif

	#if S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif

	#if D_OPAQUE == 1
		RenderState( DepthWriteEnable, true );
		RenderState( BlendEnable, false );
	#endif

	float4 MainPs( PS_INPUT i ) : SV_Target0
	{
		float4 col = g_ColorTexture.Sample( g_sBilinearClamp, i.sheetUv.xy );

		if ( i.sheetBlend > 0 )
		{
			float4 col2 = g_ColorTexture.Sample( g_sBilinearClamp, i.sheetUv.zw );
			col = lerp( col, col2, i.sheetBlend );
		}

		return col * i.tint;
	}
}
