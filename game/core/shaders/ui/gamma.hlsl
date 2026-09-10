#ifndef UI_GAMMA_HLSL
#define UI_GAMMA_HLSL

#include "math_general.fxc"

// UI gamma output ---------------------------------------------------------------------------------------------------------------------------------------
//
// With ui_gamma_blend on, the UI layers blend on sRGB-encoded values like a browser does, so the shader has to write encoded
// output itself. Straight-alpha content keeps its math linear and encodes once at output. Premultiplied textures (D_BLENDMODE 3)
// are premultiplied in sRGB space and can't be decoded exactly, so in gamma mode they stay encoded end to end and skip the encode.

bool g_bUIGammaOutput < Attribute( "UIGammaOutput" ); Default( 0 ); >;

// Frame grabs of a float UI target already hold gamma-space values...
bool g_bUIFrameGrabEncoded < Attribute( "UIFrameGrabEncoded" ); Default( 0 ); >;

// ...but panel layers (filter, mask, isolation) are 8-bit sRGB, so a grab of one always decodes
bool g_bUIInPanelLayer < Attribute( "UIInPanelLayer" ); Default( 0 ); >;

// HDR blows anti-aliased edges out - 30% coverage of a 4.0 colour lands at 1.2 and nothing clamps until the
// swapchain - so keep the part above 1 only where the pixel is nearly covered. flCoverage is the geometric edge
// (SDF, glyph, clip), never opacity: a half-transparent HDR colour is still HDR.
float4 UISoftenHdrEdges( float4 vColor, float flCoverage )
{
	float3 vSdr = min( vColor.rgb, 1.0 );
	vColor.rgb = lerp( vSdr, vColor.rgb, smoothstep( 0.8, 1.0, flCoverage ) );
	return vColor;
}

float4 UIEncodeOutput( float4 vColor )
{
	if ( g_bUIGammaOutput )
	{
		vColor.rgb = SrgbLinearToGamma( vColor.rgb );
	}

	return vColor;
}

// A color authored in sRGB, brought into the shader's working space. In gamma mode that space is sRGB,
// so it stays encoded - a shader that mixes two of these then lands where the blend unit would, which is
// what the browser does. Decoding here and encoding at output is the same thing for a single color, but
// puts any mix in between in the wrong space.
float3 UIDecodeColor( float3 vColor )
{
	return g_bUIGammaOutput ? vColor : SrgbGammaToLinear( vColor );
}

// Raw sRGB premultiplied texel -> the working space for premultiplied content. Decoding to linear has to happen
// on the straight color, so unpremultiply, decode, premultiply again.
float4 UIPremultipliedTexel( float4 vTexel )
{
	if ( g_bUIGammaOutput )
		return vTexel;

	float flAlpha = max( vTexel.a, 0.0001 );
	vTexel.rgb = SrgbGammaToLinear( vTexel.rgb / flAlpha ) * flAlpha;
	return vTexel;
}

#endif
