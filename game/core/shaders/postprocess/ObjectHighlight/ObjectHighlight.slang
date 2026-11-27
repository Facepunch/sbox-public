// ObjectHighlight: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The outside pass draws a quad round each edge of each triangle, the outline's width and half a pixel past it every way,
// where the .shader's geometry stage draws them: the inside pass's stencil keeps the outline outside the object, and the
// pixel shader fades it over one pixel from the distance to the edge (OutlineCoverage), so it's smooth at any sample
// count. Here the vertex stage makes the quads, one strip of 16 vertices per triangle (ExpandPrimitives) joined by
// repeated vertices, with the triangle's corners as per-instance inputs (the second's semantics 4 up, the third's 8 up).

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Depth );

#include "sbox/common_features.slangh"
Feature( F_USE_PATTERN, 0..1, "Rendering" );

#define USE_CUSTOM_SHADING
#include "sbox/shared.slangh"

DynamicCombo( D_OUTLINE_PASS, 0..1, Sys( ALL ) );

#define OUTLINE_INSIDE 0
#define OUTLINE_OUTSIDE 1

struct VertexInput
{
	#include "sbox/common_vertex_input.slangh"
};

struct PixelInput
{
	#include "sbox/common_pixel_input.slangh"

	#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
		// In pixels: where this is, the edge's ends and the outline's width
		noperspective float2 vOutlinePixel : TEXCOORD8;
		nointerpolation float4 vOutlineEdge : TEXCOORD9;
		nointerpolation float flOutlineRadius : TEXCOORD10;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
#include "sbox/vertex.slangh"

#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )

// Three quads, one round each edge, as one strip: vertices 0-3, 6-9 and 12-15, joined by 4-5 and 10-11 repeating the last
// of one and the first of the next
ExpandPrimitives( TRIANGLES, 16, TRIANGLE_STRIP );

float _LineSize < UiType( Color ); UiGroup( "Color" ); Attribute( "LineWidth" ); Default( 0.2f ); >;

// The triangle's second and third corners: what the vertex stage reads of each
struct CornerInputs
{
	float3 vPositionOs1 : POSITION4;
	float2 vTexCoord1 : TEXCOORD4;
	float2 vTexCoord21 : TEXCOORD5;
	float4 vNormalOs1 : NORMAL4;
	#if ( VS_INPUT_HAS_TANGENT_BASIS )
		float4 vTangentUOs_flTangentVSign1 : TANGENT4;
	#endif

	float3 vPositionOs2 : POSITION8;
	float2 vTexCoord2 : TEXCOORD8;
	float2 vTexCoord22 : TEXCOORD9;
	float4 vNormalOs2 : NORMAL8;
	#if ( VS_INPUT_HAS_TANGENT_BASIS )
		float4 vTangentUOs_flTangentVSign2 : TANGENT8;
	#endif

	uint nBoneIndex1 : BLENDINDICES4;
	uint nBoneIndex2 : BLENDINDICES8;

