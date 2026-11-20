// tools_solid: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
// HEADER: Description = "Solid/stripes/checkerboard for tools";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );
Mode( PrepassGBuffer, S_MODE_PREPASS_GBUFFER );
Mode( FullDeferredGBuffer, S_MODE_FULL_DEFERRED_GBUFFER );
Mode( DeferredGather, S_MODE_DEFERRED_GATHER );
Mode( ToolsUtil );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_ALPHA_TEST, 0..1 );
Feature( F_PHONG, 0..1 );
Feature( F_UNLIT, 0..1 );
Feature( F_TRANSLUCENT, 0..1 );
Feature( F_NO_ZTEST, 0..1 );
Feature( F_NO_CULLING, 0..1 );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "vr_common.fxc"
#include "common.fxc"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionOs			: POSITION < Semantic( PosXyz ); >;
	float3 vNormalOs			: NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;

	// Instancing data
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vNormalWs			: TEXCOORD0;
	float4 vColor				: COLOR0;

	// Used for dotted/dashed lines
	float4 vThisVertPosPs		: TEXCOORD3;

	#if ( S_MODE_PREPASS_GBUFFER || S_MODE_FULL_DEFERRED_GBUFFER )
		float2 vDepthZWPs		: TEXCOORD2;
	#else
		float3 vPositionWs		: TEXCOORD2;
	#endif

	// VS only
	#if ( ( PROGRAM == VFX_PROGRAM_VS ) || ( PROGRAM == VFX_PROGRAM_GS ) )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( ( PROGRAM == VFX_PROGRAM_PS ) && ( S_MODE_DEFERRED_GATHER ) )
		float4 vPositionSs		: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/instancing.slangh"
#include "vr_common.fxc"
#include "vr_lighting.fxc"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_DEPTH_BIAS, 0..1, Sys( ALL ) );
DynamicCombo( D_SNAP_TO_SCREEN_PIXELS, 0..1, Sys( ALL ) );

StaticCombo( S_MODE_PREPASS_GBUFFER, 0..1, Sys( ALL ) );
StaticCombo( S_MODE_FULL_DEFERRED_GBUFFER, 0..1, Sys( ALL ) );
StaticComboRule( Allow1( S_MODE_PREPASS_GBUFFER, S_MODE_FULL_DEFERRED_GBUFFER ) );

#define VS_OUTPUT PS_INPUT

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

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

[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i )
{
	VS_OUTPUT o;
	
	float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
	float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );
	
	// Worldspace normal
	float3 vNormalWs = normalize( mul( matObjectToWorld, float4( i.vNormalOs.xyz, 0.0 ) ) );

	
	{
		// Projected position
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
		
		#if ( D_SNAP_TO_SCREEN_PIXELS )
			o.vPositionPs = SnapToScreenPixels( o.vPositionPs );
		#endif

		// Setup info for the dotted line if needed
		o.vThisVertPosPs = o.vPositionPs;

		o.vNormalWs.xyz = vNormalWs.xyz;

		#if ( D_DEPTH_BIAS )
		{
			// Fake slope-scaled depth bias using the face normal of the face belonging to this line
			float flSlope = dot( vNormalWs.xyz, CalculatePositionToCameraDirWs( vPositionWs.xyz ) );
			float flFrontFacing = ( flSlope > 0.0 ) ? 1.0 : 0.0;
			flSlope = saturate( flSlope );
			
			float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
			float flFacingBias = lerp( 0.0001, 0.0001, flProjDepth );
			float flObliqueBias = lerp( 0.001, 0.001, flProjDepth );
			
			float flDepthBias = lerp( flObliqueBias, flFacingBias, flSlope );
			o.vPositionPs.z -= flFrontFacing * flDepthBias * o.vPositionPs.w;
		}
		#endif

		// Texture coordinate
		o.vColor = i.vColor;

		#if ( S_MODE_PREPASS_GBUFFER || S_MODE_FULL_DEFERRED_GBUFFER )
		{
			o.vDepthZWPs.xy = o.vPositionPs.zw;
		}
		#else
		{
			// World space position
			o.vPositionWs.xyz = vPositionWs.xyz;
		}
		#endif
	}

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/encoding.slangh"

// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );

StaticCombo( S_MODE_PREPASS_GBUFFER, 0..1, Sys( ALL ) );
StaticCombo( S_MODE_FULL_DEFERRED_GBUFFER, 0..1, Sys( ALL ) );
StaticCombo( S_MODE_DEFERRED_GATHER, 0..1, Sys( ALL ) );
StaticComboRule( Allow1( S_MODE_PREPASS_GBUFFER, S_MODE_FULL_DEFERRED_GBUFFER, S_MODE_DEFERRED_GATHER ) );

StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
StaticCombo( S_PHONG, F_PHONG, Sys( ALL ) );
StaticCombo( S_UNLIT, F_UNLIT, Sys( ALL ) );
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
StaticCombo( S_NO_ZTEST, F_NO_ZTEST, Sys( ALL ) );
StaticCombo( S_NO_CULLING, F_NO_CULLING, Sys( ALL ) );

// Artist input textures ----------------------------------------------------------------------------------------------------------------------------------

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------

#if ( S_UNLIT == 0 )
	Texture2D g_tLightBuffer < Attribute( "CustomTexture0" ); SrgbRead( false ); >;
	Texture2D g_tSpecBuffer < Attribute( "CustomTexture1" ); SrgbRead( false ); >;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( FillMode, SOLID );

// Force stencil to mark pixels as rendered outside for now
RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );

// Don't backface cull for alpha tested objects (for now)
#if ( S_ALPHA_TEST || S_NO_CULLING )
	RenderState( CullMode, NONE );
#endif

#if ( S_TRANSLUCENT )
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
#else
	#if ( S_MODE_DEFERRED_GATHER )
		RenderState( DepthWriteEnable, false );

		#if ( S_ALPHA_TEST )
			RenderState( DepthFunc, EQUAL );
		#else
			RenderState( DepthFunc, GREATER_EQUAL );
		#endif
	#endif
#endif

#if ( D_NO_ZTEST || S_NO_ZTEST )
	RenderState( DepthEnable, false );
#else
	RenderState( DepthEnable, true );
	#if ( S_TRANSLUCENT )
	#elif ( S_MODE_DEFERRED_GATHER )
	#else
		RenderState( DepthWriteEnable, true );
	#endif
#endif

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float g_flPhongExp < Default( 0.1 ); Range( 0.1, 256.0 ); >;
float g_flPhongBoost < Default( 0.1 ); Range( 0.0, 256.0 ); >;
float g_flAlphaTestReference < Default ( 0.3 ); Range( 0.01, 0.99 ); >;

// Pattern to use
//	0 for solid line
//	1 for stripes
//	2 for checkerboard
float g_flPatternType < Attribute( "PatternType" );  Default( 0.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	// Prepass g-buffer output
	#if ( S_MODE_PREPASS_GBUFFER )
		float4 vNormalWsSpecExp										: SV_Target0;
		float4 vDepthAndDepthSquared								: SV_Target1;
	#elif ( S_MODE_FULL_DEFERRED_GBUFFER )
		float4 vNormalWsSpecExp										: SV_Target0;
		float4 vAlbedoSpecAmt										: SV_Target1;
		float4 vDepthAndDepthSquared								: SV_Target2;

	#elif ( S_MODE_DEFERRED_GATHER )
		float4 vColor												: SV_Target0;
	#else
		float4 vColor												: SV_Target0;
	#endif
};

