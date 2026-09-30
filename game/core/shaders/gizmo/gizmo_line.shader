
HEADER
{
	DevShader = true;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Wireframe for tools";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
	Forward();
	
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	Feature( F_NO_ZTEST, 0..1 );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
	#include "common.fxc"
	#include "math_general.fxc"
	#include "vr_common.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float3 vFirstLineVertPosWs	: TEXCOORD0 < Semantic( uvw ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	float4 vOtherVertexPosWs	: TANGENT0 < Semantic( uvw ); >;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct GS_INPUT
{
	float3 vPositionWs			: POSITION;
	float3 vNormalWs			: NORMAL;
	float flLineThickness		: TEXCOORD0;
	float4 vColor				: COLOR0;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float4 vColor				: COLOR0;

	// Used for dashed/dotted lines
	float4 vFirstLineVertPosPs	: TANGENT0;
	float4 vThisVertPosPs		: TEXCOORD3;
	float3 vPositionWs			: TEXCOORD2;

	// VS only
	#if ( ( PROGRAM == VFX_PROGRAM_VS ) || ( PROGRAM == VFX_PROGRAM_GS ) )
		float4 vPositionPs		: SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	#include "instancing.fxc"
	#include "vr_lighting.fxc"
	
	#define VS_OUTPUT GS_INPUT

	float g_LineThickness < Attribute("LineThickness"); Default( 1.0 ); >;
	float g_DepthBiasAmount < Default( 0.0 ); >;
	
	// Adds constants for a dynamic combo, must be last otherwise constants get re-arranged
	#include "ffd.fxc"

	VS_OUTPUT MainVs( VS_INPUT i )
	{
		VS_OUTPUT o;

		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

		// Compute the position after applying the freeform deformation, if enabled
		DeformationParameters_t deformParams = ComputeDeformationWeight( i.vPositionOs.xyz );
		float3 vPositionOs = ComputeDeformedPosition( i.vPositionOs.xyz, deformParams );

		float3 vPositionWs = mul( matObjectToWorld, float4( vPositionOs, 1.0 ) );
		
		// Worldspace normal
		float3 vNormalWs = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );

		o.vPositionWs.xyz = vPositionWs.xyz;
		o.vNormalWs.xyz = vNormalWs.xyz;
		o.flLineThickness = 0.5 * g_LineThickness;
		o.vColor.rgba = i.vColor.rgba;

		float4 vVertexColor;
		vVertexColor.rgb = SrgbGammaToLinear( o.vColor.rgb );
		vVertexColor.a = o.vColor.a;

		o.vColor = vVertexColor;
		return o;
	}
}

