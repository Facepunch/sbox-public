MODES
{
	Default();
	Forward();
}

FEATURES
{
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float2 vTexCoord : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0f );
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	RenderState( BlendEnable, false );
	RenderState( DepthEnable, false );

	Texture2D g_tSource < Attribute( "GlowSource" ); SrgbRead( false ); >;
	float2 g_vStep < Attribute( "GlowStep" ); >; // uv size of one pixel of the glow texture
	float g_flReach < Attribute( "GlowReach" ); >; // how far the blur reaches, in glow texture pixels
	int g_nExtract < Attribute( "GlowExtract" ); Default( 0 ); >;

	#define TAPS 6

	float3 Fetch( float2 uv )
	{
		float4 s = g_tSource.SampleLevel( g_sTrilinearClamp, uv, 0 );
		if ( g_nExtract == 0 ) return s.rgb;

		float3 c = s.a > 0.00001 ? SrgbGammaToLinear( s.rgb / s.a ) : 0;
		return max( c - 1.0, 0.0 ) * s.a;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTexCoord;
		float2 vTapStep = g_vStep * max( g_flReach / TAPS, 0.5 );

		float3 vSum = Fetch( uv );
		float flWeights = 1.0;

		[unroll]
		for ( int k = 1; k <= TAPS; k++ )
		{
			float w = exp( -( k * k ) / ( 2.0 * 2.4 * 2.4 ) );
			vSum += ( Fetch( uv + vTapStep * k ) + Fetch( uv - vTapStep * k ) ) * w;
			flWeights += w * 2.0;
		}

		return float4( vSum / flWeights, 1.0 );
	}
}
