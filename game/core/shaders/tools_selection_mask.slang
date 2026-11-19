// tools_selection_mask: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). Hammer's
// selection on Borealis.Gpu (Editor.ToolSceneViews.ToolSelection), in the channels native's stencil copies write
// (CMapView::PreRenderSetup): selected objects in red where they cover the view and green where they're also in front of the
// scene, prefabs and instances in blue and alpha.
//   D_COMPOSITE 0: a selected object drawn into the mask, its channels the max of what was there (SelectionAny where it
//                  covers the pixel, SelectionVisible as well where the scene's depth doesn't hide it), with no depth test.
//   D_COMPOSITE 1: over the finished frame, from the mask: the outline tools_selection_outline draws (without its depth
//                  outline), round the part in front, then the tints of native's HammerSelectionOverlayLayer and
//                  HammerPrefabOverlayLayer, which native masks by the depth-tested stencil: SelectionTint where green is
//                  set, PrefabTint where only alpha is. Both blends in one pass, premultiplied.

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );

#include "common/features.hlsl"

#define USE_CUSTOM_SHADING
#include "common/shared.hlsl"

DynamicCombo( D_COMPOSITE, 0..1, Sys( ALL ) );

#if ( D_COMPOSITE )

struct VertexInput
{
	float3 vPositionSs : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
	float4 vPosition : SV_Position;
};

#else

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

#endif

#if ( PROGRAM == VFX_PROGRAM_VS )

#if ( D_COMPOSITE )
// A screen quad in projection space (Graphics.Blit's)
[shader( "vertex" )] PixelInput MainVs( VertexInput i )
{
	PixelInput o;
	o.vPosition = float4( i.vPositionSs, 1.0 );
	return o;
}
#else
#include "common/vertex.hlsl"

[shader( "vertex" )] PixelInput MainVs( VertexInput i )
{
	PixelInput o = ProcessVertex( i );
	return FinalizeVertex( o );
}
#endif

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
#include "common.fxc"

RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );
RenderState( BlendEnable, true );

#if ( D_COMPOSITE )

RenderState( SrcBlend, ONE );
RenderState( DstBlend, INV_SRC_ALPHA );

Texture2D g_tSelectionMask < Attribute( "SelectionMask" ); SrgbRead( false ); >;
float g_flSelectionOutline < Attribute( "SelectionOutline" ); Default( 0.0 ); >;
float4 g_vOutlineColorA < Attribute( "OutlineColorA" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
float4 g_vOccludedColorA < Attribute( "OccludedColorA" ); Default4( 1.0, 1.0, 1.0, 0.25 ); >;
float4 g_vOutlineColorB < Attribute( "OutlineColorB" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
float4 g_vOccludedColorB < Attribute( "OccludedColorB" ); Default4( 1.0, 1.0, 1.0, 0.25 ); >;
float4 g_vSelectionTint < Attribute( "SelectionTint" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;
float4 g_vPrefabTint < Attribute( "PrefabTint" ); Default4( 0.0, 0.0, 0.0, 0.0 ); >;

// The mask at a pixel, clamped to its edges (nothing reads past them)
float4 MaskAt( int2 vPixel, int2 vMax )
{
	return g_tSelectionMask.Load( int3( clamp( vPixel, int2( 0, 0 ), vMax ), 0 ) );
}

[shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
{
	uint nWidth, nHeight;
	g_tSelectionMask.GetDimensions( nWidth, nHeight );
	int2 vMax = int2( nWidth, nHeight ) - 1;
	int2 vPixel = int2( i.vPosition.xy );

	float4 vMask = MaskAt( vPixel, vMax );
	float4 vSum = MaskAt( vPixel + int2( 0, 1 ), vMax ) + MaskAt( vPixel + int2( 0, -1 ), vMax )
		+ MaskAt( vPixel + int2( 1, 0 ), vMax ) + MaskAt( vPixel + int2( -1, 0 ), vMax )
		+ MaskAt( vPixel + int2( -1, 1 ), vMax ) + MaskAt( vPixel + int2( -1, -1 ), vMax )
		+ MaskAt( vPixel + int2( 1, 1 ), vMax ) + MaskAt( vPixel + int2( 1, -1 ), vMax );

	// tools_selection_outline: the band between a mask grown where 4 of the 8 neighbours are set and one shrunk to where
	// they all are, coloured by whether the selection is in front there; set A (the selection) over set B (prefabs). Its
	// band is round the selection's part in front of the scene (green, alpha), as native's pictures show it: a selected
	// floor half under a platform is outlined where the platform hides it, not at its own edges off screen
	float4 vInner = saturate( vMask * ( vSum * 0.25 - 1.0 ) );
	float4 vOuter = saturate( vSum * 0.25 );
	float2 vOutline = vOuter.ga * ( 1.0 - vInner.ga );
	float4 vColorA = lerp( g_vOccludedColorA, g_vOutlineColorA, vOuter.g );
	float4 vColorB = lerp( g_vOccludedColorB, g_vOutlineColorB, vOuter.a );
	float4 vOutlineColor = vOutline.r > vOutline.g ? vColorA : vColorB;
	float flOutline = vOutlineColor.a * max( vOutline.r, vOutline.g ) * g_flSelectionOutline;

	// Then the tint over it: the selection's stencil value is written after the prefabs', so it wins where both are
	float4 vTint = vMask.g > 0.5 ? g_vSelectionTint : vMask.a > 0.5 ? g_vPrefabTint : float4( 0, 0, 0, 0 );

	float4 vColor;
	vColor.rgb = vOutlineColor.rgb * flOutline * ( 1.0 - vTint.a ) + vTint.rgb * vTint.a;
	vColor.a = 1.0 - ( 1.0 - flOutline ) * ( 1.0 - vTint.a );
	clip( vColor.a - 0.001 );
	return vColor;
}

#else

RenderState( SrcBlend, ONE );
RenderState( DstBlend, ONE );
RenderState( BlendOp, MAX );
RenderState( SrcBlendAlpha, ONE );
RenderState( DstBlendAlpha, ONE );
RenderState( BlendOpAlpha, MAX );

#include "common/classes/Depth.hlsl"

float4 g_vSelectionAny < Attribute( "SelectionAny" ); Default4( 1.0, 0.0, 0.0, 0.0 ); >;
float4 g_vSelectionVisible < Attribute( "SelectionVisible" ); Default4( 0.0, 1.0, 0.0, 0.0 ); >;

[shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
{
	// In front of the scene's depth, or the scene's surface itself: its own depth nudged nearer by its slope, as
	// ObjectHighlight's inside pass tells the two apart
	float flObjectDepth = i.vPositionSs.z + fwidth( i.vPositionSs.z );
	float flSceneDepth = Depth::GetNormalized( i.vPositionSs.xy - g_vViewportOffset );
	float flVisible = flSceneDepth - flObjectDepth < 0.0 ? 1.0 : 0.0;
	return g_vSelectionAny + g_vSelectionVisible * flVisible;
}

#endif

#endif // PS
