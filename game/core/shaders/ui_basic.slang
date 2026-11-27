// ui_basic: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// HEADER: DevShader = true;
// HEADER: Version = 1;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/ui/features.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/ui/common.slangh"

  
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "sbox/ui/vertex.slangh"

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
#include "sbox/ui/pixel.slangh"

float4 g_vViewport < Source( Viewport ); >;
float4 g_vInvTextureDim < Source( InvTextureDim ); SourceArg( g_tColor ); >;
Texture2D g_tColor < Attribute( "Texture" ); SrgbRead( true ); >;

RenderState( ColorWriteEnable0, RGBA );
RenderState( FillMode, SOLID );
RenderState( CullMode, NONE );

// No depth
RenderState( DepthWriteEnable, false );

#define SUBPIXEL_AA_MAGIC 0.5

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	UI_CommonProcessing_Pre( i );

	float4 vImage = g_tColor.Sample( g_sAniso, i.vTexCoord.xy );
	o.vColor = vImage * i.vColor.rgba;
	return UI_CommonProcessing_Post( i, o, vImage.a );
}

#endif // PS

