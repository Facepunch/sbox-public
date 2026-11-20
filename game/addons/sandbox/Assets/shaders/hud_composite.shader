MODES
{
	Default();
	Forward();
}

FEATURES
{
	#include "ui/features.hlsl"
}

COMMON
{
	#include "ui/common.hlsl"
}

VS
{
	#include "ui/vertex.hlsl"
}

PS
{
	#include "ui/pixel.hlsl"

	// HUD
	Texture2D 	g_tHud 			< Attribute( "HudTexture" ); SrgbRead( false ); >;
	float2 		g_vHudInvSize	< Attribute( "HudInvSize" ); >;

	// frame buffer
	BoolAttribute( bWantsFBCopyTexture, true );
	Texture2D 	g_tFrame 		< Attribute( "FrameBufferCopyTexture" ); SrgbRead( true ); >;
	float4 		g_vFrameRect 	< Attribute( "FrameBufferCopyRectangle" ); Default4( 0.0, 0.0, 1.0, 1.0 ); >;

	// glow settings
	Texture2D g_tGlow < Attribute( "GlowTexture" ); SrgbRead( false ); >;
	float GlowStrength < Default( 0.22 ); Attribute( "GlowStrength" ); >;	// how fast HDR excess turns into glow
	float ScanlineIntensity < Default( 0.46 ); Attribute( "ScanlineIntensity" ); >;
	float ScanlinePeriod < Attribute( "ScanlinePeriod" ); >;		// screen pixels from one line to the next, set by HudCanvas
	float ScanlineThickness < Default( 0.55 ); Attribute( "ScanlineThickness" ); >;	// share of the period that is dark
	float ScanlineSoftness < Default( 0.04 ); Attribute( "ScanlineSoftness" ); >;	// edge blur, as a share of the period
	float HudOpacity < Default( 1.0 ); Attribute( "HudOpacity" ); >;	// the player's HUD opacity, fades the HUD and its glow together

	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );

	// HUD target is gamma encoded and premultiplied, returns the straight clamped gamma colour and the coverage
	float4 SampleHud( float2 uv )
	{
		float4 s = g_tHud.SampleLevel( g_sTrilinearBorder, uv, 0 );
		return s.a > 0.00001 ? float4( saturate( s.rgb / s.a ), saturate( s.a ) ) : 0;
	}

	float3 Glow( float2 uv )
	{
		float3 vExcess = g_tGlow.SampleLevel( g_sTrilinearClamp, uv, 0 ).rgb;
		return 1.0 - exp( -vExcess * GlowStrength );
	}

	float ScanlineMask( float flPixelY )
	{
		float t = frac( flPixelY / ScanlinePeriod );
		float flHalf = ScanlineThickness * 0.5;
		return 1.0 - smoothstep( flHalf - ScanlineSoftness, flHalf + ScanlineSoftness, abs( t - 0.5 ) );
	}

	float4 MainPs( PS_INPUT i ) : SV_Target0
	{
		float2 vPixel = i.vPositionPs.xy;
		float2 vHudUv = vPixel * g_vHudInvSize;

		float2 vNdc = i.vPositionSs.xy / i.vPositionSs.w;
		float2 vFrameUv = ( float2( vNdc.x, -vNdc.y ) * 0.5 + 0.5 ) * g_vFrameRect.zw;
		float3 vBackdrop = saturate( g_tFrame.SampleLevel( g_sTrilinearClamp, vFrameUv, 0 ).rgb );
		if ( g_bUIFrameGrabEncoded && !g_bUIInPanelLayer )
			vBackdrop = SrgbGammaToLinear( vBackdrop );

		// additive glow onto the frame, in linear space
		float3 vGlow = Glow( vHudUv ) * HudOpacity;
		float3 vLinear = saturate( vBackdrop + vGlow );

		// HUD
		float4 vHud = SampleHud( vHudUv );
		vHud.a *= HudOpacity;
		float3 vHudLinear = 0;
		if ( vHud.a > 0 )
		{
			// blended in gamma space like the rest of the UI
			vLinear = SrgbGammaToLinear( lerp( SrgbLinearToGamma( vLinear ), vHud.rgb, vHud.a ) );
			vHudLinear = SrgbGammaToLinear( vHud.rgb );
		}

		// scanlines
		vLinear += ScanlineMask( vPixel.y ) * saturate( ScanlineIntensity ) * ( vHudLinear * vHud.a + vGlow );

		return UIEncodeOutput( float4( saturate( vLinear ), 1.0 ) );
	}
}
