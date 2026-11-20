// tools_visualize_tangent_frame: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The .shader's geometry stage draws each triangle corner's normal (or tangent and bitangent) as lines. Here the draw
// runs two vertices per line for each vertex of the draw (ExpandPrimitives, the vertex as per-instance inputs): the same
// lines, each drawn once where the geometry stage drew it once per triangle using the vertex.

// HEADER: DevShader = true;
// HEADER: Description = "Debug shader that render normals or tangent space as lines";

#include "sbox/pass.slangh"

Mode( ToolsUtil, S_MODE_FORWARD );

Feature( F_SHOW_TANGENTS, 0..1, "General" );
Feature( F_SHOW_BENT_NORMALS, 0..1, "General" );

#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "common.fxc"
#include "math_general.fxc"

#define SUPPORTS_COMPRESSED_NORMAL_AND_TANGENT 1

struct PS_INPUT
{
	float3 vVertexColor : COLOR0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "instancing.fxc"
#include "vs_decompress.fxc"
#include "morph.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_MODE_FORWARD, 0..1, Sys( ALL ) );
StaticCombo( S_SHOW_TANGENTS, F_SHOW_TANGENTS, Sys( ALL ) );
StaticCombo( S_SHOW_BENT_NORMALS, F_SHOW_BENT_NORMALS, Sys( ALL ) );

struct VS_INPUT
{
	float3 vPositionOs		: POSITION < Semantic( PosXyz ); >;
	float4 vNormalOs		: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;

	#if ( S_SHOW_TANGENTS )
		float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	#endif

	#if ( S_SHOW_BENT_NORMALS )
		float3 vBentNormalOs : TEXCOORD3 < Semantic( BentNormal ); >;
	#endif

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

// Lines for each vertex: the normal, or the tangent and the bitangent
#if ( S_SHOW_TANGENTS )
	ExpandPrimitives( VERTICES, 4, LINE_LIST );
#else
	ExpandPrimitives( VERTICES, 2, LINE_LIST );
#endif

// The geometry stage's constant
float g_flVectorLength  < Default( 1.0 ); Range( 0.0, 1000.0 ); Attribute( "g_flVectorLength" ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i, uint nLineVertex : SV_VertexID )
{
	//---------------------------------------------------//
	// Generate Object->World matrix and animation scale //
	//---------------------------------------------------//
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	//--------------//
	// World normal //
	//--------------//
	float3 vNormalWs;
	float3 vTangentUWs = 0;
	float3 vTangentVWs = 0;
	float4 vInputNormalOs;
	float4 vInputTangentUOs_flTangentVSign = float4( 0.0, 0.0, 1.0, 1.0 );
	if ( !g_bUncompressedTangentFrame )
	{
		vInputNormalOs.xyzw = i.vNormalOs.xyzw;
	}
	else
	{
		vInputNormalOs.xyzw = float4( i.vNormalOs.xyz, 1.0 );

		#if ( S_SHOW_TANGENTS )
		{
			vInputTangentUOs_flTangentVSign.xyzw = i.vTangentUOs_flTangentVSign.xyzw;
		}
		#endif
	}

	float3 vPositionOs = i.vPositionOs.xyz;
	#if ( S_SHOW_BENT_NORMALS )
	{
		vNormalWs = normalize( mul( matObjectToWorld, float4( i.vBentNormalOs.xyz, 0.0 ) ) );
	}
	#elif ( S_SHOW_TANGENTS )
	{
		//--------------------------------//
		// Tangent space and world normal //
		//--------------------------------//
		float4 vTangentUOs_flTangentVSign;
		float3 vNormalOs;
		DecompressNormalTangent( vInputNormalOs.xyzw, vInputTangentUOs_flTangentVSign.xyzw, vNormalOs.xyz, vTangentUOs_flTangentVSign.xyzw );

		vNormalWs.xyz = normalize( mul( matObjectToWorld, float4( vNormalOs.xyz, 0.0 ) ) );

		float flTangentVSign = vTangentUOs_flTangentVSign.w;

		// Transform tangentU into world space
		vTangentUWs = mul( matObjectToWorld, float4( vTangentUOs_flTangentVSign.xyz, 0.0 ) );

		// Force tangentU perpendicular to normal and normalize
		vTangentUWs.xyz = normalize( vTangentUWs.xyz - ( vNormalWs.xyz * dot( vTangentUWs.xyz, vNormalWs.xyz ) ) );

		// Generate tangentV using cross product
		vTangentVWs = cross( vNormalWs.xyz, vTangentUWs.xyz ) * flTangentVSign;
	}
	#else
	{
		//-------------------//
		// World normal only //
		//-------------------//
		float3 vNormalOs;
		DecompressNormal( vInputNormalOs.xyzw, vNormalOs.xyz );
		vNormalWs = normalize( mul( matObjectToWorld, float4( vNormalOs.xyz, 0.0 ) ) );
	}
	#endif

	//----------------//
	// World position //
	//----------------//
	float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );

	// Each line runs from the vertex (its first vertex) to the vertex plus the vector (its second)
	bool bTip = ( nLineVertex & 1 ) != 0;
	PS_INPUT o;

	#if ( S_SHOW_TANGENTS )
	{
		// The tangent in red, then the bitangent in green
		bool bTangentV = nLineVertex >= 2;
		float3 vVector = bTangentV ? vTangentVWs : vTangentUWs;
		o.vVertexColor.rgb = bTangentV ? ( bTip ? float3( 0.3, 1.0, 0.3 ) : float3( 0.0, 1.0, 0.0 ) ) : ( bTip ? float3( 1.0, 0.3, 0.3 ) : float3( 1.0, 0.0, 0.0 ) );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz + ( bTip ? g_flVectorLength * vVector.xyz : 0.0 ) );
	}
	#else
	{
		// Normal
		#if ( S_SHOW_BENT_NORMALS )
			float3 vTipColor = float3( 0.3, 1.0, 1.0 );
		#else
			float3 vTipColor = float3( 0.3, 0.3, 1.0 );
		#endif
		o.vVertexColor.rgb = bTip ? vTipColor : float3( 0.0, 0.0, 1.0 );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz + ( bTip ? g_flVectorLength * vNormalWs.xyz : 0.0 ) );
	}
	#endif

	return o;
}
#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( CullMode, NONE );
RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
RenderState( ColorWriteEnable0, RGBA );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;
	o.vColor = float4( i.vVertexColor.rgb, 1.0 );
	return o;
}
#endif // PS
