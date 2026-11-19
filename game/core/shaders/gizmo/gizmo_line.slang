// gizmo_line: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
// The .shader's geometry stage widens each line into a quad. Here the vertex stage does it: the draw runs four
// vertices per line (ExpandPrimitives), with the line's two ends as per-instance inputs (the second's semantics 4 up),
// and each vertex makes one corner of the quad as the geometry stage made it.

// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Wireframe for tools";

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );

Feature( F_NO_ZTEST, 0..1 );

#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"
#include "vr_common.fxc"

struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float3 vFirstLineVertPosWs	: TEXCOORD0 < Semantic( uvw ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	float4 vOtherVertexPosWs	: TANGENT0 < Semantic( uvw ); >;

	// The line's second end
	float3 vPositionOs1			: POSITION4;
	float3 vNormalOs1			: NORMAL4;
	float4 vColor1				: COLOR4;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float4 vColor				: COLOR0;

	// Used for dashed/dotted lines
	float4 vFirstLineVertPosPs	: TANGENT0;
	float4 vThisVertPosPs		: TEXCOORD3;
	float3 vPositionWs			: TEXCOORD2;

	// Across the line in pixels: x from its centre (the quad's edges are half a pixel past the line's), y the line's half
	// thickness. Linear in screen space, so the pixel shader fades the edge over one pixel
	noperspective float2 vAcross	: TEXCOORD4;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
#include "instancing.fxc"
#include "vr_lighting.fxc"

// Four vertices per line, as a strip: the geometry stage's order (end 0 and 1 on one side, then on the other)
ExpandPrimitives( LINES, 4, TRIANGLE_STRIP );

float g_LineThickness < Attribute("LineThickness"); Default( 1.0 ); >;
float g_DepthBiasAmount < Default( 0.0 ); >;

// Adds constants for a dynamic combo, must be last otherwise constants get re-arranged
#include "ffd.fxc"

// The vertex stage's work for one end: world position, normal and linear colour
void LineEnd( float3x4 matObjectToWorld, float3 vPositionOs, float3 vNormalOs, float4 vColor, out float3 vPositionWs, out float3 vNormalWs, out float4 vLinearColor )
{
	DeformationParameters_t deformParams = ComputeDeformationWeight( vPositionOs.xyz );
	vPositionOs = ComputeDeformedPosition( vPositionOs.xyz, deformParams );
	vPositionWs = mul( matObjectToWorld, float4( vPositionOs, 1.0 ) );
	vNormalWs = normalize( mul( matObjectToWorld, float4( vNormalOs.xyz, 0.0 ) ) );
	vLinearColor = float4( SrgbGammaToLinear( vColor.rgb ), vColor.a );
}

[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i, uint nCorner : SV_VertexID )
{
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	float3 vPositionWs0, vNormalWs0, vPositionWs1, vNormalWs1;
	float4 vColor0, vColor1;
	LineEnd( matObjectToWorld, i.vPositionOs, i.vNormalOs, i.vColor, vPositionWs0, vNormalWs0, vColor0 );
	LineEnd( matObjectToWorld, i.vPositionOs1, i.vNormalOs1, i.vColor1, vPositionWs1, vNormalWs1, vColor1 );

	// Transform line endpoints into screen space
	float4 vPositionPs0 = Position3WsToPs( vPositionWs0.xyz );
	float4 vPositionPs1 = Position3WsToPs( vPositionWs1.xyz );

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

	// Corners 0 and 1 are ends 0 and 1 pushed one way, 2 and 3 the other
	bool bEnd1 = ( nCorner & 1 ) != 0;
	float flExtrudeDirectionSign = nCorner < 2 ? 1.0 : -1.0;
	float flLineThickness = 0.5 * g_LineThickness;

	// Half a pixel wider each side than the line, for its antialiased edge
	float flHalfWidth = flLineThickness + 0.5;

	PS_INPUT o;
	o.vPositionPs.xyzw = bEnd1 ? vPositionPs1.xyzw : vPositionPs0.xyzw;
	o.vPositionPs.xy += flExtrudeDirectionSign * flHalfWidth * vLineNormal2D.xy * o.vPositionPs.w;
	o.vAcross = float2( flExtrudeDirectionSign * flHalfWidth, flLineThickness );
	o.vNormalWs.xyz = bEnd1 ? vNormalWs1.xyz : vNormalWs0.xyz;
	o.vColor.rgba = bEnd1 ? vColor1.rgba : vColor0.rgba;

	o.vFirstLineVertPosPs.xyzw = Position3WsToPs( vPositionWs0.xyz );
	o.vThisVertPosPs.xyzw = o.vPositionPs.xyzw;

	float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
	float flBiasAmount = lerp( g_DepthBiasAmount * 0.00001f, 0.00001f, flProjDepth );
	o.vPositionPs.z += flBiasAmount * o.vPositionPs.w;

	o.vPositionWs.xyz = bEnd1 ? vPositionWs1.xyz : vPositionWs0.xyz;
	return o;
}
#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
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

// How much of the pixel the line covers: 1 inside, 0 half a pixel past its edge, a one-pixel ramp centred on the edge
// (as Hud/line.shader's analytical antialiasing), so lines are smooth without multisampling and keep their thickness
float LineCoverage( float2 vAcross )
{
	return saturate( vAcross.y + 0.5 - abs( vAcross.x ) );
}

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

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	if ( RejectDottedLinePixel( i ) )
	{
		clip( -1.0 );
	}

	PS_OUTPUT o;
	o.vColor = i.vColor * g_tint;
	o.vColor.a *= LineCoverage( i.vAcross );
	return o;
}
#endif // PS
