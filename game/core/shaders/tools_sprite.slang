// tools_sprite: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The .shader's geometry stage makes a quad of each point. Here the vertex stage does it: the draw runs four vertices
// per point (ExpandPrimitives), with the point as per-instance inputs, and each vertex makes one corner. A point the
// box test rejects collapses to nothing.

// HEADER: DevShader = true;
// HEADER: Description = "Point rendering for tools";

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );
Mode( Depth, S_MODE_DEPTH );
Mode( ToolsUtil );

Feature( F_TEXTURED, 0..1 );
Feature( F_ALPHA_TEST, 0..2 ( 0 = "None", 1 = "Separate Alpha (RGB+A)", 2 = "Embedded Alpha (RGBA)" ) );
Feature( F_TRANSLUCENT, 0..1 );
Feature( F_PER_POINT_SIZE, 0..1 );
Feature( F_WORLD_SPACE_SIZE, 0..1 ( 0 = "Size In Pixels", 1 = "Size In World Space" ) );
Feature( F_CULL_LARGER_THAN_BOX, 0..1, "Cull if Larger Than Box" );

#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"
#include "sbox/math.slangh"

// Functions ----------------------------------------------------------------------------------------------------------------------------------------------

float2 ProjSpaceToScreenSpace( float4 vPosPs )
{
	float2 pos01 = 0.5 *( vPosPs.xy / vPosPs.w ) + float2( 0.5, 0.5 );
	return float2( pos01.x, 1-pos01.y ) / g_vInvViewportSize.xy;
}

// test to see if the sprite is larger than a box - if so, reject it (because the sprite is supposed to be 'inside' the box)
// (strictly not correct if you're looking at the box along something other than a primary axis)
bool PassesBoxSizeTest( float3 vPositionWs, float4 vCullBoxHalfSizeWs )
{
	#if ( !S_CULL_LARGER_THAN_BOX )
	{
		// no test
		return true;
	}
	#else
	{
		float flMinSize = vCullBoxHalfSizeWs.w;
		if ( flMinSize == 0 )
		{
			// no test
			return true;
		}

		// project the box onto the screen
		float3 vMinPosWs = vPositionWs.xyz - vCullBoxHalfSizeWs.xyz;
		float3 vMaxBoxWs = vPositionWs.xyz + vCullBoxHalfSizeWs.xyz;
		float2 vMinBoxSs = ProjSpaceToScreenSpace( Position3WsToPs( vMinPosWs ) );
		float2 vMaxBoxSs = ProjSpaceToScreenSpace( Position3WsToPs( vMaxBoxWs ) );

		// test the screen
		float2 vBoxExtentSS = float2( abs( vMaxBoxSs.x - vMinBoxSs.x ), abs( vMaxBoxSs.y - vMinBoxSs.y ) );
		return ( vBoxExtentSS.x > flMinSize ) && ( vBoxExtentSS.y > flMinSize );
	}
	#endif
}

float4 CalculateSpritePs( float3 vWorldSpace, float flPointSize, float2 vDelta )
{
	float4 resultPs;

	// worldspace
	#if ( S_WORLD_SPACE_SIZE )
	{
		float3 vecCameraRightDir = cross( g_vCameraDirWs, g_vCameraUpDirWs );
		vWorldSpace += 0.5 * flPointSize * vDelta.x * vecCameraRightDir;
		vWorldSpace += 0.5 * flPointSize * vDelta.y * g_vCameraUpDirWs;
	}
	#endif

	// transform into screenspace
	resultPs = Position3WsToPs( vWorldSpace );

	// screenspace
	#if ( !S_WORLD_SPACE_SIZE )
	{
		float2 vPixelSize = 1.0 * g_vInvViewportSize.xy;
		resultPs.xy += ( flPointSize * vPixelSize.xy * vDelta.xy * resultPs.w );
	}
	#endif

	return resultPs;
}

