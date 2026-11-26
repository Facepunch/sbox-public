// Same as engine's highlight shader, but with respect for opacity mask
// This could've been a PR for the engine but lets test it here first?
HEADER
{
	Description = "Sandbox Object Highlight";
	DevShader = true;
}

MODES
{
	Forward();
	Depth();
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

//
// Used for outline
//
GS
{
#if D_OUTLINE_PASS == OUTLINE_OUTSIDE

	float _LineSize < UiType( Color ); UiGroup( "Color" ); Attribute( "LineWidth" ); Default( 0.2f ); >;

	#include "common/vertex.hlsl"

	void PositionOffset( inout PixelInput input, float2 vOffsetDir, float flOutlineSize )
	{
		float2 vAspectRatio = normalize( g_vInvViewportSize );
		input.vPositionPs.xy += ( vOffsetDir * 2.0 ) * vAspectRatio * input.vPositionPs.w * flOutlineSize;

		// even though we do -f-vk-invert this is the only shader that does not behave with it
		#if defined( VULKAN )
			input.vPositionPs.y = -input.vPositionPs.y;
		#endif
	}

	[maxvertexcount( 3 * 7 )]
	void MainGs( triangle in PixelInput vertices[3], inout TriangleStream<PixelInput> triStream )
	{
		const float flOutlineSize = _LineSize / 64.0f;
		const float fTwoPi = 6.28318f;
		const int nNumIterations = clamp( _LineSize * 10, 3, 6 ); // Thin lines don't need many iterations

		PixelInput v[3];

		[unroll]
		for ( float i = 0; i <= nNumIterations; i += 1 )
		{
			float fCycle = i / nNumIterations;

			float2 vOffset = float2(
				( sin( fCycle * fTwoPi ) ),
				( cos( fCycle * fTwoPi ) )
			);

			for ( int i = 0; i < 3; i++ )
			{
				v[i] = vertices[i];
				PositionOffset( v[i], vOffset, flOutlineSize );
			}

			triStream.Append( v[2] );
			triStream.Append( v[0] );
			triStream.Append( v[1] );
		}

		triStream.RestartStrip();
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
	bool g_bHasBaseOpacity < Attribute( "HasBaseOpacity" ); Default( 0 ); >;

	Texture2D _ColorTexture < Attribute( "ColorTexture" ); SrgbRead( true ); >;

	float4 _ColorMain < UiType( Color ); UiGroup( "Color" ); Attribute( "Color" ); >;
	float4 _ColorOccluded < UiType( Color ); UiGroup( "Color" ); Attribute( "ObscuredColor" ); >;

	float g_flOpacityClip < Default( 0.33 ); Range( 0.0, 1.0 ); UiGroup( "Opacity" ); >;

	float2 g_vPatternScrollRate < Default2( 0.0, 0.0 ); Range2( -2.0, -2.0, 2.0, 2.0 ); UiGroup( "Pattern,11/1" ); >;

	CreateInputTexture2D( Pattern, Linear, 8, "", "", "Pattern,11/2", Default( 1.0 ) );
	Texture2D g_tPattern < Channel( R, Box( Pattern ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float4 MainPs( PixelInput i ) : SV_Target
	{
		// Same filtering the material's own passes fetch opacity with, so the mask matches what's on screen
		if ( g_bHasBaseOpacity )
		{
			float flOpacity = Tex2DS( _BaseOpacity, g_sAniso, i.vTextureCoords.xy ).a;
			clip( flOpacity - g_flOpacityClip );
		}

		float objectDepth = i.vPositionSs.z;

		float2 screenUv = CalculateViewportUv( i.vPositionSs.xy );

		float worldDepth = Depth::GetNormalized( i.vPositionSs.xy - g_vViewportOffset );

		// Get the nearest depth of the object to avoid z-fighting
		// This also solves disparities between MSAA samples and our Depth::GetNormalized() sample
		#if D_OUTLINE_PASS == OUTLINE_INSIDE
			objectDepth += fwidth( objectDepth );
		#endif

		float diff = ( worldDepth - objectDepth );

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
}
