//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Borealis meshlet ids prepass: a task group per listed fragment culls its meshlets as borealis_meshlet_cull_cs does and launches a mesh group per kept meshlet, which draws its triangles' depth and ids as borealis_gpu_driven_ids does.";
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
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// The work is in borealis_meshlet_ids.slang, which Borealis.Gpu draws with task and mesh stages, which vfx doesn't have.
// This names the shader and its mode, with stand-in programs.
struct PS_INPUT
{
	nointerpolation uint nVertexBit : TEXCOORD0;

	#if ( PROGRAM != VFX_PROGRAM_PS )
		noperspective float4 vPositionPs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	PS_INPUT MainVs( uint nVertex : SV_VertexID )
	{
		PS_INPUT o;
		o.vPositionPs = float4( 0, 0, 0, 1 );
		o.nVertexBit = nVertex;
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