struct PS_INPUT
{
	float4 vColor				: COLOR0;
	float2 vTexCoord			: TEXCOORD0;
	float4 vBorderBoundsSs		: TEXCOORD1; // x1, x2, y1, y2

	#if ( S_MODE_DEPTH )
		float flDepthNormalized	: TEXCOORD2;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs		: SV_Position;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );
StaticCombo( S_PER_POINT_SIZE, F_PER_POINT_SIZE, Sys( ALL ) );

// The geometry stage's combos, which the vertex stage takes over
StaticCombo( S_WORLD_SPACE_SIZE, F_WORLD_SPACE_SIZE, Sys( ALL ) );
StaticCombo( S_CULL_LARGER_THAN_BOX, F_CULL_LARGER_THAN_BOX, Sys( ALL ) );

// Four vertices per point, as a strip, in the geometry stage's order
ExpandPrimitives( POINTS, 4, TRIANGLE_STRIP );

struct VS_INPUT
{
	float3 vPositionWs			: POSITION < Semantic( PosXyz ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	#if ( S_PER_POINT_SIZE )
		float3 vUv_flPointSize	: TEXCOORD0 < Semantic( NONE ); >;
	#endif

	#if ( S_CULL_LARGER_THAN_BOX )
		float4 vCullBoxHalfSizeWs	: TANGENT0 < Semantic( None ); >; // w component is min size
	#endif
};

// Constants ----------------------------------------------------------------------------------------------------------------------------------------------
float g_flUniformPointSize < Default( 3.0 ); Range( 0.0, 64.0 ); >;
FloatAttribute( UniformPointSize, g_flUniformPointSize );

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i, uint nCorner : SV_VertexID )
{
	float3 vPositionWs = i.vPositionWs.xyz;

	#if( S_PER_POINT_SIZE )
		float flPointSize = i.vUv_flPointSize.z;
	#else
		float flPointSize = g_flUniformPointSize;
	#endif

	float2 border1Ss = ProjSpaceToScreenSpace( CalculateSpritePs( vPositionWs, flPointSize, float2( -1.0, -1.0 ) ) );
	float2 border2Ss = ProjSpaceToScreenSpace( CalculateSpritePs( vPositionWs, flPointSize, float2( 1.0, 1.0 ) ) );

	// Corners in the geometry stage's order: (-1, 1), (-1, -1), (1, 1), (1, -1)
	float2 vDelta = float2( nCorner < 2 ? -1.0 : 1.0, ( nCorner & 1 ) != 0 ? -1.0 : 1.0 );

	PS_INPUT o = ( PS_INPUT )0;
	o.vBorderBoundsSs = float4( border1Ss.x, border2Ss.y, border2Ss.x, border1Ss.y ); // x1, y1, x2, y2
	o.vPositionPs = CalculateSpritePs( vPositionWs, flPointSize, vDelta );
	o.vColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
	o.vColor.a = i.vColor.a;
	o.vTexCoord.xy = float2( vDelta.x * 0.5 + 0.5, 0.5 - vDelta.y * 0.5 );

	#if ( S_MODE_DEPTH )
	{
		// Dividing by far plane distance so we have depth [0.0, 1.0] relative to the far plane
		o.flDepthNormalized = dot( g_vCameraDirWs.xyz, CalculateCameraToPositionRayWs( vPositionWs.xyz ) ) / g_flFarPlane;
	}
	#endif

	// A point the box test rejects makes no quad
	#if ( S_CULL_LARGER_THAN_BOX )
		if ( !PassesBoxSizeTest( vPositionWs.xyz, i.vCullBoxHalfSizeWs.xyzw ) )
			o.vPositionPs = float4( 0.0, 0.0, 0.0, 0.0 );
	#endif

	return o;
}
#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );
DynamicCombo( D_BORDER, 0..1, Sys( ALL ) );

StaticCombo( S_TEXTURED, F_TEXTURED, Sys( ALL ) );
StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

