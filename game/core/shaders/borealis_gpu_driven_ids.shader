//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis GPU-driven depth prepass that also writes ids, for depth draws without a pixel shader: depth_only's depth, and each sample's triangle by its first vertex at target 1, for the visibility-driven forward pass. The pipeline takes culling, depth and stencil from the variant it stands in for.";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
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
	#include "vr_common.fxc"
	#include "math_general.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// The vertex's bit in the view's mask; flat, so a pixel gets its triangle's first (provoking) vertex's
	nointerpolation uint nVertexBit : TEXCOORD0;

	#if ( PROGRAM != VFX_PROGRAM_PS )
		noperspective float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "instancing.fxc"
	#include "borealis_gpu_driven.fxc"

	// depth_only's position, pulled as its GPU-driven variant pulls it, so the depth is the same
	PS_INPUT MainVs( uint nVertex : SV_VulkanVertexID, uint nInstance : SV_VulkanInstanceID )
	{
		PS_INPUT o;
		uint2 vInstance = BorealisGpuDrivenInstance( nInstance );
		uint4 vDraw = BorealisGpuDrivenDraw( vInstance.y );
		float3 vPositionOs = asfloat( BorealisGpuDrivenFetch( vDraw.x, 0, nVertex, true ).xyz );

		float3x4 matObjectToWorld = GetTransformMatrix( vInstance.x, 0 );
		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );

		o.nVertexBit = BorealisGpuDrivenVertexBit( nInstance, nVertex );
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( CullMode, DEFAULT );

	// The first vertex's bit + 1 at target 1, where the ids are (target 0 is the variants' own output, and written here
	// only so the ids stay at location 1); 0 is nothing
	struct PS_OUTPUT
	{
		float4 vUnused : SV_Target0;
		uint nId : SV_Target1;
	};

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		o.vUnused = 1.0;
		o.nId = i.nVertexBit + 1;
		return o;
	}
}
