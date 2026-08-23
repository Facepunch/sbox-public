HEADER
{
	DevShader = true;
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
	Forward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "ui/features.hlsl"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "ui/common.hlsl"
	
	DynamicCombo( D_LAYERED, 0..1, Sys( PC ) );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "ui/vertex.hlsl"  
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "ui/pixel.hlsl"
	#include "ui/rounded_rect.hlsl"

	float4 CornerRadius < Attribute( "BorderRadius" ); >;	// horizontal radii ( top-left, top-right, bottom-left, bottom-right )
	float4 CornerRadiusV < Attribute( "BorderRadiusV" ); >;	// vertical radii, same order
	float BoxBloat < Default( 0.0 ); Attribute( "BoxBloat" ); >;	// pixels the quad is grown by on each side, so the edge antialiasing has room

	float Brightness < Attribute( "Brightness" ); Default( 1 ); >;
	float Contrast < Attribute( "Contrast" );  Default( 1 ); >;
	float Saturate < Attribute( "Saturate" );  Default( 1 ); >;
	float Invert < Attribute( "Invert" );  Default( 0 ); >;
	float HueRotate < Attribute( "HueRotate" );  Default( 0 ); >;
	float Sepia< Attribute("Sepia"); Default( 0 ); >;
	float BlurScale < Attribute( "BlurScale" ); Default( 10 ); >;

	BoolAttribute( bWantsFBCopyTexture, true );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( true ); >;
	float4 g_vFBCopyTextureRect < Attribute( "FrameBufferCopyRectangle" ); Default4( 0., 0., 1.0, 1.0 ); >;

	float4 g_vViewport < Source( Viewport ); >; 

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------


	float4 DoColorMatrix( float4 color, float4x4 mColorMatrix )
	{
		return saturate(mul(mColorMatrix, color));
	}

	float3 DoColorMatrix( float3 color, float4x4 mColorMatrix )
	{
		return mul(mColorMatrix, float4( color, 1.0f )).rgb;
	}

	float4 DoBackdropFilter( float2 uv )
	{
		// transform the uv by the g_vFBCopyTextureRect
		uv.x = uv.x * g_vFBCopyTextureRect.z;
		uv.y = uv.y * g_vFBCopyTextureRect.w;

		float3 backdrop = g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearClamp, uv, sqrt( BlurScale / 2 ) ).rgb;

		// Filter in gamma space. A grab of a float target that holds gamma-space UI is already there.
		if ( !g_bUIFrameGrabEncoded || g_bUIInPanelLayer )
		{
			backdrop = SrgbLinearToGamma( backdrop );
		}

		// Sepia
		backdrop = DoColorMatrix (
			backdrop, 
			float4x4(
				0.393f + 0.607f * (1.0f - Sepia), 0.769f - 0.769f * (1.0f - Sepia), 0.189f - 0.189f * (1.0f - Sepia), 0.0f,
				0.349f - 0.349f * (1.0f - Sepia), 0.686f + 0.314f * (1.0f - Sepia), 0.168f - 0.168f * (1.0f - Sepia), 0.0f,
				0.272f - 0.272f * (1.0f - Sepia), 0.534f - 0.534f * (1.0f - Sepia), 0.131f + 0.869f * (1.0f - Sepia), 0.0f,
				0.0f, 0.0f, 0.0f, 1.0f
			)
		);

		// invert ( default 0 )
		backdrop = lerp(backdrop, 1 - backdrop, Invert);

		 // Contrast (default 1)
		backdrop = saturate(lerp(float3(0.5, 0.5, 0.5), backdrop, Contrast));

		backdrop = SrgbGammaToLinear( backdrop );

		float3 hsv = RgbToHsv( backdrop );
		hsv.r += (HueRotate / 360); // param to normalized degrees
		hsv.r = hsv.r % 1;
		hsv.g = lerp( 0, hsv.g, Saturate ); // saturation
		hsv.b *= Brightness; // value
		
		backdrop = HsvToRgb( hsv );

		return float4( backdrop, 1 );
	}


	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		
		UI_CommonProcessing_Pre( i );

#if D_WORLDPANEL
		float2 vUV = (i.vPositionPs.xy - g_vViewportOffset) * g_vInvViewportSize;
#else
		#if D_LAYERED
				float2 vUV = ((BoxPosition + i.vPositionPs.xy - g_vViewportOffset) * g_vInvViewportSize);
		#else
				float2 vUV = i.vTexCoord.zw;
		#endif
#endif

		o.vColor = DoBackdropFilter( vUV );

		// The quad may be the box grown by BoxBloat; make texcoords 0..1 across the box itself
		float2 boxUv = ( i.vTexCoord.xy - 0.5 ) * ( BoxSize + BoxBloat * 2.0 ) / max( BoxSize, 0.0001 ) + 0.5;
		float edge = SdfCoverage( RoundedRectSdfUv( boxUv, BoxSize, CornerRadius, CornerRadiusV ) );
		o.vColor.a = edge * i.vColor.a;

		return UI_CommonProcessing_Post( i, o, edge );
	}
}
