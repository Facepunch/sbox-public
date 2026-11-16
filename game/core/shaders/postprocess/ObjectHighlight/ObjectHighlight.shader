MODES
{
	Forward();
	Depth(); // It was doing this before, is it correct?
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_USE_PATTERN, 0..1, "Rendering" );
}

COMMON
{
	#define USE_CUSTOM_SHADING
	#include "common/shared.hlsl"

	DynamicCombo( D_OUTLINE_PASS, 0..1, Sys( ALL ) );
	
	#define OUTLINE_INSIDE 0
	#define OUTLINE_OUTSIDE 1
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"

	#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
		// In pixels: where this is, the edge's ends and the outline's width
		noperspective float2 vOutlinePixel : TEXCOORD8;
		nointerpolation float4 vOutlineEdge : TEXCOORD9;
		nointerpolation float flOutlineRadius : TEXCOORD10;
	#endif
};

VS
{
	#include "common/vertex.hlsl"
	//
	// Main
	//
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );

		// The geometry stage sets the outline's own
		#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
			o.vOutlinePixel = 0;
			o.vOutlineEdge = 0;
			o.flOutlineRadius = 0;
		#endif

		return FinalizeVertex( o );
	}
}

//
// Used for outline: a quad round each edge of each triangle, the outline's width and half a pixel past it every way. The
// inside pass's stencil keeps the outline outside the object; the pixel shader fades it over one pixel from the distance
// to the edge, so it's smooth at any sample count
//
GS
{
#if D_OUTLINE_PASS == OUTLINE_OUTSIDE

	float _LineSize < UiType( Color ); UiGroup( "Color" ); Attribute( "LineWidth" ); Default( 0.2f ); >;

	#include "common/vertex.hlsl"

	// The outline's width in pixels: LineWidth / 64 of the normalized inverse viewport size in projection space, which is
	// the same number of pixels across and down
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

		// even though we do -f-vk-invert this is the only shader that does not behave with it
		#if defined( VULKAN )
			o.vPositionPs.y = -o.vPositionPs.y;
		#endif

		return o;
	}

	//
	// Main
	//
	[maxvertexcount(12)]
	void MainGs(triangle in PixelInput vertices[3], inout TriangleStream<PixelInput> triStream)
	{
		const float flRadius = OutlineRadius();

		[unroll]
		for ( int nEdge = 0; nEdge < 3; nEdge++ )
		{
			[unroll]
			for ( uint nCorner = 0; nCorner < 4; nCorner++ )
				triStream.Append( OutlineCorner( vertices[nEdge], vertices[( nEdge + 1 ) % 3], nCorner, flRadius ) );

			triStream.RestartStrip();
		}
	}
#endif
}

PS
{
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
	
	#if ( D_OUTLINE_PASS == OUTLINE_OUTSIDE )
	// How much of the pixel the outline covers: 1 to half a pixel inside its width from the edge, 0 half a pixel past it
	// (as Hud/line.shader's analytical antialiasing)
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
	float4 MainPs( PixelInput i, out float flDepth : SV_Depth ): SV_Target
	#else
	float4 MainPs( PixelInput i ): SV_Target
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

		// The outline's edge fades over a pixel. Where edges' quads overlap the one covering more wins (its depth is
		// greater, which the depth test keeps), the nearer object's where both cover the pixel
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
}