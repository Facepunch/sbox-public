HEADER
{
	DevShader = true;
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
	Forward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "ui/features.hlsl"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "ui/common.hlsl"
}
  
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "ui/vertex.hlsl"  
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "ui/pixel.hlsl"
	#include "ui/blur.hlsl"  

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
	float2 FilterDropShadowScale < UiType( Slider ); Default2( 1.0f, 1.0f ); Attribute( "FilterDropShadowScale" ); >;

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		UI_CommonProcessing_Pre( i );

		//
		// Calculate texcoords
		// 
		float2 texCoord = i.vTexCoord.xy;

		// Scale down UVs based on the overgrow
		float2 scale = FilterDropShadowScale;
		
		// Center texcoords
		texCoord = texCoord - ( 1.0f - scale ) * 0.5f;
		texCoord = texCoord / scale;
		
		//
		// Drop shadow
		//
		
		// filter: drop-shadow( x y blur color ) - the layer's alpha, offset and blurred. Unlike box-shadow, the blur
		// value here is the gaussian's standard deviation itself, so a drop-shadow reads softer than the same box-shadow
		float2 vShadowUv = texCoord - FilterDropShadowOffset * g_vInvTextureDim.xy;
		float4 vShadowColor = FilterDropShadowColor;
		vShadowColor.rgb = SrgbGammaToLinear( vShadowColor.rgb );
		vShadowColor.a *= GaussianBlurTexture( g_tColor, g_sTrilinearBorder, vShadowUv, FilterDropShadowBlur, g_vInvTextureDim.xy ).a;

		// Blend with original color
		o.vColor = vShadowColor;
		
		return UI_CommonProcessing_Post( i, o );
	}
}
