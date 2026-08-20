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
	#include "common/Bindless.hlsl"
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
	#include "ui/blur.hlsl"

	float4 g_vViewport < Source( Viewport ); >; 

	// Texture Samplers ---------------------------------------------------------------------------------------------------------------------------------------
	Texture2D g_tColor < Attribute( "Texture" ); SrgbRead( true ); >;
	float4 g_vInvTextureDim < Source( InvTextureDim ); SourceArg( g_tColor ); >;

	//
	// Filters
	//
	float FilterBrightness< UiType( Slider ); Default( 1.0f ); Attribute( "FilterBrightness" ); >;
	float FilterHueRotate < UiType( Slider ); Default( 0.0f ); Attribute( "FilterHueRotate" ); >;
	float FilterBlur < UiType( Color ); Default( 0 ); Attribute( "FilterBlur" ); >;
	float FilterSaturate< UiType( Slider ); Default( 1.0f ); Attribute( "FilterSaturate" ); >;
	float FilterSepia< UiType( Slider ); Default( 0.0f ); Attribute( "FilterSepia" ); >;
	float FilterInvert< UiType( Slider ); Default( 0.0f ); Attribute( "FilterInvert" ); >;
	float FilterContrast< UiType( Slider ); Default( 1.0f ); Attribute( "FilterContrast" ); >;
	float4 FilterTint< UiType( Color ); Default4(1.0f, 1.0f, 1.0f, 1.0f); Attribute("FilterTint"); >;

	//
	// Masking
	//
	Texture2D g_tMask < Attribute( "MaskTexture" ); SrgbRead( true ); BorderColor( float4( 0, 0, 0, 0 ) ); >;
	DynamicCombo( D_MASK_IMAGE, 0..1, Sys( PC ) ); // Use Mask Image = 1
	int MaskMode <Attribute( "MaskMode" );>;
	int MaskScope <Attribute( "MaskScope" );>; // 0 = Default, 1 = Filter
	float MaskAngle < Default( 0.0 ); Attribute( "MaskAngle" ); >;

	int SamplerIndex < Attribute( "SamplerIndex" ); >;
	int BorderSamplerIndex < Attribute( "BorderSamplerIndex" ); >;

	float4 MaskPos < Default4( 0.0, 0.0, 500.0, 100.0 ); Attribute( "MaskPos" ); >;

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	// https://drafts.csswg.org/filter-effects-1/#elementdef-fecolormatrix
	float4 DoColorMatrix( float4 color, float4x4 mColorMatrix )
	{
		return saturate(mul(mColorMatrix, color));
	}

	float3 DoColorMatrix( float3 color, float4x4 mColorMatrix )
	{
		return mul(mColorMatrix, float4( color, 1.0f )).rgb;
	}

	float GetLuminance( float3 vColor )
	{
		// Convert to XYZ color space, but only the Y component
		return dot( vColor, float3( 0.2126729f, 0.7151522f, 0.0721750f ) );
	}

	// https://drafts.csswg.org/filter-effects-1/#elementdef-fecolormatrix - saturate
	float3 DoSaturate( float3 vColor, float s )
	{
		float3x3 m = float3x3(
			0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s,
			0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s,
			0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s );

		return mul( m, vColor );
	}

	// https://drafts.csswg.org/filter-effects-1/#elementdef-fecolormatrix - hueRotate
	float3 DoHueRotate( float3 vColor, float flDegrees )
	{
		float c = cos( radians( flDegrees ) );
		float s = sin( radians( flDegrees ) );

		float3x3 m = float3x3(
			0.213f + c * 0.787f - s * 0.213f, 0.715f - c * 0.715f - s * 0.715f, 0.072f - c * 0.072f + s * 0.928f,
			0.213f - c * 0.213f + s * 0.143f, 0.715f + c * 0.285f + s * 0.140f, 0.072f - c * 0.072f - s * 0.283f,
			0.213f - c * 0.213f - s * 0.787f, 0.715f - c * 0.715f + s * 0.715f, 0.072f + c * 0.928f + s * 0.072f );

		return mul( m, vColor );
	}

	// The colour part of the filter chain, applied to the (blurred) layer texel. CSS defines these
	// on sRGB values, not linear ones, so the texel is encoded for the maths and decoded after.
	float4 ApplyColorFilters( float4 vColor )
	{
		vColor.rgb = SrgbLinearToGamma( vColor.rgb );

		// Contrast
		vColor.rgb = saturate( (vColor.rgb - 0.5f) * FilterContrast + 0.5f );

		// Sepia
		vColor = DoColorMatrix (
			vColor, 
			float4x4(
				0.393f + 0.607f * (1.0f - FilterSepia), 0.769f - 0.769f * (1.0f - FilterSepia), 0.189f - 0.189f * (1.0f - FilterSepia), 0.0f,
				0.349f - 0.349f * (1.0f - FilterSepia), 0.686f + 0.314f * (1.0f - FilterSepia), 0.168f - 0.168f * (1.0f - FilterSepia), 0.0f,
				0.272f - 0.272f * (1.0f - FilterSepia), 0.534f - 0.534f * (1.0f - FilterSepia), 0.131f + 0.869f * (1.0f - FilterSepia), 0.0f,
				0.0f, 0.0f, 0.0f, 1.0f
			)
		);

		// Invert
		vColor.rgb = lerp( vColor.rgb, 1.0f - vColor.rgb, FilterInvert );

		vColor.rgb = saturate( DoHueRotate( vColor.rgb, FilterHueRotate ) );
		vColor.rgb = saturate( DoSaturate( vColor.rgb, FilterSaturate ) );

		// Brightness is a straight multiply, not a lift of the HSV value
		vColor.rgb = saturate( vColor.rgb * FilterBrightness );

		vColor.rgb = SrgbGammaToLinear( vColor.rgb );

		return vColor * FilterTint;
	}

	float2 RotateTexCoord( float2 vTexCoord, float angle, float2 offset = 0.5 )
	{
		float2x2 m = float2x2( cos(angle), -sin(angle), sin(angle), cos(angle) );
		return mul( m, vTexCoord - offset ) + offset ;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		// The quad is the layer grown by three sigma of blur on each side, see Panel.Layer.cs
		float2 uv = i.vTexCoord.xy;
		float2 uvAdjust = FilterBlur * 3.0 * g_vInvTextureDim.xy;
		uv = lerp(-uvAdjust, 1.0 + uvAdjust, uv);

		UI_CommonProcessing_Pre( i );


		//uv.x = lerp( uvAdjust, 1 - uvAdjust, uv.x );
		//uv.y = lerp( uvAdjust, 1 - uvAdjust, uv.y );

		// filter: blur( r ) - r is the gaussian's standard deviation. Sampled with a border sampler so the blur fades
		// to nothing past the layer's edge instead of wrapping its far side in
		o.vColor = GaussianBlurTexture( g_tColor, g_sTrilinearBorder, uv, FilterBlur, g_vInvTextureDim.xy );
		o.vColor = ApplyColorFilters( o.vColor );

		//
		// Masking
		//
		if ( D_MASK_IMAGE == 1 )
		{
			float2 maskSize = MaskPos.zw;
			float2 vOffset = MaskPos.xy / maskSize;
			
			float2 vUV = -vOffset + ( ( uv ) * ( BoxSize / maskSize ) );
			vUV = RotateTexCoord( vUV, MaskAngle );

			//
			// Sample from mask image
			//
			float4 vMask;

			vMask = g_tMask.Sample( Bindless::GetSampler( SamplerIndex ), vUV );

			//
			// Figure out what we should use as the mask value
			//
			float mask = 1.0f;
			if ( MaskMode == 0 || MaskMode == 2 ) // MatchSource || Luminance
			{
				// MatchSource todo?
				mask = GetLuminance( vMask.xyz );
			}
			else if ( MaskMode == 1 ) // Alpha
			{
				mask = vMask.a; 
			}

			if ( MaskScope == 0 )
			{
				o.vColor.a *= mask;
			}
			else if ( MaskScope == 1 )
			{
				// Sample from original texture, we're using a mask-scope value of "filter"
				// so we use this for blending
				float4 origColor = g_tColor.Sample( Bindless::GetSampler( BorderSamplerIndex ), uv );
				o.vColor = lerp( origColor, o.vColor, mask );
			}
		}

		return UI_CommonProcessing_Post( i, o );
	}
}
