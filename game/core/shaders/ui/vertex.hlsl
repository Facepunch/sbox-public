// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc"
#include "common.fxc"
#include "vr_common.fxc"
#include "vr_lighting.fxc"
#include "math_general.fxc"
#include "instancing.fxc"

#define EPSILON 0.000001

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
float4 g_vViewport < Source( Viewport ); >;
float4x4 g_matTransform < Attribute( "TransformMat" ); >; 
float4x4 LayerMat < Attribute( "LayerMat" ); >; 
float4x4 g_matWorldPanel < Attribute( "WorldMat" ); >; 

BoolAttribute( ui, true );
BoolAttribute( ScreenSpaceVertices, true );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
PS_INPUT MainVs( VS_INPUT i )
{
	PS_INPUT o;

	float4 vViewport = g_vViewport;
	float3 vPositionSs = i.vPositionSs.xyz;

	#if !( D_WORLDPANEL )
	{
		float4 vMatrix = mul( LayerMat, mul( g_matTransform, float4( vPositionSs.xy, 0, 1 ) ));
		vPositionSs.xy = vMatrix.xy / vMatrix.w;
		o.vPositionPs.xy = 2.0 * ( vPositionSs.xy - vViewport.xy ) / ( vViewport.zw ) - float2( 1.0, 1.0 );
		o.vPositionPs.y *= -1.0;
		o.vPositionPs.z = 1.0;
		o.vPositionPs.w = 1.0 + EPSILON;
		o.vTexCoord.zw = vPositionSs.xy / vViewport.zw;		
	}
	#else
	{
		float4 vMatrix = mul( LayerMat, mul( g_matTransform, float4( vPositionSs.xyz, 1 ) ));
		vPositionSs.xyz = vMatrix.xyz / vMatrix.w;

		o.vPositionPs = float4( vPositionSs.xyz, 1 );
		o.vPositionPs.y *= -1.0;

		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

		matObjectToWorld = mul( matObjectToWorld, g_matWorldPanel );

		o.vPositionWs = mul( matObjectToWorld, float4( o.vPositionPs.xyz, 1.0 ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );
	}
	#endif
	
	o.vPositionSs = o.vPositionPs;
	o.vPositionPanelSpace = mul( g_matTransform, float4( i.vPositionSs.xy, 0, 1 ) );
	o.vTexCoord.zw = vPositionSs.xy / vViewport.zw;

	o.vColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
	o.vColor.a = i.vColor.a;
	o.vTexCoord.xy = i.vTexCoord.xy;

	return o;
}