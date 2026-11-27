// comicprint: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The Comic Print post effect: rotated halftone plates per ink over a flat underprint.
// HEADER: Description = "Comic Print";

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
float DotSpacing < Attribute( "DotSpacing" ); Default( 5 ); >;
float InkShift < Attribute( "InkShift" ); Default( 1 ); >;
float Strength < Attribute( "Strength" ); Default( 1 ); >;

// A rotated halftone screen per plate. The threshold expands dots until they meet in solid ink.
float Ink( float2 pixel, float2 axis, float coverage )
{
	float2 grid = float2( dot( pixel, axis ), dot( pixel, float2( -axis.y, axis.x ) ) );
	float2 wave = cos( grid * ( 6.283185 / max( DotSpacing, 2.0 ) ) );
	float threshold = 0.5 + 0.25 * (wave.x + wave.y);
	float aa = max( 0.5 * fwidth( threshold ), 0.001 );
	return smoothstep( threshold - aa, threshold + aa, lerp( -aa, 1.0 + aa, coverage ) );
}

[shader( "pixel" )] float4 MainPs( PixelInput input ) : SV_Target0
{
	float2 uv = CalculateViewportUv( input.position.xy );
	float2 pixel = input.position.xy;
	float2 offset = float2( InkShift, 0 ) / g_vRenderTargetSize.xy;
	float4 original = SceneColor.Sample( g_sTrilinearClamp, uv );

	// Separate neutral shadows onto the black plate and offset the cyan and magenta impressions.
	float3 rgb = sqrt( saturate( original.rgb ) );
	float brightest = max( max( rgb.r, rgb.g ), rgb.b );
	float red = sqrt( saturate( SceneColor.Sample( g_sTrilinearClamp, uv - offset ).r ) );
	float green = sqrt( saturate( SceneColor.Sample( g_sTrilinearClamp, uv + offset ).g ) );
	float3 cmy = saturate( 1.0 - float3( red, green, rgb.b ) / max( brightest, 0.001 ) );

	float cyan = Ink( pixel - float2( InkShift, 0 ), float2( 0.966, 0.259 ), cmy.x );
	float magenta = Ink( pixel + float2( InkShift, 0 ), float2( 0.259, 0.966 ), cmy.y );
	float yellow = Ink( pixel, float2( 1, 0 ), cmy.z );
	float black = Ink( pixel, float2( 0.7071, 0.7071 ), 1.0 - brightest );

	float3 paper = float3( 0.96, 0.91, 0.79 );
	paper *= lerp( 1.0, float3( 0.05, 0.72, 0.9 ), cyan );
	paper *= lerp( 1.0, float3( 0.95, 0.06, 0.35 ), magenta );
	paper *= lerp( 1.0, float3( 1, 0.85, 0.06 ), yellow );
	paper *= lerp( 1.0, 0.045, black );
	// A flat underprint keeps silhouettes readable between the individual ink dots.
	float3 flat = floor( rgb * 9.0 + 0.5 ) / 9.0;
	float3 underprint = flat * flat * float3( 0.96, 0.91, 0.79 );
	float3 print = lerp( underprint, paper, 0.42 );
	return float4( lerp( original.rgb, print, saturate( Strength ) ), original.a );
}

#endif // PS

