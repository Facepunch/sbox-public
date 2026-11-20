FEATURES
{
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
    #include "common/pixel.hlsl"

	RenderState( BlendEnable, false );
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( DepthWriteEnable, true );

	// visuals setup - configured from material editor for now but i might as well make it accessible from icon editor?
	float g_flBendThreshold < Default( 4.0 ); Range( 0, 64 ); UiGroup( "Edges, 10/10" ); >;
	float g_flBendStrength 	< Default( 1.0 ); Range( 0, 1 ); UiGroup( "Edges, 10/20" ); >;

	float g_flRimThreshold 	< Default( 0.75 ); Range( 0, 1 ); UiGroup( "Edges, 10/30" ); >;
	float g_flRimStrength 	< Default( 1.0 ); Range( 0, 1 ); UiGroup( "Edges, 10/40" ); >;

	float g_flHatchSpacing 	< Default( 8.0 ); Range( 2, 64 ); UiGroup( "Hatching, 20/10" ); >;
	float g_flHatchWidth 	< Default( 2.0 ); Range( 0, 32 ); UiGroup( "Hatching, 20/20" ); >;
	float g_flHatchAngle 	< Default( 45.0 ); Range( 0, 180 ); UiGroup( "Hatching, 20/30" ); >;
	float g_flHatchOpacity 	< Default( 1.0 ); Range( 0, 1 ); UiGroup( "Hatching, 20/40" ); >;
	float g_flHatchSoftness < Default( 1.0 ); Range( 0, 4 ); UiGroup( "Hatching, 20/50" ); >;

	float Hatch( float2 vPixel )
	{
		float flAngle = radians( g_flHatchAngle );
		float flAcross = dot( vPixel, float2( cos( flAngle ), sin( flAngle ) ) );
		float flDist = abs( frac( flAcross / g_flHatchSpacing ) - 0.5f ) * g_flHatchSpacing;
		float flHalfWidth = g_flHatchWidth * 0.5f;
		float flHalfSoft = max( g_flHatchSoftness * 0.5f, 0.0001f );
		return 1.0f - smoothstep( flHalfWidth - flHalfSoft, flHalfWidth + flHalfSoft, flDist );
	}

	float SharpStep( float flThreshold, float flValue )
	{
		float flAA = max( fwidth( flValue ), 0.0001 );
		return smoothstep( flThreshold - flAA, flThreshold + flAA, flValue );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 vPositionWs = g_vCameraPositionWs + i.vPositionWithOffsetWs;
		float3 vNormalWs = normalize( i.vNormalWs );
		float3 vViewWs = CalculatePositionToCameraDirWs( vPositionWs );

		// curvature
		float flNormalDelta = max( length( ddx( vNormalWs ) ), length( ddy( vNormalWs ) ) );
		float flPosDelta = max( length( ddx( vPositionWs ) ), length( ddy( vPositionWs ) ) );
		float flCurvature = flNormalDelta / max( flPosDelta, 0.00001 );
		float flBend = SharpStep( g_flBendThreshold, flCurvature ) * g_flBendStrength;

		// rim
		float flRim = 1.0f - saturate( dot( vNormalWs, float3( 0, 0.2, 1 ) ) );
		float flSilhouette = SharpStep( g_flRimThreshold, flRim ) * g_flRimStrength;

		float flEdge = max( flBend, flSilhouette );

		float3 base = 0.4;
		float3 bright = base * 4;
		float flHatchMask = 1 - SharpStep( g_flRimThreshold * 1.25f, flRim );
		float flHatch = Hatch( i.vPositionSs.xy ) * flHatchMask * g_flHatchOpacity;

		// we only draw the mask and hatch, everything else is 0 alpha
		float flAlpha = max( 1 - flEdge, flHatch );
		float3 final = bright;

		return float4( final, flAlpha );
	}
}
