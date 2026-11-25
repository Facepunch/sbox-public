HEADER
{
	Description = "1-Bit Dither";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 position : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
	float4 position : SV_Position;
};

VS
{
	PixelInput MainVs( VertexInput input )
	{
		PixelInput output;
		output.position = float4( input.position.xy, 0, 1 );
		return output;
	}
}

PS
{
	#include "postprocess/common.hlsl"

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

	float4 MainPs( PixelInput input ) : SV_Target0
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
}
