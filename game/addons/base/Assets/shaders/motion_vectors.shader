//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Reconstructs static motion vectors from depth buffer and previous frame matrices";
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
	#include "system.fxc"
	#include "common.fxc"
	#include "sbox_shared.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
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
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );

	#include "common/classes/Depth.hlsl"
	#include "common/classes/Motion.hlsl"

	float4 MainPs( float4 vPositionSs : SV_Position ) : SV_Target0
	{
		float3 vPrevScreenPos = Motion::Get( vPositionSs.xy );
		float2 vMotionVector = vPrevScreenPos.xy - vPositionSs.xy;
		return float4( vMotionVector, Depth::Get( vPositionSs.xy ), 0.0 );
	}
}
