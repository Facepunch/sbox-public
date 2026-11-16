// ObjectHighlight.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// The outside pass's geometry stage draws each triangle again at 4 to 7 offsets round a circle, as one triangle strip
// (so consecutive copies are joined by the strip's in-between triangles). Here the vertex stage makes that strip: the
// draw runs 21 vertices per triangle (ExpandPrimitives), with the triangle's corners as per-instance inputs (the second's
// semantics 4 up, the third's 8 up); strip vertices past the last copy repeat it, which adds only empty triangles.

// MODES: Forward();
// MODES: Depth(); // It was doing this before, is it correct?

#include "sbox/pass.slangh"

#include "common/features.hlsl"
Feature( F_USE_PATTERN, 0..1, "Rendering" );

#define USE_CUSTOM_SHADING
#include "common/shared.hlsl"

DynamicCombo( D_OUTLINE_PASS, 0..1, Sys( ALL ) );

#define OUTLINE_INSIDE 0
#define OUTLINE_OUTSIDE 1

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

#if ( PROGRAM == VFX_PROGRAM_VS )
#include "common/vertex.hlsl"

#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )

// Up to seven copies of three strip vertices each
ExpandPrimitives( TRIANGLES, 21, TRIANGLE_STRIP );

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

// The geometry stage offset a position its vertex stage had already flipped for Vulkan, then flipped it back before its own
// flip: its offsets land mirrored in y against one added before this stage's flip
void PositionOffset (inout PixelInput input, float2 vOffsetDir, float flOutlineSize)
{
	float2 vAspectRatio = normalize(g_vInvViewportSize);
	input.vPositionPs.xy += (float2( vOffsetDir.x, -vOffsetDir.y ) * 2.0) * vAspectRatio * input.vPositionPs.w * flOutlineSize;
}

[shader( "vertex" )] PixelInput MainVs( VertexInput i, CornerInputs c, uint nStripVertex : SV_VertexID )
{
	const float flOutlineSize = _LineSize / 64.0f;
	const float fTwoPi = 6.28318f;
	const int nNumIterations = clamp( _LineSize * 10, 3, 6 ); // Thin lines don't need many iterations

	// The geometry stage's strip: copy n of corners 2, 0, 1 for n = 0 to nNumIterations; vertices past it repeat its last
	uint nVertex = min( nStripVertex, (uint)( nNumIterations * 3 + 2 ) );
	uint nCopy = nVertex / 3;
	uint nCorner = ( nVertex % 3 + 2 ) % 3;

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

	PixelInput o = FinalizeVertex( ProcessVertex( v ) );

	float fCycle = (float)nCopy / nNumIterations;
	float2 vOffset = float2(
		( sin( fCycle * fTwoPi ) ),
		( cos( fCycle * fTwoPi ) )
	);

	PositionOffset( o, vOffset, flOutlineSize );
	return o;
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
#include "common.fxc"
#include "common/classes/Depth.hlsl"
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

//
// Main
//
[shader( "pixel" )] float4 MainPs( PixelInput i ): SV_Target
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

	// We do custom blending here to avoid overdraw
	float4 vScene = _ColorTexture.Sample( g_sPointWrap, screenUv );
	float flBlend = saturate( vColor.a );
	vColor.rgb = lerp( vScene.rgb, vColor.rgb, flBlend );

	// Composite alpha over the scene so outlines stay visible on cameras with a transparent background
	vColor.a = lerp( vScene.a, 1.0f, flBlend );

	return vColor;
}
#endif // PS
