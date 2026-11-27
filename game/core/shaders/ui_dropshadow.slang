// ui_dropshadow: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
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
#include "sbox/ui/blur.slangh"

float4 g_vViewport < Source( Viewport ); >; 

// Texture Samplers ---------------------------------------------------------------------------------------------------------------------------------------
Texture2D g_tColor < Attribute( "Texture" ); SrgbRead( true ); Default( 1.0 ); >;
float4 g_vInvTextureDim < Source( InvTextureDim ); SourceArg( g_tColor ); >;

//
// Drop-shadow specifics
//
float2 FilterDropShadowOffset < UiType( Slider ); Default2( 0.0f, 0.0f ); Attribute( "FilterDropShadowOffset" ); >;
float FilterDropShadowBlur < UiType( Slider ); Default( 0.0f ); Attribute( "FilterDropShadowBlur" ); >;
float4 FilterDropShadowColor < UiType( Color ); Default4( 0.0f, 0.0f, 0.0f, 1.0f ); Attribute( "FilterDropShadowColor" ); >;

// Layer targets are rounded up in size; the content covers this much of them, from the top left
float2 LayerUVScale < Default2( 1.0f, 1.0f ); Attribute( "LayerUVScale" ); >;

// Always write rgba
RenderState( ColorWriteEnable0, RGBA );
RenderState( FillMode, SOLID );

// Never cull
RenderState( CullMode, NONE );

// No depth
RenderState( DepthWriteEnable, false );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	UI_CommonProcessing_Pre( i );

	// filter: drop-shadow( x y blur color ) - the layer's alpha, offset and blurred. Unlike box-shadow, the blur
	// value here is the gaussian's standard deviation itself, so a drop-shadow reads softer than the same box-shadow.
	// The layer is padded by the offset and the blur's reach, so the quad and the layer line up one to one.
	float2 vShadowUv = i.vTexCoord.xy * LayerUVScale - FilterDropShadowOffset * g_vInvTextureDim.xy;
	float4 vShadowColor = FilterDropShadowColor;
	vShadowColor.rgb = SrgbGammaToLinear( vShadowColor.rgb );
	vShadowColor.a *= GaussianBlurTexture( g_tColor, g_sTrilinearBorder, vShadowUv, FilterDropShadowBlur, g_vInvTextureDim.xy ).a;

	// Blend with original color
	o.vColor = vShadowColor;
	
	return UI_CommonProcessing_Post( i, o );
}

#endif // PS