GS
{
	#include "vr_lighting.fxc"

	float g_DepthBiasAmount < Default( 0.0 ); Range( 0, 1 ); UiGroup( "Depth Bias Amount" ); >;

	void FillPsOutputStruct( inout PS_INPUT o, GS_INPUT i[2], int nVertId, float flExtrudeDirectionSign, float2 vLineNormal2D )
	{
		o.vPositionPs.xy += flExtrudeDirectionSign * i[ nVertId ].flLineThickness * vLineNormal2D.xy * o.vPositionPs.w;
		o.vNormalWs.xyz = i[ nVertId ].vNormalWs.xyz;
		o.vColor.rgba = i[ nVertId ].vColor.rgba;

		o.vFirstLineVertPosPs.xyzw = Position3WsToPs( i[0].vPositionWs.xyz );	// Index 0 is correct here.
		o.vThisVertPosPs.xyzw = o.vPositionPs.xyzw;

		float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
		float flBiasAmount = lerp( g_DepthBiasAmount * 0.00001f, 0.00001f, flProjDepth );
		o.vPositionPs.z += flBiasAmount * o.vPositionPs.w;

		o.vPositionWs.xyz = i[ nVertId ].vPositionWs.xyz;
	}

	[maxvertexcount(4)]
	void MainGs( line GS_INPUT i[2], inout TriangleStream< PS_INPUT > triStream )
	{
		// Transform line endpoints into screen space
		float4 vPositionPs0 = Position3WsToPs( i[0].vPositionWs.xyz );
		float4 vPositionPs1 = Position3WsToPs( i[1].vPositionWs.xyz );

		// test if any of these points are too close to near plane, long lines can blow up in thickness,
		// so clip them if they're below the threshold 
		const float flNearPlaneW = 0.01;
		bool bPos0 = vPositionPs0.w < flNearPlaneW;
		bool bPos1 = vPositionPs1.w < flNearPlaneW;

		if ( bPos0 != bPos1 )
		{
			float flT = ( flNearPlaneW - vPositionPs0.w ) / ( vPositionPs1.w - vPositionPs0.w );
			float4 vCorrectedPosPs = lerp( vPositionPs0, vPositionPs1, flT );

			if ( bPos0 )
				vPositionPs0 = vCorrectedPosPs;
			else
				vPositionPs1 = vCorrectedPosPs;
		}

		// Expand line in 2D

		float2 vLineNormal2D = ( vPositionPs1.xy / vPositionPs1.w ) - ( vPositionPs0.xy / vPositionPs0.w );
		vLineNormal2D.xy /= 2.0 * g_vInvViewportSize.xy;	// Convert to pixel space
		vLineNormal2D.xy = normalize( float2( 1.0, -1.0 ) * vLineNormal2D.yx );	// Length of normal is one screen pixel
		vLineNormal2D.xy *= 2.0 * g_vInvViewportSize.xy;	// Back to projective space

		PS_INPUT o;
		o.vPositionPs.xyzw = vPositionPs0.xyzw;
		FillPsOutputStruct( o, i, 0, 1.0, vLineNormal2D.xy );
		GSAppendVertex( triStream, o );

		o.vPositionPs.xyzw = vPositionPs1.xyzw;
		FillPsOutputStruct( o, i, 1, 1.0, vLineNormal2D.xy );
		GSAppendVertex( triStream, o );

		o.vPositionPs.xyzw = vPositionPs0.xyzw;
		FillPsOutputStruct( o, i, 0, -1.0, vLineNormal2D.xy );
		GSAppendVertex( triStream, o );

		o.vPositionPs.xyzw = vPositionPs1.xyzw;
		FillPsOutputStruct( o, i, 1, -1.0, vLineNormal2D.xy );
		GSAppendVertex( triStream, o );

		GSRestartStrip( triStream );
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#include "encoded_normals.fxc"

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	StaticCombo( S_NO_ZTEST, F_NO_ZTEST, Sys( ALL ) );
	DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	RenderState( FillMode, SOLID );

	// Write 0 to the stencil so we don't stencil based get overlays drawing overtop
	RenderState( StencilEnable, true );
	RenderState( StencilPassOp, REPLACE );
	RenderState( StencilRef, 0 );
	RenderState( CullMode, NONE );

	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	#if ( D_NO_ZTEST || S_NO_ZTEST )
		RenderState( DepthEnable, false );
	#else
		RenderState( DepthEnable, true );
	#endif

	// Pattern to use
	//	0 for solid line
	//	1 for a dashed line
	//	2 for a dotted line
	float g_flPatternType < Attribute( "PatternType" );  Default( 0.0 ); >;
	float4 g_tint < Attribute( "g_tint" );  Default4( 1, 1, 1, 1 ); >;

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	struct PS_OUTPUT
	{
		float4 vColor : SV_Target0;
	};

	bool RejectDottedLinePixel( PS_INPUT i )
	{
		float flGapDist = 0;

		if ( g_flPatternType == 1 ) // dashed
		{
			flGapDist = 16.0;
		}
		else if ( g_flPatternType == 2 ) // dotted 
		{
			flGapDist = 3.0;
		}

		float4 v1 = i.vFirstLineVertPosPs;
		float4 v2 = i.vThisVertPosPs;

		// Get the screen positions.
		float2 vScreenPos1;
		vScreenPos1.x = v1.x / v1.w / g_vInvViewportSize.x;
		vScreenPos1.y = v1.y / v1.w / g_vInvViewportSize.y;

		float2 vScreenPos2;
		vScreenPos2.x = v2.x / v2.w / g_vInvViewportSize.x;
		vScreenPos2.y = v2.y / v2.w / g_vInvViewportSize.y;

		float flDistMultiplier = 1.0;
		float dist = length( vScreenPos2 - vScreenPos1 ) * flDistMultiplier;

		if ( flGapDist != 0.0 )
		{
			return fmod( dist, flGapDist * 2.0 ) > flGapDist;
		}

		return false;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		if ( RejectDottedLinePixel( i ) )
		{
			clip( -1.0 );
		}

		PS_OUTPUT o;
		o.vColor = i.vColor * g_tint;
		return o;
	}
}
