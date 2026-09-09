MODES
{
	Default();
	VrForward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
	#include "common.fxc"
	#include "math_general.fxc"
}

struct VS_INPUT
{
	float2 vPositionPs : POSITION < Semantic( position ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( texcoord ); >;
};

struct PS_INPUT
{
	float2 vTexCoord : TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	float4 Viewport < Source( Viewport ); >;

	float4 ScreenToClipPos( float2 positionSs )
	{
		float2 positionClipSpace = 2.0 * ( positionSs.xy - Viewport.xy ) / ( Viewport.zw ) - float2( 1.0, 1.0 );
		positionClipSpace.y *= -1.0;
		return float4( positionClipSpace, 0.0f, 1.0f );
	}

	PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;

		o.vPositionPs.xyzw = ScreenToClipPos( i.vPositionPs.xy );
		o.vTexCoord.xy = i.vTexCoord.xy;

		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	SamplerComparisonState g_scLinearClamp < Filter( COMPARISON_MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	Texture2D g_tTexture < Attribute( "Texture" ); SrgbRead( false ); > ;


	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	//
	// observable by human eye, could probably be done in a way that makes more sense
	//
    float RemapDepth( float flDepth )
    {
        flDepth = RemapValClamped( flDepth, g_flViewportMinZ, g_flViewportMaxZ, 0, 1 );

        float flZScale = g_vInvProjRow3.z;
        float flZTran = g_vInvProjRow3.w;

        flDepth = 1.0 / ( ( flDepth * flZScale + flZTran ) );

		flDepth = RemapValClamped( flDepth, g_flViewportMinZ, 50000, 0.0, 1.0 );

        return flDepth;
    }

	float4 MainPs( PS_INPUT i ) : SV_Target0
	{
		float4 color = float4( 0, 0, 0, 1 );

		{
			// Visualize depth buffer
			#define NUM_SAMPLES 128.0

			for ( int j = 0; j < NUM_SAMPLES; j++ )
			{
				color.rgb += ( 1.0 / NUM_SAMPLES ) * ( g_tTexture.SampleCmpLevelZero( g_scLinearClamp, i.vTexCoord.xy, j / NUM_SAMPLES ).r );
			}
		}
		
		color.rgb = SrgbLinearToGamma( color.rgb );
		return color;
	}
}
