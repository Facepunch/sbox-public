//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Basalt GPU-driven depth prepass that also writes ids: depth_only's depth, and each sample's fragment and triangle for the visibility-driven forward pass.";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	Feature( F_RENDER_BACKFACES, 0..1, "Rendering" );
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
	nointerpolation uint nFragment : TEXCOORD0;

	#if ( PROGRAM != VFX_PROGRAM_PS )
		noperspective float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "instancing.fxc"
	#include "basalt_gpu_driven.fxc"

	// Each instance record's fragment: a word per instance, g_nBasaltFragments + instance * 4 in the instance table
	cbuffer BasaltGpuDrivenIds_t
	{
		uint g_nBasaltFragments;
	};

	// depth_only's position, pulled as its GPU-driven variant pulls it, so the depth is the same
	PS_INPUT MainVs( uint nVertex : SV_VulkanVertexID, uint nInstance : SV_VulkanInstanceID )
	{
		PS_INPUT o;
		uint2 vInstance = BasaltGpuDrivenInstance( nInstance );
		uint4 vDraw = BasaltGpuDrivenDraw( vInstance.y );
		float3 vPositionOs = asfloat( BasaltGpuDrivenFetch( vDraw.x, 0, nVertex, true ).xyz );

		float3x4 matObjectToWorld = GetTransformMatrix( vInstance.x, 0 );
		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		o.nFragment = g_bindless_VertexBuffers[ g_nBasaltInstances ].Load( g_nBasaltFragments + nInstance * 4 );
		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	#if ( F_RENDER_BACKFACES )
		RenderState( CullMode, NONE );
	#else
		RenderState( CullMode, DEFAULT );
	#endif

	// (fragment + 1) << 16 | triangle; 0 is nothing
	uint MainPs( PS_INPUT i, uint nPrimitive : SV_PrimitiveID ) : SV_Target0
	{
		return ( ( i.nFragment + 1 ) << 16 ) | ( nPrimitive & 0xffff );
	}
}
