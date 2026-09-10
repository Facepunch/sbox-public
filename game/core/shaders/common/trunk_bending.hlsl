#ifndef TRUNK_BENDING_HLSL
#define TRUNK_BENDING_HLSL

// Trunk/branch bending for vegetation
// Based on GPU Gems 3 Chapter 16: "Vegetation Procedural Animation and Shading in Crysis"
// https://developer.nvidia.com/gpugems/GPUGems3/gpugems3_ch16.html
void ApplyTrunkBending( inout float3 vPositionOs, float flStrength, float flSpeed, float3 vWind, float flTime )
{
	// Height bend factor
	float flHeight = vPositionOs.z;
	float flBendFactor = flHeight * flStrength * 0.0001;
	flBendFactor *= flBendFactor; // Quadratic falloff

	float3 vWindDir = length( vWind ) > 0.01 ? normalize( vWind ) : float3( 1, 0, 0 );
	float flWindStrength = length( vWind );

	float flSway = sin( flTime * flSpeed ) + sin( flTime * flSpeed * 0.7 ) * 0.5;

	vPositionOs.xy += vWindDir.xy * flBendFactor * ( flWindStrength + flSway );
}

#endif