bool RejectDottedLinePixel( PS_INPUT i )
{
	float flGapDist = 0;
	if ( g_flPatternType == 1 ) // stripes
	{
		flGapDist = 6.0;
	}
	else if ( g_flPatternType == 2 ) // checkerboard 
	{
		flGapDist = 4.0;
	}

	float4 v1 = i.vThisVertPosPs;

	float2 vScreenPos;
	vScreenPos.x = v1.x / v1.w / g_vInvViewportSize.x;
	if ( vScreenPos.x < 0 )
	{
		vScreenPos.x *= -1;
		vScreenPos.x += flGapDist; 
	}
	vScreenPos.y = v1.y / v1.w / g_vInvViewportSize.y;
	if ( vScreenPos.y < 0 )
	{
		vScreenPos.y *= -1; 
		vScreenPos.y += flGapDist; 
	}

	if ( g_flPatternType == 1 ) // stripes
	{
		vScreenPos.y = 0;
		//vScreenPos.y = v1.y / v1.w / g_vInvViewportSize.y;
		float dist = length( vScreenPos );

		return fmod( dist, flGapDist * 2.0 ) > flGapDist;
	}
	else if ( g_flPatternType == 2 ) // checkerboard 
	{			
		bool xOk = ( fmod( vScreenPos.x, flGapDist * 2.0 ) > flGapDist );
		bool yOk = ( fmod( vScreenPos.y, flGapDist * 2.0 ) > flGapDist );

		bool bResult = ( xOk && yOk );
		if ( !bResult )
		{
			bResult = ( !xOk && !yOk );
		}

		return bResult;
	}
	else
	{
		return false;
	}
}

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	// Figure out initial albedo.
	float4 vAlbedo;
	vAlbedo.rgba = i.vColor;

	// World-space normal.
	float3 vNormalWs = normalize( i.vNormalWs.xyz );

	// Pack specular exp into a texture-friendly format (0..1 range).  Solving the equation we use for specular
	// ( lSpecExp = 1.0 - flSpecTexel + ( 150.0 * flSpecTexel ) ) for flSpecTexel gives:
	float flSpecExp = g_flPhongExp;
	flSpecExp = ( flSpecExp - 1.0 ) / 149.0;

	// Handle the alpha test.
	#if ( S_ALPHA_TEST && !S_MODE_DEFERRED_GATHER )
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
	#if ( S_MODE_PREPASS_GBUFFER || S_MODE_FULL_DEFERRED_GBUFFER )
	{
		o.vNormalWsSpecExp.xyz = EncodeNormal( vNormalWs.xyz );
		o.vNormalWsSpecExp.w = flSpecExp;
		
		#if ( S_MODE_FULL_DEFERRED_GBUFFER )
		{
			// Pack albedo and specular amount
			float flSpecularAmt = 0.0;
			o.vAlbedoSpecAmt.xyzw = float4( vAlbedo.xyz, flSpecularAmt );
		}
		#endif

		// Output depth for d3d9 pc
		o.vDepthAndDepthSquared.xyzw = ( i.vDepthZWPs.x / i.vDepthZWPs.y ).xxxx;
	}
	#elif ( S_MODE_DEFERRED_GATHER )
	{

		#if ( S_UNLIT == 0 )
		{
			float2 vScreenUv = CalculateViewportUvFromInvSize( i.vPositionSs.xy, g_vInvGBufferSize.xy );
			float4 vLightingTexel = g_tLightBuffer.Sample( g_sPointWrap, vScreenUv.xy );
			float3 vSpecularColor = g_tSpecBuffer.Sample( g_sPointWrap, vScreenUv.xy ).rgb;

			// HACK!!!: Convert albedo into linear space, assuming that it was authored in 2.2 gamma space
			const float flFromTextureGamma = 2.2;
			const float flToMonitorGamma = 1.0 / 2.2;

			float3 vLinearAlbedo = pow( vAlbedo, float4( flFromTextureGamma, flFromTextureGamma, flFromTextureGamma, flFromTextureGamma ) ).rgb;
			float3 vDiffuseColor = vLightingTexel.rgb * vLinearAlbedo.rgb;
			o.vColor.rgb = pow( vDiffuseColor.rgb, float3( flToMonitorGamma, flToMonitorGamma, flToMonitorGamma ) ).rgb;

			o.vColor.rgb += vSpecularColor.rgb * g_flPhongBoost;
			o.vColor.a = vAlbedo.a;
		}
		#else
		{
			o.vColor = i.vColor;
		}
		#endif
	}
	#else
	{
		// We should never get in here..
		o.vColor = vAlbedo;
	}
	#endif

	return o;
}

#endif // PS

