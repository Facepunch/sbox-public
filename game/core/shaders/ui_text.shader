HEADER
{
	DevShader = true;
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	
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
	#include "ui/text.hlsl"
	#include "common/classes/Fog.hlsl"

	float g_FogStrength < Attribute( "g_FogStrength" ); >;

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	#if ( D_BLENDMODE == 1 )
		// Multiplication changes the surface color without replacing its coverage.
		RenderState( SrcBlendAlpha, ZERO );
		RenderState( DstBlendAlpha, ONE );
	#endif



	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	/// <summary>
	/// Apply world-panel fog to straight color according to the panel's blend mode.
	/// Premultiplication must follow this step so fog radiance retains glyph coverage.
	/// </summary>
	void ApplyTextFog( float3 worldPos, float2 screenPos, inout float4 color )
	{
		#if ( D_BLENDMODE == 1 )
			color.rgb = lerp( float3( 1, 1, 1 ), color.rgb, saturate( Fog::ModulationTransmittance( worldPos, screenPos, g_FogStrength ) ) );
		#elif ( D_BLENDMODE == 2 )
			color.a *= Fog::Transmittance( worldPos, screenPos, g_FogStrength );
		#else
			color.rgb = Fog::Apply( worldPos, screenPos, color.rgb, g_FogStrength );
		#endif
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		UI_CommonProcessing_Pre( i );

		// Composited straight from the glyph outlines at whatever size the quad lands on screen
		int2 size = int2( TextWidth, TextHeight );
		float2 p = i.vTexCoord.xy * size;
		float4 vColor = TextComposite( p, TextFootprint( p ), TextInstanceOffset, TextTileOffset, TextTilesX, size, 0 );
		vColor.rgb = SrgbGammaToLinear( vColor.rgb );

		o.vColor = vColor;
		o.vColor.a *= i.vColor.a;
		o = UI_CommonProcessing_Post( i, o, vColor.a );

		// Apply fog only on world panels
		#if D_WORLDPANEL
			ApplyTextFog( i.vPositionWs.xyz, i.vPositionPs.xy, o.vColor );
		#endif

		#if ( D_BLENDMODE == 1 )
			// Multiply blending ignores source alpha, so encode glyph coverage in its multiplier.
			o.vColor.rgb = lerp( float3( 1, 1, 1 ), o.vColor.rgb, o.vColor.a );
		#elif ( D_BLENDMODE == 3 )
			// Caller asked for a premultiplied blend state, so hand it premultiplied output.
			o.vColor.rgb *= o.vColor.a;
		#endif

		return o;
	}
}