	// Their places in the vertex cache: TEXCOORD15 is past the corners' semantic range, so a model's vertices drawn for this
	// stage hold them as TEXCOORD3 too (a skinned mesh has no lightmap uvs), read here four and eight up
	#if ( D_CS_VERTEX_ANIMATION )
		float nVertexCacheIndex1 : TEXCOORD7;
		float nVertexCacheIndex2 : TEXCOORD11;
	#endif
};

// The vertex stage's inputs for corner 0, 1 or 2 of the triangle
VertexInput CornerInput( VertexInput i, CornerInputs c, uint nCorner )
{
	VertexInput v = i;
	if ( nCorner == 1 )
	{
		v.vPositionOs = c.vPositionOs1;
		v.vTexCoord = c.vTexCoord1;
		v.vTexCoord2 = c.vTexCoord21;
		v.vNormalOs = c.vNormalOs1;
		#if ( VS_INPUT_HAS_TANGENT_BASIS )
			v.vTangentUOs_flTangentVSign = c.vTangentUOs_flTangentVSign1;
		#endif
		v.nBoneIndex = c.nBoneIndex1;
		#if ( D_CS_VERTEX_ANIMATION )
			v.nVertexCacheIndex = c.nVertexCacheIndex1;
		#endif
	}
	else if ( nCorner == 2 )
	{
		v.vPositionOs = c.vPositionOs2;
		v.vTexCoord = c.vTexCoord2;
		v.vTexCoord2 = c.vTexCoord22;
		v.vNormalOs = c.vNormalOs2;
		#if ( VS_INPUT_HAS_TANGENT_BASIS )
			v.vTangentUOs_flTangentVSign = c.vTangentUOs_flTangentVSign2;
		#endif
		v.nBoneIndex = c.nBoneIndex2;
		#if ( D_CS_VERTEX_ANIMATION )
			v.nVertexCacheIndex = c.nVertexCacheIndex2;
		#endif
	}

	return v;
}

// The outline's width in pixels: LineWidth / 64 of the normalized inverse viewport size in projection space, which is the
// same number of pixels across and down
float OutlineRadius()
{
	return ( _LineSize / 64.0f ) / length( g_vInvViewportSize );
}

// Corner 0-3 of the quad round edge a-b: past a, then past b, on one side then the other, half a pixel more than the
// outline's width every way. An end behind the camera leaves the quad empty
PixelInput OutlineCorner( PixelInput a, PixelInput b, uint nCorner, float flRadius )
{
	bool bEnd = nCorner >= 2;
	PixelInput o = bEnd ? b : a;

	float2 vHalfViewport = 0.5 / g_vInvViewportSize;
	float2 vA = a.vPositionPs.xy / a.vPositionPs.w * vHalfViewport;
	float2 vB = b.vPositionPs.xy / b.vPositionPs.w * vHalfViewport;
	float2 vAlong = vB - vA;
	float flLength = length( vAlong );
	vAlong = flLength > 1e-4 ? vAlong / flLength : float2( 1, 0 );
	float2 vAcross = float2( -vAlong.y, vAlong.x );
	float flHalf = flRadius + 0.5;

	float2 vPixel = ( bEnd ? vB + vAlong * flHalf : vA - vAlong * flHalf ) + vAcross * ( ( nCorner & 1 ) != 0 ? -flHalf : flHalf );
	if ( a.vPositionPs.w > 0.01 && b.vPositionPs.w > 0.01 ) o.vPositionPs.xy = vPixel / vHalfViewport * o.vPositionPs.w;

	o.vOutlinePixel = vPixel;
	o.vOutlineEdge = float4( vA, vB );
	o.flOutlineRadius = flRadius;
	return o;
}

[shader( "vertex" )] PixelInput MainVs( VertexInput i, CornerInputs c, uint nStripVertex : SV_VertexID )
{
	// Which edge (0 from corner 0 to 1, 1 from 1 to 2, 2 from 2 to 0) and which corner of its quad
	uint s = min( nStripVertex, 15u );
	uint nEdge = s < 5 ? 0 : ( s < 11 ? 1 : 2 );
	uint nQuadCorner = s < 4 ? s : s == 4 ? 3 : s <= 6 ? 0 : s <= 9 ? s - 6 : s == 10 ? 3 : s <= 12 ? 0 : s - 12;

	PixelInput a = FinalizeVertex( ProcessVertex( CornerInput( i, c, nEdge ) ) );
	PixelInput b = FinalizeVertex( ProcessVertex( CornerInput( i, c, ( nEdge + 1 ) % 3 ) ) );
	return OutlineCorner( a, b, nQuadCorner, OutlineRadius() );
}

#else

//
// Main
//
[shader( "vertex" )] PixelInput MainVs( VertexInput i )
{
	PixelInput o = ProcessVertex( i );
	return FinalizeVertex( o );
}

#endif // D_OUTLINE_PASS
#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
#include "sbox/view.slangh"
#include "sbox/classes/depth.slangh"
StaticCombo( S_USE_PATTERN, F_USE_PATTERN, Sys( ALL ) );

RenderState( DepthWriteEnable, true );
RenderState( DepthEnable, true );
RenderState( ColorWriteEnable0, RGBA );

RenderState( CullMode, NONE );

RenderState( StencilEnable, true );
RenderState( StencilRef, 1 );

// Write to stencil if we're doing the inside pass
#if D_OUTLINE_PASS == OUTLINE_INSIDE
	RenderState( StencilPassOp, REPLACE );
	RenderState( BackStencilPassOp, REPLACE );
	RenderState( StencilFunc, ALWAYS );
	RenderState( BackStencilFunc, ALWAYS );
#else
	RenderState( StencilPassOp, KEEP );
	RenderState( StencilFunc, NOT_EQUAL );
	RenderState( BackStencilFunc, NOT_EQUAL );
#endif

Texture2D _BaseOpacity < Attribute( "BaseOpacity" ); >;
Texture2D _ColorTexture < Attribute( "ColorTexture" ); SrgbRead( true ); >;

float4 _ColorMain < UiType( Color ); UiGroup( "Color" ); Attribute( "Color" ); >;
float4 _ColorOccluded < UiType( Color ); UiGroup( "Color" ); Attribute( "ObscuredColor" ); >;

float2 g_vPatternScrollRate < Default2( 0.0, 0.0 ); Range2( -2.0, -2.0, 2.0, 2.0 ); UiGroup( "Pattern,11/1" ); >;

CreateInputTexture2D( Pattern, Linear, 8, "", "",  "Pattern,11/2", Default( 1.0 ) );
Texture2D g_tPattern < Channel( R, Box( Pattern ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
// How much of the pixel the outline covers: 1 to half a pixel inside its width from the edge, 0 half a pixel past it (as
// Hud/line.shader's analytical antialiasing)
float OutlineCoverage( PixelInput i )
{
	float2 vA = i.vOutlineEdge.xy;
	float2 vEdge = i.vOutlineEdge.zw - vA;
	float t = saturate( dot( i.vOutlinePixel - vA, vEdge ) / max( dot( vEdge, vEdge ), 1e-6 ) );
	float flDistance = length( i.vOutlinePixel - ( vA + t * vEdge ) );
	return saturate( i.flOutlineRadius + 0.5 - flDistance );
}
#endif

//
// Main
//
#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
[shader( "pixel" )] float4 MainPs( PixelInput i, out float flDepth : SV_Depth ): SV_Target
#else
[shader( "pixel" )] float4 MainPs( PixelInput i ): SV_Target
#endif
{
	float objectDepth = i.vPositionSs.z;

	float2 screenUv = CalculateViewportUv( i.vPositionSs.xy );

	float worldDepth = Depth::GetNormalized( i.vPositionSs.xy - g_vViewportOffset );

	// Get the nearest depth of the object to avoid z-fighting
	// This also solves disparities between MSAA samples and our Depth::GetNormalized() sample
	#if D_OUTLINE_PASS == OUTLINE_INSIDE
		objectDepth += fwidth(objectDepth);
	#endif

	float alpha = _BaseOpacity.Sample( g_sPointWrap, i.vTextureCoords.xy ).a;

	float diff = (worldDepth - objectDepth);

	float4 vColor = lerp( _ColorMain, _ColorOccluded, diff >= 0.0f );

	// Add pattern to alpha
	#if ( S_USE_PATTERN == 1 )
	{
		float2 vPatternDims = TextureDimensions2D( g_tPattern, 0 ).xy;
		float fPattern = g_tPattern.Sample( g_sPointWrap, i.vPositionSs.xy / vPatternDims + ( g_vPatternScrollRate * g_flTime ) ).r;
		vColor.a *= fPattern;
	}
	#endif

	// The outline's edge fades over a pixel. Where edges' quads overlap the one covering more wins (its depth is greater,
	// which the depth test keeps), the nearer object's where both cover the pixel
	#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
	{
		float flCoverage = OutlineCoverage( i );
		vColor.a *= flCoverage;
		flDepth = flCoverage * 0.999 + saturate( objectDepth ) * 0.001;
	}
	#endif

	// We do custom blending here to avoid overdraw
	float4 vScene = _ColorTexture.Sample( g_sPointWrap, screenUv );
	float flBlend = saturate( vColor.a );
	vColor.rgb = lerp( vScene.rgb, vColor.rgb, flBlend );

	// Composite alpha over the scene so outlines stay visible on cameras with a transparent background
	vColor.a = lerp( vScene.a, 1.0f, flBlend );

	return vColor;
}
#endif // PS
