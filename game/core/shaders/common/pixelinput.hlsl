// Common Vertex Shader Attributes

#if ( PROGRAM == VFX_PROGRAM_PS )
	float3 vPositionWithOffsetWs : TEXCOORD0;
#else
	float3 vPositionWs : TEXCOORD0;
#endif

float3 vNormalWs 		: TEXCOORD1;
float4 vTextureCoords 	: TEXCOORD2;
float4 vVertexColor 	: TEXCOORD4;

// We could compress these into a single float4
#if ( PS_INPUT_HAS_TANGENT_BASIS )
	float3 vTangentUWs : TEXCOORD6; 
	float3 vTangentVWs : TEXCOORD7; 
#endif

#if ( S_USE_PER_VERTEX_CURVATURE )
	float flSSSCurvature : TEXCOORD11;
#endif

centroid float2 vLightmapUV : TEXCOORD3; // Should be stubbed in specialization when not used

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// System interpolants
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#if ( PROGRAM != VFX_PROGRAM_PS ) 
	float4 vPositionPs : SV_Position;
#else // PS only
	float4 vPositionSs : SV_Position;
	#if ( S_RENDER_BACKFACES )
		bool face : SV_IsFrontFace;
	#endif
#endif

#ifndef COMMON_PS_INPUT_DEFINED
#define COMMON_PS_INPUT_DEFINED
#endif

