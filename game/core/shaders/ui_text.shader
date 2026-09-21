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



	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	void ApplyFog( float3 worldPos, float2 screenPos, inout float4 color )
	{
		if ( g_FogStrength <= 0 ) return;

		#if ( D_BLENDMODE == 2 )
		{
			float alpha = color.a;
			float3 toCamera = worldPos - g_vCameraPositionWs;

			if ( g_bGradientFogEnabled ) alpha *= 1.0 - CalculateGradientFog( worldPos, toCamera ).a;
			if ( g_bCubemapFogEnabled ) alpha *= 1.0 - CalculateCubemapFog( worldPos, toCamera ).a;
			if ( g_bVolumetricFogEnabled ) alpha *= CalculateVolumetricFog( worldPos, screenPos ).a;

			color.a = lerp( color.a, alpha, g_FogStrength );
		}
		#else
		{
			float3 fogged = Fog::Apply( worldPos, screenPos, color.rgb );
			color.rgb = lerp( color.rgb, fogged, g_FogStrength );
		}
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

		#if ( D_BLENDMODE == 3 )
			// Caller asked for a premultiplied blend state, so hand it premultiplied output
			o.vColor.rgb *= o.vColor.a;
		#endif

		// Apply fog only on world panels
		#if D_WORLDPANEL
			ApplyFog( i.vPositionWs.xyz, i.vPositionPs.xy, o.vColor );
		#endif

		return o;
	}
}
