// gizmo_sprite: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The .shader's geometry stage makes a quad of each point. Here the vertex stage does it: the draw runs four vertices
// per point (ExpandPrimitives), with the point as per-instance inputs, and each vertex makes one corner.

// HEADER: DevShader = true;
// HEADER: Description = "Sprite rendering for gizmos";

#include "sbox/pass.slangh"

Mode( Forward );

#include "system.fxc"
#include "common.fxc"
#include "math_general.fxc"

struct VS_INPUT
{
	float3 vPositionWs			: POSITION < Semantic( PosXyz ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;

	// xy: size
	// z: 0 = screen, 1 = worldspace
	float4 vTexCoord			: TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PS_INPUT
{
	float4 vColor				: COLOR0;
	float2 vTexCoord			: TEXCOORD0;
	float4 vBorderBoundsSs		: TEXCOORD1; // x1, x2, y1, y2

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs		: SV_Position;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
// Four vertices per point, as a strip, in the geometry stage's order
ExpandPrimitives( POINTS, 4, TRIANGLE_STRIP );

float2 RotateDelta( float2 delta, float angle )
{
	float c = cos(angle);
	float s = sin(angle);
	return float2(
		delta.x * c + delta.y * s,
		-delta.x * s + delta.y * c
	);
}

float2 ProjSpaceToScreenSpace( float4 vPosPs )
{
	float2 pos01 = 0.5 *( vPosPs.xy / vPosPs.w ) + float2( 0.5, 0.5 );
	return float2( pos01.x, 1-pos01.y ) / g_vInvViewportSize.xy;
}

float4 CalculateSpritePs( float3 vWorldSpace, float2 flPointSize, float fWorldSpace, float2 vDelta )
{
	float4 resultPs;

	// worldspace
	{
		float3 vecCameraRightDir = cross( g_vCameraDirWs, g_vCameraUpDirWs );
		vWorldSpace += 0.5 * flPointSize.x * vDelta.x * vecCameraRightDir * (fWorldSpace);
		vWorldSpace += 0.5 * flPointSize.y * vDelta.y * g_vCameraUpDirWs * (fWorldSpace);
	}

	// transform into screenspace
	resultPs = Position3WsToPs( vWorldSpace );

	// screenspace
	{
		float2 vPixelSize = 1.0 * g_vInvViewportSize.xy;
		resultPs.xy += ( flPointSize.xy * vPixelSize.xy * vDelta.xy * resultPs.w ) * (1-fWorldSpace);
	}

	return resultPs;
}

[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i, uint nCorner : SV_VertexID )
{
	float3 vPositionWs = i.vPositionWs.xyz;
	float2 flPointSize = i.vTexCoord.xy;
	float fWorldSpace = i.vTexCoord.z;
	float fRotation = i.vTexCoord.w;

	float2 border1Ss = ProjSpaceToScreenSpace( CalculateSpritePs( vPositionWs, flPointSize, fWorldSpace, float2( -1.0, -1.0 ) ) );
	float2 border2Ss = ProjSpaceToScreenSpace( CalculateSpritePs( vPositionWs, flPointSize, fWorldSpace, float2( 1.0, 1.0 ) ) );

	// Corners in the geometry stage's order: (-1, 1), (-1, -1), (1, 1), (1, -1)
	float2 vDelta = float2( nCorner < 2 ? -1.0 : 1.0, ( nCorner & 1 ) != 0 ? -1.0 : 1.0 );

	PS_INPUT o = ( PS_INPUT )0;
	o.vBorderBoundsSs = float4( border1Ss.x, border2Ss.y, border2Ss.x, border1Ss.y ); // x1, y1, x2, y2
	o.vPositionPs = CalculateSpritePs( vPositionWs, flPointSize, fWorldSpace, RotateDelta( vDelta, fRotation ) );
	o.vColor.rgb = SrgbGammaToLinear( i.vColor.rgb );
	o.vColor.a = i.vColor.a;
	o.vTexCoord.xy = float2( vDelta.x * 0.5 + 0.5, 0.5 - vDelta.y * 0.5 );
	return o;
}
#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );

CreateInputTexture2D( TextureColor, Srgb, 8, "", "_sprite", "TextureColor", Default4( 1.0, 1.0, 1.0, 1.0 ) );
Texture2D g_color < Attribute( "TextureColor" ); Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

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

RenderState( StencilEnable, true );
RenderState( StencilPassOp, REPLACE );
RenderState( StencilRef, 1 );

RenderState( BlendEnable, true );
RenderState( SrcBlend, SRC_ALPHA );
RenderState( DstBlend, INV_SRC_ALPHA );
RenderState( BlendOp, ADD );
RenderState( SrcBlendAlpha, ONE );
RenderState( DstBlendAlpha, INV_SRC_ALPHA );
RenderState( BlendOpAlpha, ADD );

struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	o.vColor = i.vColor.rgba;

	o.vColor *= g_color.Sample( g_sDefault, i.vTexCoord ).rgba;
	clip( o.vColor.a - 0.1 );

	return o;
}
#endif // PS
