// onebitdither: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The 1-Bit Dither post effect: the scene in two colours through a 4x4 ordered dither, in cells of PixelSize.
// HEADER: Description = "1-Bit Dither";

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );

#include "sbox/post/shared.slangh"

struct VertexInput
{
	float3 position : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
	float4 position : SV_Position;
};

#if ( PROGRAM == VFX_PROGRAM_VS )
[shader( "vertex" )] PixelInput MainVs( VertexInput input )
{
	PixelInput output;
	output.position = float4( input.position.xy, 0, 1 );
	return output;
}

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
#include "sbox/post/common.slangh"

Texture2D SceneColor < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
int PixelSize < Attribute( "PixelSize" ); Default( 2 ); >;
float Threshold < Attribute( "Threshold" ); Default( 0.5 ); >;
float DitherAmount < Attribute( "DitherAmount" ); Default( 1 ); >;
float4 Ink < Attribute( "Ink" ); Default4( 0, 0, 0, 1 ); >;
float4 Paper < Attribute( "Paper" ); Default4( 1, 1, 1, 1 ); >;

static const int bayer[16] =
{
	 0,  8,  2, 10,
	12,  4, 14,  6,
	 3, 11,  1,  9,
	15,  7, 13,  5
};

[shader( "pixel" )] float4 MainPs( PixelInput input ) : SV_Target0
{
	float scale = max( 1, PixelSize );
	uint2 cell = (uint2)floor( (input.position.xy - g_vViewportOffset.xy) / scale );
	float2 center = min( (cell + 0.5) * scale, g_vViewportSize.xy - 0.5 );
	float4 scene = SceneColor.SampleLevel( g_sPointClamp, center * g_vInvViewportSize.xy, 0 );

	float brightness = SrgbLinearToGamma( saturate( GetLuminance( scene.rgb ) ).xxx ).r;
	float pattern = (bayer[(cell.y & 3) * 4 + (cell.x & 3)] + 0.5) / 16.0;
	float cutoff = saturate( Threshold ) + (pattern - 0.5) * saturate( DitherAmount );
	float3 color = brightness >= cutoff ? Paper.rgb : Ink.rgb;
	return float4( SrgbGammaToLinear( saturate( color ) ), scene.a );
}

#endif // PS

