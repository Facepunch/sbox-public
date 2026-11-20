// tools_wireframe: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// With D_THICK_LINES the .shader's geometry stage widens each line into a quad. Here the vertex stage does it: the draw
// runs four vertices per line (ExpandPrimitives), with the line's two ends as per-instance inputs (the second's
// semantics 4 up), and each vertex makes one corner of the quad. F_WIREFRAME is the pipeline's line fill, as before.

// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Wireframe for tools";

#include "sbox/pass.slangh"

Mode( Default );
Mode( Depth, S_MODE_DEPTH );
Mode( ToolsUtil );
Mode( Forward );

Feature( F_ALPHA_TEST, 0..1 );
Feature( F_TRANSLUCENT, 0..1 );
Feature( F_WIREFRAME, 0..1 );
Feature( F_NO_ZTEST, 0..1 );
Feature( F_NO_CULLING, 0..1 );

#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"
#include "vr_common.fxc"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float4 vColor				: COLOR0;

	// Used for dashed/dotted lines
	float4 vFirstLineVertPosPs	: TANGENT0;
	float4 vThisVertPosPs		: TEXCOORD3;

	#if ( S_MODE_DEPTH )
		float flDepthNormalized	: TEXCOORD2;
	#else
		float3 vPositionWs		: TEXCOORD2;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "instancing.fxc"
#include "vr_lighting.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_DEPTH_BIAS, 0..1, Sys( ALL ) );
DynamicCombo( D_SNAP_TO_SCREEN_PIXELS, 0..1, Sys( ALL ) );
DynamicCombo( D_THICK_LINES, 0..1, Sys( ALL ) );

StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );

struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float3 vFirstLineVertPosWs	: TEXCOORD0 < Semantic( uvw ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;

	#if ( D_THICK_LINES )
		// The line's second end
		float3 vPositionOs1		: POSITION4;
		float3 vNormalOs1		: NORMAL4;
		float4 vColor1			: COLOR4;
	#endif

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

// Thick lines: four vertices per line, as a strip, in the geometry stage's order (end 0 and 1 on one side, then the other)
#if ( D_THICK_LINES )
	ExpandPrimitives( LINES, 4, TRIANGLE_STRIP );
#endif

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

float4 g_vOverrideColor < UiType( Color ); Default4( 0.0, 0.0, 0.0, 1.0f ); UiGroup( "Override Color" ); Attribute( "g_vOverrideColor" ); >;
float g_OverrideColorFactor < Default( 0.0 ); Range( 0, 1 ); UiGroup( "Override Color" ); Attribute( "g_OverrideColorFactor" ); >;
float g_LineThickness < Default( 1.0 ); Range( 0, 8 ); UiGroup( "Line Thickness" ); Attribute( "g_LineThickness" ); >;
float g_DepthBiasAmount < Default( 0.005 ); Range( 0, 1 ); UiGroup( "Depth Bias Amount" ); Attribute( "g_DepthBiasAmount" ); >;

// Adds constants for a dynamic combo, must be last otherwise constants get re-arranged
#include "ffd.fxc"

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
float4 SnapToScreenPixels( float4 vPositionPs )
{
	// Projection space->screen space
	float sx = (vPositionPs.x / vPositionPs.w) / g_vInvViewportSize.x;
	float sy = (vPositionPs.y / vPositionPs.w) / g_vInvViewportSize.y;

	// Snap to pixel centers.
	// The 0.01 here deals with rounding problems. Without this, one side of a line might get floored
	// to the int(sx)-1 and the other side might get floored to int(sx).
	sx = floor( sx + 0.01 ) + g_flPixelCenter;
	sy = floor( sy + 0.01 ) + g_flPixelCenter;

	// Screen space->projection space
	vPositionPs.x = sx * g_vInvViewportSize.x * vPositionPs.w;
	vPositionPs.y = sy * g_vInvViewportSize.y * vPositionPs.w;

	return vPositionPs;
}

// The vertex stage's colour: the vertex's, linear, under the override
float4 VertexColor( float4 vColor )
{
	float4 vVertexColor;
	vVertexColor.rgb = SrgbGammaToLinear( vColor.rgb );
	vVertexColor.a = vColor.a;
	return lerp( vVertexColor, float4( SrgbGammaToLinear( g_vOverrideColor.rgb ), g_vOverrideColor.a ), g_OverrideColorFactor );
}

float3 PositionWs( float3x4 matObjectToWorld, float3 vPositionOs )
{
	// Compute the position after applying the freeform deformation, if enabled
	DeformationParameters_t deformParams = ComputeDeformationWeight( vPositionOs.xyz );
	return mul( matObjectToWorld, float4( ComputeDeformedPosition( vPositionOs.xyz, deformParams ), 1.0 ) );
}

#if ( D_THICK_LINES )
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i, uint nCorner : SV_VertexID )
{
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

	// What the vertex stage passed on for each end
	float3 vPositionWs0 = PositionWs( matObjectToWorld, i.vPositionOs );
	float3 vPositionWs1 = PositionWs( matObjectToWorld, i.vPositionOs1 );

	// Transform line endpoints into screen space
	float4 vPositionPs0 = Position3WsToPs( vPositionWs0.xyz );
	float4 vPositionPs1 = Position3WsToPs( vPositionWs1.xyz );

	// Expand line in 2D
	float2 vLineNormal2D = ( vPositionPs1.xy / vPositionPs1.w ) - ( vPositionPs0.xy / vPositionPs0.w );
	vLineNormal2D.xy /= 2.0 * g_vInvViewportSize.xy;	// Convert to pixel space
	vLineNormal2D.xy = normalize( float2( 1.0, -1.0 ) * vLineNormal2D.yx );	// Length of normal is one screen pixel
	vLineNormal2D.xy *= 2.0 * g_vInvViewportSize.xy;	// Back to projective space

	// Corners 0 and 1 are ends 0 and 1 pushed one way, 2 and 3 the other
	bool bEnd1 = ( nCorner & 1 ) != 0;
	float flExtrudeDirectionSign = nCorner < 2 ? 1.0 : -1.0;
	float3 vPositionWs = bEnd1 ? vPositionWs1 : vPositionWs0;

	PS_INPUT o;
	o.vPositionPs.xyzw = bEnd1 ? vPositionPs1.xyzw : vPositionPs0.xyzw;
	o.vPositionPs.xy += flExtrudeDirectionSign * ( 0.5 * g_LineThickness ) * vLineNormal2D.xy * o.vPositionPs.w;
	o.vNormalWs.xyz = normalize( mul( matObjectToWorld, float4( bEnd1 ? i.vNormalOs1.xyz : i.vNormalOs.xyz, 0.0 ) ) );
	o.vColor.rgba = VertexColor( bEnd1 ? i.vColor1 : i.vColor );

	o.vFirstLineVertPosPs.xyzw = Position3WsToPs( vPositionWs0.xyz );	// End 0 is correct here.
	o.vThisVertPosPs.xyzw = o.vPositionPs.xyzw;

	#if ( D_DEPTH_BIAS )
	{
		float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
		float flBiasAmount = lerp( g_DepthBiasAmount * 0.00001f, 0.00001f, flProjDepth );
		o.vPositionPs.z += flBiasAmount * o.vPositionPs.w;
	}
	#endif

	#if ( S_MODE_DEPTH )
	{
		// Dividing by far plane distance so we have depth [0.0, 1.0] relative to the far plane
		o.flDepthNormalized = dot( g_vCameraDirWs.xyz, CalculateCameraToPositionRayWs( vPositionWs.xyz ) ) / g_flFarPlane;
	}
	#else
	{
		o.vPositionWs.xyz = vPositionWs.xyz;
	}
	#endif

	return o;
}
#else
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i )
{
	PS_INPUT o;

	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
	float3 vPositionWs = PositionWs( matObjectToWorld, i.vPositionOs );

	// Worldspace normal
	float3 vNormalWs = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );

	// Projected position
	o.vPositionPs.xyzw = Position3WsToPs(  vPositionWs.xyz );

	#if ( D_SNAP_TO_SCREEN_PIXELS )
		o.vPositionPs = SnapToScreenPixels( o.vPositionPs );
	#endif

	// Setup info for the dotted line if needed
	o.vFirstLineVertPosPs = Position3WsToPs(  i.vFirstLineVertPosWs );
	o.vThisVertPosPs = o.vPositionPs;

	o.vNormalWs.xyz = vNormalWs.xyz;

	#if ( D_DEPTH_BIAS )
	{
		// Just simple depth relative depth bias, for wireframe overlays sloped scale
		// calculations were causing too much offset that would make the wireframe
		// appear over things that should have been behind it.
		// Reverse-Z: flProjDepth is 1 at the near plane, keep a small bias there so close overlays don't z-fight.
		float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
		float flBiasAmount = lerp( g_DepthBiasAmount * 0.00001f, 0.00001f, flProjDepth );
		o.vPositionPs.z += flBiasAmount * o.vPositionPs.w;
	}
	#endif

	o.vColor = VertexColor( i.vColor );

	#if ( S_MODE_DEPTH )
	{
		// Dividing by far plane distance so we have depth [0.0, 1.0] relative to the far plane
		o.flDepthNormalized = dot( g_vCameraDirWs.xyz, CalculateCameraToPositionRayWs( vPositionWs.xyz ) ) / g_flFarPlane;
	}
	#else
	{
		// World space position
		o.vPositionWs.xyz = vPositionWs.xyz;
	}
	#endif

	return o;
}
#endif // D_THICK_LINES
#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "encoded_normals.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );
StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
StaticCombo( S_NO_ZTEST, F_NO_ZTEST, Sys( ALL ) );

StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
StaticComboRule( Allow1( S_MODE_DEPTH, S_TRANSLUCENT ) );

DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );
DynamicCombo( D_NO_CULLING, 0..1, Sys( ALL ) );
DynamicCombo( D_THICK_LINES, 0..1, Sys( ALL ) );

DynamicCombo( D_SHADED, 0..1, Sys( ALL ) );
DynamicComboRule( Allow1( S_MODE_DEPTH, D_SHADED ) );

// Render State -------------------------------------------------------------------------------------------------------------------------------------------

#if ( D_THICK_LINES )
	RenderState( FillMode, SOLID );
#else
	RenderState( FillMode, F_WIREFRAME ? WIREFRAME : SOLID );
#endif

// Write 0 to the stencil so we don't stencil based get overlays drawing overtop
RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 0 );

// Don't backface cull for alpha tested objects (for now)
RenderState( CullMode, ( S_ALPHA_TEST || F_WIREFRAME || F_NO_CULLING || D_NO_CULLING ) ? NONE : BACK );

#if ( S_TRANSLUCENT )
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
#endif

#if ( D_NO_ZTEST || S_NO_ZTEST )
	RenderState( DepthEnable, false );
#else
	RenderState( DepthEnable, true );
	#if ( !S_TRANSLUCENT )
		RenderState( DepthWriteEnable, true );
	#endif
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float g_flPhongExp < Default( 0.1 ); Range( 0.1, 256.0 ); >;
float g_flPhongBoost < Default( 0.1 ); Range( 0.0, 256.0 ); >;
float g_flAlphaTestReference < Default ( 0.3 ); Range( 0.01, 0.99 ); >;

// Pattern to use
//	0 for solid line
//	1 for a dashed line
//	2 for a dotted line
float g_flPatternType < Attribute( "PatternType" );  Default( 0.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	// Depth and depth squared, shadow rendering, etc
	#if ( S_MODE_DEPTH )
		float4 vDepthAndDepthSquared								: SV_Target0;
	#else
		float4 vColor												: SV_Target0;
	#endif
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

	// Allow custom dotted alpha for our tools if not explicitly transparent.
	float flGapDist2 = 4.0;
	#if ( !S_TRANSLUCENT && !S_ALPHA_TEST )
		if( i.vPositionSs.x % flGapDist2 > i.vColor.a * flGapDist2 || i.vPositionSs.y % flGapDist2 > i.vColor.a * flGapDist2 )
			return true;
	#endif

	return false;
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	// Figure out initial albedo.
	float4 vAlbedo;
	vAlbedo.rgba = i.vColor;

	// World-space normal.
	float3 vNormalWs = normalize( i.vNormalWs.xyz );

	// Handle the alpha test.
	#if ( S_ALPHA_TEST )
	{
		if ( vAlbedo.w < g_flAlphaTestReference )
		{
			clip( -1.0 );
		}
	}
	#endif

	// Handle dotted lines.
	if ( RejectDottedLinePixel( i ) )
	{
		clip( -1.0 );
	}

	//----------------//
	// Combine result //
	//----------------//
	PS_OUTPUT o;
	#if ( S_MODE_DEPTH )
	{
		o.vDepthAndDepthSquared.xyzw = float4( i.flDepthNormalized, i.flDepthNormalized * i.flDepthNormalized, 0.0, 0.0 );
	}
	#else
	{
		#if ( D_SHADED )
		{
			float3 vNormalVs = Vector3WsToVs( vNormalWs );
			float3 vPositionVs = Position3WsToVs( i.vPositionWs.xyz ).xyz;

			float3 vViewDir = normalize( -vPositionVs );
			float flViewLight = dot( vViewDir, vNormalVs.xyz );
			float flWorldLight = max( 0, dot( float3( 0.5819, 0.3637, 0.7274 ), vNormalWs ) )
							   + max( 0, dot( float3( -0.5720, -0.6674, -0.4767 ), vNormalWs ) );

			float flLight = saturate( ( flViewLight + flWorldLight ) * 0.5 );
			vAlbedo.rgb *= flLight;
		}
		#endif

		o.vColor = vAlbedo;
	}
	#endif

	return o;
}
#endif // PS
