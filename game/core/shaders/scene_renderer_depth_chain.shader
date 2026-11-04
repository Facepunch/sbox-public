//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Managed scene renderer - the depth chain drawn as bands of linear depth, so a camera's command list can put it in a frame both renderers draw (SceneLab).";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Forward();
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "common.fxc"

	struct VS_INPUT
	{
		float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
		float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	};

	struct VS_OUTPUT
	{
		float4 vPositionPs : SV_Position;
	};

	VS_OUTPUT MainVs( VS_INPUT i )
	{
		VS_OUTPUT o;
		o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "common.fxc"
	#include "math_general.fxc"
	#include "common/classes/Depth.hlsl"

	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	// World units per band
	float BandSize < Attribute( "BandSize" ); Default( 40.0 ); >;

	float4 MainPs( float4 vPositionPs : SV_Position ) : SV_Target0
	{
		// Bands of linear depth: a depth difference of a few units shows as a jump in colour
		float flDepth = Depth::GetLinear( vPositionPs.xy );
		return float4( frac( flDepth / BandSize ), frac( flDepth / ( BandSize * 5.0 ) ), 0.25, 1.0 );
	}
}
