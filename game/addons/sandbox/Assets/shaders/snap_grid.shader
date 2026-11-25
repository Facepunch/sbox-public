HEADER
{
	Description = "Snap Grid Overlay";
	DevShader = true;
}

MODES
{
	Forward();
}

FEATURES
{
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionWs : POSITION < Semantic( PosXyz ); >;
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

struct PixelInput
{
	float3 vPositionWs : TEXCOORD0;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionWs = i.vPositionWs;
		o.vPositionPs = Position3WsToPs( i.vPositionWs );

		// Reverse-Z: increasing depth brings the overlay in front of its mesh.
		float flProjDepth = saturate( o.vPositionPs.z / o.vPositionPs.w );
		float flBias = flProjDepth * 0.0001f;
		o.vPositionPs.z += flBias * o.vPositionPs.w;

		return o;
	}
}

PS
{
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );
	// Do not draw the reverse side of collision surfaces through thin props.
	RenderState( CullMode, BACK );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float3 GridOrigin < Attribute( "GridOrigin" ); Default3( 0, 0, 0 ); >;
	float3 BoundsOrigin < Attribute( "BoundsOrigin" ); Default3( 0, 0, 0 ); >;
	float3 GridRight < Attribute( "GridRight" ); Default3( 1, 0, 0 ); >;
	float3 GridUp < Attribute( "GridUp" ); Default3( 0, 0, 1 ); >;

	float3 SnapPoint < Attribute( "SnapPoint" ); Default3( 0, 0, 0 ); >;
	float3 SnapNormal < Attribute( "SnapNormal" ); Default3( 0, 0, 1 ); >;
	float3 AimPoint < Attribute( "AimPoint" ); Default3( 0, 0, 0 ); >;
	float MaskRadius < Attribute( "MaskRadius" ); Default( 48.0 ); >;

	float2 CellSpacing < Attribute( "CellSpacing" ); Default2( 16, 16 ); >;
	float CellSize < Attribute( "CellSize" ); Default( 16.0 ); >;

	float RoundSurface < Attribute( "RoundSurface" ); Default( 0 ); >;
	float RoundRadius < Attribute( "RoundRadius" ); Default( 1 ); >;
	float AngleStep < Attribute( "AngleStep" ); Default( 0.392699 ); >;
	float PlanarSurface < Attribute( "PlanarSurface" ); Default( 0 ); >;
	float MeshSurface < Attribute( "MeshSurface" ); Default( 0 ); >;

	float SnapCornerX < Attribute( "SnapCornerX" ); Default( 0 ); >;
	float SnapCornerY < Attribute( "SnapCornerY" ); Default( 0 ); >;

	float4 GridColor < Attribute( "GridColor" ); Default4( 1.0, 1.0, 1.0, 0.5 ); >;
	float4 CornerColor < Attribute( "CornerColor" ); Default4( 0.2, 0.6, 1.0, 1.0 ); >;
	float4 CenterLineColor < Attribute( "CenterLineColor" ); Default4( 1.0, 1.0, 1.0, 0.85 ); >;

	float2 HalfExtents < Attribute( "HalfExtents" ); Default2( 48.0, 48.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 p = i.vPositionWs;
		// Limit planar overlays to the selected level, not every parallel shelf.
		if ( PlanarSurface > 0.5 )
		{
			float3 planeNormal = normalize( cross( GridRight, GridUp ) );
			clip( CellSize * 0.5 - abs( dot( p - BoundsOrigin, planeNormal ) ) );
		}

		float3 offset = p - GridOrigin;
		float2 facePos = float2( dot( offset, GridRight ), dot( offset, GridUp ) );

		float radius = length( facePos );
		float angle = atan2( facePos.y, facePos.x );
		float2 cellUv = facePos / CellSpacing;
		if ( RoundSurface > 0.5 ) cellUv = float2( radius / CellSize, angle / AngleStep );

		float2 deriv = max( abs( ddx( cellUv ) ), abs( ddy( cellUv ) ) );
		float2 wrapped = abs( frac( cellUv ) - 0.5 );
		float2 lw = 1.0 * deriv;
		float2 cov = saturate( ( wrapped - ( 0.5 - lw ) ) / max( lw, 0.0001 ) );
		float grid = max( cov.x, cov.y );
		if ( RoundSurface > 0.5 )
		{
			float pixel = max( fwidth( radius ), 0.0001 );
			float spokeDistance = abs( sin( angle - round( angle / AngleStep ) * AngleStep ) ) * radius;
			float spokes = 1.0 - saturate( spokeDistance / pixel );
			float outerRing = 1.0 - saturate( abs( radius - RoundRadius ) / pixel );
			grid = max( max( cov.x, spokes ), outerRing ) * (1.0 - smoothstep( RoundRadius, RoundRadius + pixel, radius ));
		}

		// Rectangular fade: grid eases in from the bounds edge over half a cell.
		float3 boundsOffset = p - BoundsOrigin;
		float2 boundsPos = float2( dot( boundsOffset, GridRight ), dot( boundsOffset, GridUp ) );
		float2 edgeDist = HalfExtents - abs( boundsPos );
		float edgeFade = saturate( min( edgeDist.x, edgeDist.y ) / max( CellSize * 0.5, 0.001 ) );

		float dist = length( p - AimPoint );
		float aimFade = 1.0 - saturate( dist / ( MaskRadius * 0.3 ) );
		float gridFade = aimFade;
		gridFade = lerp( gridFade, 1.0, max( PlanarSurface, RoundSurface ) );
		grid *= gridFade;

		float mask = lerp( edgeFade, 1.0, MeshSurface );

		float2 cornerFace = float2( SnapCornerX, SnapCornerY ) * CellSpacing;
		float2 localUv = facePos - cornerFace;
		if ( RoundSurface > 0.5 )
		{
			float snapAngle = atan2( cornerFace.y, cornerFace.x );
			float deltaAngle = atan2( sin( angle - snapAngle ), cos( angle - snapAngle ) );
			localUv = float2( deltaAngle * max( radius, CellSize ), radius - length( cornerFace ) );
		}

		float2 facePosDdx = ddx( facePos );
		float2 facePosDdy = ddy( facePos );
		float2 px = max( abs( facePosDdx ), abs( facePosDdy ) );

		// Thicken the cross bar that lies along a center axis.
		float onCenterX = 1.0 - saturate( abs( SnapCornerX ) );
		float onCenterY = 1.0 - saturate( abs( SnapCornerY ) );
		float barW = lerp( px.x * 1.5, px.x * 2.5, onCenterX );
		float barH = lerp( px.y * 1.5, px.y * 2.5, onCenterY );

		float barX = saturate( ( barH - abs( localUv.y ) ) / max( barH, 0.0001 ) );
		float barY = saturate( ( barW - abs( localUv.x ) ) / max( barW, 0.0001 ) );

		// Fade each line segment away from the cursor along the line direction.
		float2 aimOffset2 = float2( dot( AimPoint - GridOrigin, GridRight ), dot( AimPoint - GridOrigin, GridUp ) );
		float fadeRadius = max( MaskRadius * 0.075, 0.001 );
		float fadeVert  = 1.0 - saturate( abs( facePos.y - aimOffset2.y ) / fadeRadius );
		float fadeHoriz = 1.0 - saturate( abs( facePos.x - aimOffset2.x ) / fadeRadius );

		float cross = saturate( barY * fadeVert + barX * fadeHoriz );
		if ( RoundSurface > 0.5 ) cross = saturate( barY + barX ) * (1.0 - saturate( length( facePos - cornerFace ) / fadeRadius ));

		float4 col = GridColor * grid;

		// --- Center axis lines & bounds edge lines (thicker) ---
		// Re-use facePosDdx/ddy (already computed above in world units).
		float2 thickLw = max( abs( facePosDdx ), abs( facePosDdy ) ) * 2.5;

		// Center: lines at facePos == (0, 0).
		float centerX = saturate( ( thickLw.x - abs( facePos.x ) ) / max( thickLw.x, 0.0001 ) );
		float centerY = saturate( ( thickLw.y - abs( facePos.y ) ) / max( thickLw.y, 0.0001 ) );
		float centerLines = max( centerX, centerY ) * edgeFade * gridFade;

		// Bounds edges: lines at facePos == ±HalfExtents.
		float2 boundsEdgeDist = abs( HalfExtents - abs( boundsPos ) );
		float boundsX = saturate( ( thickLw.x - boundsEdgeDist.x ) / max( thickLw.x, 0.0001 ) );
		float boundsY = saturate( ( thickLw.y - boundsEdgeDist.y ) / max( thickLw.y, 0.0001 ) );
		float boundsLines = max( boundsX, boundsY ) * (1.0 - RoundSurface) * aimFade;

		float specialLines = saturate( centerLines + boundsLines ) * (1.0 - MeshSurface) + boundsLines * MeshSurface;
		col = lerp( col, CenterLineColor, specialLines * CenterLineColor.a );

		// Blue corner cross draws on top.
		col = lerp( col, CornerColor, saturate( cross * 2.0 ) );

		// Keep boundary snap markers visible instead of fading them out at the edge.
		col.a *= max( mask, max( boundsLines, cross ) );

		if ( col.a < 0.002 ) discard;

		return col;
	}
}
