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

#if D_PAINTER_SCREENPIXELS
float4 g_vPainterScreenViewport < Attribute( "PainterScreenViewport" ); Default4( 0, 0, 0, 0 ); >;

float4 MapPainterScreenPixels( float4 position, float4 viewport )
{
	#if !( D_WORLDPANEL )
	float2 logicalSize = g_vPainterScreenViewport.zw;
	if ( all( logicalSize > EPSILON ) && all( viewport.zw > EPSILON ) )
		position.xy = viewport.xy * position.w + ( position.xy - g_vPainterScreenViewport.xy * position.w ) * ( viewport.zw / logicalSize );
	#endif
	return position;
}
#endif

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
		#if D_PAINTER_SCREENPIXELS
			vMatrix = MapPainterScreenPixels( vMatrix, vViewport );
		#endif
		vPositionSs.xy = vMatrix.xy / vMatrix.w;
		o.vPositionPs.xy = 2.0 * ( vMatrix.xy - vViewport.xy * vMatrix.w ) / vViewport.zw - vMatrix.w;
		o.vPositionPs.y *= -1.0;
		o.vPositionPs.z = vMatrix.w;
		o.vPositionPs.w = vMatrix.w * ( 1.0 + EPSILON );
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

	o.vColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
	o.vColor.a = i.vColor.a;
	o.vTexCoord = float4( i.vTexCoord.xy, 0, 0 );

	return o;
}
