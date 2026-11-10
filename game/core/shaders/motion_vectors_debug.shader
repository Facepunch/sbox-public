//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Visualizes the cached MotionVectors texture (produced by motion_vectors.shader) by sampling it
// and amplifying for display. Used by the tools "Motion Vectors" visualization mode so it works
// regardless of viewport / depth-binding mismatches at debug-overlay time.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Visualize cached motion vectors";
}

MODES
{
	Forward();
	Default();
}

FEATURES
{
}

COMMON
{
	#include "system.fxc"
	#include "common.fxc"
	#include "sbox_shared.fxc"
}

VS
{
	struct VS_INPUT
	{
		float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
		float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	};

	struct VS_OUTPUT
	{
		float4 vPositionPs : SV_Position;
		float2 vTexCoord : TEXCOORD0;
	};

	VS_OUTPUT MainVs( VS_INPUT i )
	{
		VS_OUTPUT o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
		o.vTexCoord = i.vTexCoord;
		return o;
	}
}

PS
{
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );

	Texture2D g_tMotionVectors < Attribute( "MotionVectors" ); SrgbRead( false ); >;

	// Tunable: the MV pixel magnitude that maps to "fully saturated" in the visualization.
	// Default 16 — i.e. 16-pixel motion saturates. Drop to 1 to see sub-pixel motion at full
	// brightness; raise to 64+ if MVs are huge.
	float g_flDebugScale < Attribute( "MotionVectorsDebugScale" ); Default( 16.0 ); >;

	float3 HueToRgb( float h )
	{
		float r = abs( h * 6.0 - 3.0 ) - 1.0;
		float g = 2.0 - abs( h * 6.0 - 2.0 );
		float b = 2.0 - abs( h * 6.0 - 4.0 );
		return saturate( float3( r, g, b ) );
	}

	float4 MainPs( float4 vPositionSs : SV_Position, float2 vTexCoord : TEXCOORD0 ) : SV_Target0
	{
		float2 mv = g_tMotionVectors.Load( int3( int2( vPositionSs.xy ), 0 ) ).rg;
		float fMag = length( mv );
		float fHue = ( atan2( mv.y, mv.x ) / 6.2831853 ) + 0.5;
		float fSat = saturate( fMag / max( g_flDebugScale, 0.0001 ) );
		return float4( HueToRgb( fHue ) * fSat, 1.0 );
	}
}
