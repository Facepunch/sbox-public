// tools_selection_outline.shader as a pass module: Basalt.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//===================== Copyright (c) Valve Corporation. All Rights Reserved. ======================
//
//==================================================================================================

//--------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Shader to draw the selection outline in hammer";

//--------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//--------------------------------------------------------------------------------------------------

//--------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "math_general.fxc"

//--------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionSs : POSITION < Semantic( PosXyz ); >;
};

//--------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs			: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs			: SV_Position;
	#endif
};

//--------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// Main -------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xyzw = float4( i.vPositionSs, 1.0 );

	return o;
}

#endif // VS

//--------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
DynamicCombo( D_DEPTH_OUTLINE, 0..1, Sys( SM >= 40 ) );
DynamicCombo( D_MSAA_MODE, 0..2, Sys( SM >= 40 ) );
DynamicComboRule( Requires1( D_MSAA_MODE, D_DEPTH_OUTLINE ) );

// Textures ---------------------------------------------------------------
Texture2D g_tInput < Attribute( "InputTexture" ); SrgbRead( false ); Filter( MIN_MAG_LINEAR_MIP_POINT ); >;

#if ( D_MSAA_MODE )
	CreateTexture2DMSArg( g_tDepth, 1 ) < Attribute( "DepthBuffer" ); SrgbRead( false ); Filter( MIN_MAG_MIP_POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;
#else
	Texture2D g_tDepth < Attribute( "DepthBuffer" ); SrgbRead( false ); Filter( MIN_MAG_MIP_POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;
#endif

// Render State -----------------------------------------------------------
RenderState( ColorWriteEnable0, RGBA );

RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( DepthFunc, ALWAYS );

RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );

// Globals ----------------------------------------------------------------
float4 g_vInvTexDim < Source( InvTextureDim ); SourceArg( g_tInput ); >;
float4 g_vOutlineColorA < Attribute( "OutlineColorA" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
float4 g_vOccludedColorA < Attribute( "OccludedColorA" ); Default4( 1.0, 1.0, 1.0, 0.25 ); >;
float4 g_vOutlineColorB < Attribute( "OutlineColorB" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
float4 g_vOccludedColorB < Attribute( "OccludedColorB" ); Default4( 1.0, 1.0, 1.0, 0.25 ); >;

//----------------------------------------------------------------------------------------------
// Sample the depth buffer at the specified location. Returned value is a [ 0, 1 ] value in
// projection space (1/z).
//----------------------------------------------------------------------------------------------
float ProjectedDepthSample( float2 vScreenPos, int nMultiSampleIndex )
{					
	#if ( D_MSAA_MODE )
		float flDepthSample = Tex2DMS( g_tDepth, vScreenPos, nMultiSampleIndex ).x;
	#else
		float flDepthSample = g_tDepth.Load( int3( vScreenPos, 0 ) ).x;
	#endif

	// Our depth buffer may not actually go from 0 to 1, remap the so that the min z value
	// maps to 0 and the max z value maps to 1.
	return RemapValClamped( flDepthSample, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0 );
}

//----------------------------------------------------------------------------------------------
// Sample the depth buffer at the specified locaiton and return a [ 0, 1 ] value in a linear
// depth space, where 0 is closest and 1 is farthest.
//----------------------------------------------------------------------------------------------
float LinearDepthSample( float2 vScreenPos, int nMultiSampleIndex )
{
	float flDepth = ProjectedDepthSample( vScreenPos, nMultiSampleIndex );
	float flLinearDepth = RemapValClamped( flDepth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0 );
	return saturate( flLinearDepth );
}

//----------------------------------------------------------------------------------------------
// Compute the different in depth between the provided depth sample and the pixel at the 
// specified offset.
//----------------------------------------------------------------------------------------------
float ComputeDepthDelta( float flCenterLinearDepth, float2 vScreenPos, float2 vOffset, int nMultiSampleIndex )
{
	float flLinearDepth = LinearDepthSample( vScreenPos + vOffset, nMultiSampleIndex );
	float flDepthDelta = flLinearDepth - flCenterLinearDepth;
	return flDepthDelta;
}

//----------------------------------------------------------------------------------------------
// Perform a single sample for the depth outline. Given a center depth value and corresponing
// position, sample the depth at the specified offset and its opposite and determine if the
// outline should be displayed for the center sample becase one or both of the pixels are at
// a further depth than the center pixel.
//----------------------------------------------------------------------------------------------
float DepthOutlineSample( float flCenterDepth, float2 vScreenPos, float2 vOffset, int nMultiSampleIndex )
{
	// Sample at the specified offset and its opposite
	float flDeltaA = ComputeDepthDelta( flCenterDepth, vScreenPos,  vOffset, nMultiSampleIndex );
	float flDeltaB = ComputeDepthDelta( flCenterDepth, vScreenPos, -vOffset, nMultiSampleIndex );
			
	// Edge detection: First, sum the two samples. If the surface is sloped but actually flat 
	// the will cancel each other out since they are from opposite directions so one will be
	// positive and the other negetive. Scale the value by center depth value, this gives us 
	// larger values closer. Then subtract off a value which isolates deltas that are large 
	// enough to be considered an edge, the multiply to get near the 0 to 1 range. Finally 
	// subract of a constant to remove faint lines that show up along creases.
	float flDepthRange = ( g_flFarPlane - g_flNearPlane );
	float flMaskSub = ( flDepthRange / 50000 ) * flCenterDepth;
	float flMaskMult = flDepthRange * flCenterDepth;

	return saturate( ( ( flDeltaA + flDeltaB ) / flCenterDepth - flMaskSub ) * flMaskMult - 0.1 );
}

//----------------------------------------------------------------------------------------------
// Compute a [ 0, 1 ] value indicating if the specified locaiton is on the depth outline. This
// is done with four samples that each read two oppoiste locations (so all 8 neighbors are used).
//----------------------------------------------------------------------------------------------
float DepthOutlineSum( float2 vScreenPos, int nMultiSampleIndex )
{
	float flCenterDepth = LinearDepthSample( vScreenPos, nMultiSampleIndex );			
	float flSum = 0;
	flSum += DepthOutlineSample( flCenterDepth, vScreenPos, float2(  1,  0 ), nMultiSampleIndex );
	flSum += DepthOutlineSample( flCenterDepth, vScreenPos, float2(  0,  1 ), nMultiSampleIndex );
	flSum += DepthOutlineSample( flCenterDepth, vScreenPos, float2(  1,  1 ), nMultiSampleIndex );
	flSum += DepthOutlineSample( flCenterDepth, vScreenPos, float2( -1,  1 ), nMultiSampleIndex );

	return saturate( flSum * 0.25 );
}

//----------------------------------------------------------------------------------------------
// Depth outline: The outline of the selection mask merges for all selected objects, so 
// when one selected object is in front of another it doesn't show anything. To solve this
// we also add a depth based outline. This is tricky however, because we want to make sure
// the depth outline doesn't show up around objects that are infront of the selection. 
// To achive this we only draw the depth outline where the depth of neighboring pixels is 
// greater than the current one, and the selection mask is set for the current pixel.
//----------------------------------------------------------------------------------------------
float2 DepthOutline( float2 vScreenPos, float4 vSelectionMask, float4 vInnerMask )
{
	float2 vDepthOutline = float2( 0.0, 0.0 );

	#if ( D_DEPTH_OUTLINE )
	{
		float flDepthOutline = 0.0;
		#if ( D_MSAA_MODE == 1 )
		{
			flDepthOutline += DepthOutlineSum( vScreenPos, 0 );
			flDepthOutline += DepthOutlineSum( vScreenPos, 1 );
			flDepthOutline = flDepthOutline * 0.5;
			vDepthOutline = flDepthOutline * vInnerMask.ga;

		}
		#elif ( D_MSAA_MODE == 2 )
		{
			flDepthOutline += DepthOutlineSum( vScreenPos, 0 );
			flDepthOutline += DepthOutlineSum( vScreenPos, 1 );
			flDepthOutline += DepthOutlineSum( vScreenPos, 2 );
			flDepthOutline += DepthOutlineSum( vScreenPos, 3 );
			flDepthOutline = flDepthOutline * 0.25;
			vDepthOutline = flDepthOutline * vInnerMask.ga;
		}
		#else
		{
			flDepthOutline = DepthOutlineSum( vScreenPos, 0 );								
			vDepthOutline = flDepthOutline * vSelectionMask.ga;
		}
		#endif
	}
	#endif

	return vDepthOutline;
}
	

//----------------------------------------------------------------------------------------------
// Sammple the selection mask texture at the specified position and offset. This is 
// just a helper to handle the different texture sampling requrirements for sm30.
//----------------------------------------------------------------------------------------------
float4 SampleSelectionMask( float2 vScreenPos, float2 vOffset )
{
	return g_tInput.Load( int3( vScreenPos + vOffset, 0 ) );
}

//----------------------------------------------------------------------------------------------
// Compute the sum of the 8 neighboring pixels around the pixel at the specified screen position
//----------------------------------------------------------------------------------------------
float4 SumNeighbors( float2 vScreenPos )
{
	float4 vSum = float4( 0.0, 0.0, 0.0, 0.0 );

	vSum += SampleSelectionMask( vScreenPos, float2(  0.0,  1.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2(  0.0, -1.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2(  1.0,  0.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2( -1.0,  0.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2( -1.0,  1.0 ) );		
	vSum += SampleSelectionMask( vScreenPos, float2( -1.0, -1.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2(  1.0,  1.0 ) );
	vSum += SampleSelectionMask( vScreenPos, float2(  1.0, -1.0 ) );
																		 
	return vSum;
}

//----------------------------------------------------------------------------------------------
// Main
//----------------------------------------------------------------------------------------------
struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float2 vScreenPos = i.vPositionSs.xy;
		
	// Sample the pixel in the selection mask texture corresponding to the output pixel
	float4 vSelectionMask = SampleSelectionMask( vScreenPos, float2( 0.0, 0.0 ) );
	
	// Sample the 8 surrounding pixels
	float4 vSelectionMaskSum = SumNeighbors( vScreenPos );
		
	// Construct the inner mask which is smaller than the original selection mask. By multiplying 
	// by the selection mask we ensure there are no values outside the selection mask. Then 
	// ( sum * 0.25 - 1.0 ) maps the sum value [ 4, 8 ] to the [ 0, 1 ] range. So we only get
	// a value if 4 of the eight sourrounding samples are set and only get 1.0 when all 8 are set.
	float4 vInnerMask = saturate( vSelectionMask * ( vSelectionMaskSum * 0.25 - 1.0 ) );

	// Construct the outer mask which is larger than the original selection mask. The sum 
	// is multiplied by .25 and clamped to [ 0, 1 ]. This means the value will be 1.0 if 4
	// or more of the 8 samples are set.
	float4 vOuterMask = saturate( vSelectionMaskSum * 0.25 );

	// The region between the outer and inner mask gives us the outline.
	float2 vSelectionOutline = vOuterMask.rb * ( 1.0f - vInnerMask.rb );
	
	// Compute the depth outline r ang b have the the outline masked
	// by the selection, while b has the unmasked depth outline value.
	float2 vDepthOutline = DepthOutline( vScreenPos, vSelectionMask, vInnerMask );
		
	// Combine the selection and depth outlines
	float2 vOutline = vSelectionOutline + vDepthOutline;
	
	// Compute the final opacity of the combined outlines
	float flFinalOpacity = max( vOutline.r, vOutline.g );
			
	// Kill the pixel if the opacity is 0
	clip( flFinalOpacity - 0.001 );

	// Compute the color based on the depth tested / not depth tested parts. The green and alpha
	// chanels contain the depth tested part of the stencil, so where the value is 1 use the 
	// standarad color / alpha. Where the value is 0 use the occludded color / alpha
	float4 vColorA = lerp( g_vOccludedColorA, g_vOutlineColorA, vOuterMask.g );
	float4 vColorB = lerp( g_vOccludedColorB, g_vOutlineColorB, vOuterMask.a );

	// Compute the final color between the two sets by lerping between the two colors based on
	// the opacity of set A. This means that where the two sets overlap, the color for set A 
	// will be used, making it look like set A is infront of set B.
	float4 vFinalColor = lerp( vColorA, vColorB, vOutline.r > vOutline.g ? 0.0 : 1.0 );

	o.vColor = vFinalColor;
	o.vColor.a *= flFinalOpacity;

	// Debugging - uncomment a componnent, disable clip above and set alpha to 1 below.
	//o.vColor = vSelectionMask.g;
	//o.vColor = vOuterMask.g;
	//o.vColor = vInnerMask.g;
	//o.vColor = vSelectionOutline.r;
	//o.vColor = LinearDepthSample( vScreenPos, 0 );
	//o.vColor = vDepthOutline.r;
	//o.vColor = vOutline.r;
	//o.vColor = flFinalOpacity;
	//o.vColor.a = 1;

	return o;
}

#endif // PS

