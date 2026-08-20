// UI clipping for shaders that draw one quad at a time --------------------------------------------------------------------------------------------------
//
// A stack of up to four rounded rects the pixel must be inside all of, set by PanelRenderer.SetScissorAttributes.
// Each rect is left, top, right, bottom in the clipping panel's layout space and its matrix takes screen space
// there. Coverage is antialiased, so a clipped image or text gets the same edge as the panel clipping it; the
// batched box shader does the same from a buffer.

#include "ui/rounded_rect.hlsl"

int HasScissoring < Default( 0 ); Attribute( "HasScissor" ); >;
int ScissorCount < Default( 0 ); Attribute( "ScissorCount" ); >;

float4 ScissorRect0 < Attribute( "ScissorRect0" ); >;
float4 ScissorRect1 < Attribute( "ScissorRect1" ); >;
float4 ScissorRect2 < Attribute( "ScissorRect2" ); >;
float4 ScissorRect3 < Attribute( "ScissorRect3" ); >;
float4 ScissorRadiiH0 < Attribute( "ScissorRadiiH0" ); >;
float4 ScissorRadiiH1 < Attribute( "ScissorRadiiH1" ); >;
float4 ScissorRadiiH2 < Attribute( "ScissorRadiiH2" ); >;
float4 ScissorRadiiH3 < Attribute( "ScissorRadiiH3" ); >;
float4 ScissorRadiiV0 < Attribute( "ScissorRadiiV0" ); >;
float4 ScissorRadiiV1 < Attribute( "ScissorRadiiV1" ); >;
float4 ScissorRadiiV2 < Attribute( "ScissorRadiiV2" ); >;
float4 ScissorRadiiV3 < Attribute( "ScissorRadiiV3" ); >;
float4x4 ScissorMat0 < Attribute( "ScissorMat0" ); >;
float4x4 ScissorMat1 < Attribute( "ScissorMat1" ); >;
float4x4 ScissorMat2 < Attribute( "ScissorMat2" ); >;
float4x4 ScissorMat3 < Attribute( "ScissorMat3" ); >;

float4x4 TransformMat < Attribute( "TransformMat" ); >;

float ClipShapeCoverage( float2 vScreenPos, float4 vRect, float4 vRadiiH, float4 vRadiiV, float4x4 matToLayout )
{
	float2 p = mul( matToLayout, float4( vScreenPos, 0, 1 ) ).xy;
	float2 vCentre = ( vRect.xy + vRect.zw ) * 0.5;
	float2 vHalf = ( vRect.zw - vRect.xy ) * 0.5;
	return SdfCoverage( RoundedRectSdf( p - vCentre, vHalf, vRadiiH, vRadiiV ) );
}

float SoftwareScissorCoverage( PS_INPUT i )
{
#if D_WORLDPANEL
	// World panels have no screen space; rebuild the position from the box, then transform like the screen path does
	float2 vLocal = BoxSize * i.vTexCoord.xy + BoxPosition;
	float2 vPixelPos = mul( TransformMat, float4( vLocal, 0, 1 ) ).xy;
#else
	float2 vPixelPos = i.vPositionPanelSpace.xy;
#endif

	float flCoverage = 1.0;
	if ( ScissorCount > 0 ) flCoverage *= ClipShapeCoverage( vPixelPos, ScissorRect0, ScissorRadiiH0, ScissorRadiiV0, ScissorMat0 );
	if ( ScissorCount > 1 ) flCoverage *= ClipShapeCoverage( vPixelPos, ScissorRect1, ScissorRadiiH1, ScissorRadiiV1, ScissorMat1 );
	if ( ScissorCount > 2 ) flCoverage *= ClipShapeCoverage( vPixelPos, ScissorRect2, ScissorRadiiH2, ScissorRadiiV2, ScissorMat2 );
	if ( ScissorCount > 3 ) flCoverage *= ClipShapeCoverage( vPixelPos, ScissorRect3, ScissorRadiiH3, ScissorRadiiV3, ScissorMat3 );
	return flCoverage;
}