// Textures -----------------------------------------------------------------------------------------------------------------------------------------------
CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent", Default3( 1.0, 1.0, 1.0 ) );

#if ( S_ALPHA_TEST == 0 ) // None
	Texture2D g_tColorA < Channel( RGB, Box( TextureColor ), Srgb );
								  OutputFormat( DXT1 ); SrgbRead( true ); >;
#elif ( S_ALPHA_TEST == 1 ) // Separate Alpha
	Texture2D g_tColorB < Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb );
								  Channel( A, PreserveCoverage( TextureTranslucency ), Linear );
								  OutputFormat( DXT5 ); SrgbRead( true ); >;
#else // Embedded Alpha
	Texture2D g_tColorC < Channel( RGBA, Box( TextureColor ), Srgb );
								  OutputFormat( DXT5 ); SrgbRead( true ); >;
#endif

// Render State -------------------------------------------------------------------------------------------------------------------------------------------
RenderState( FillMode, SOLID );

#if ( D_NO_ZTEST )
	RenderState( DepthEnable, false );
#else
	RenderState( DepthEnable, true );
#endif

#if ( D_NO_ZTEST || S_TRANSLUCENT )
	RenderState( DepthWriteEnable, false );
#else
	RenderState( DepthWriteEnable, true );
#endif

#if ( S_TRANSLUCENT )
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );
#endif

RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );

// Globals ------------------------------------------------------------------------------------------------------------------------------------------------
float g_flAlphaTestReference < Default ( 0.3 ); Range( 0.01, 0.99 ); >;

float4 g_vBorderColor < Attribute( "BorderColor" );  Default4( 0.0, 0.0, 0.0, 1.0 ); >;
float g_flBorderWidthPx < Attribute( "BorderWidthPx" );  Default( 1.0 ); >;

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_OUTPUT
{

	#if ( S_MODE_DEPTH )
		float4 vDepthAndDepthSquared: SV_Target0;
	#else
		float4 vColor : SV_Target0;
	#endif
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float4 vAlbedo = i.vColor.rgba;

	#if ( S_TEXTURED )
		#if ( S_ALPHA_TEST == 0 ) // None
			vAlbedo.rgba *= g_tColorA.Sample( g_sDefault, i.vTexCoord ).rgba;
		#elif ( S_ALPHA_TEST == 1 ) // Separate Alpha
			vAlbedo.rgba *= g_tColorB.Sample( g_sDefault, i.vTexCoord ).rgba;
		#else // Embedded Alpha
			vAlbedo.rgba *= g_tColorC.Sample( g_sDefault, i.vTexCoord ).rgba;
		#endif
	#endif

	#if ( D_BORDER ) // Give us a border
		float x1 = i.vBorderBoundsSs.x + g_flBorderWidthPx;
		float y1 = i.vBorderBoundsSs.y + g_flBorderWidthPx;
		float x2 = i.vBorderBoundsSs.z - g_flBorderWidthPx;
		float y2 = i.vBorderBoundsSs.w - g_flBorderWidthPx;

		if ( i.vPositionSs.x < x1 ||
			 i.vPositionSs.x > x2 ||
			 i.vPositionSs.y < y1 ||
			 i.vPositionSs.y > y2
			 )
		{
			vAlbedo = g_vBorderColor;
		}
	#endif

	// Handle the alpha test.
	#if ( S_ALPHA_TEST )
	{
		if ( vAlbedo.a < g_flAlphaTestReference )
		{
			clip( -1.0 );
		}
	}
	#endif

	#if ( S_MODE_DEPTH )
	{
		o.vDepthAndDepthSquared.xyzw = float4( i.flDepthNormalized, i.flDepthNormalized * i.flDepthNormalized, 0.0, 0.0 );
	}
	#else
	{
		#if ( !S_TRANSLUCENT )
			vAlbedo.a = 1;
		#endif
		o.vColor = vAlbedo;
	}
	#endif

	return o;
}
#endif // PS
