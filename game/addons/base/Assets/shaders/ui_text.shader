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
	#include "common/classes/Fog.hlsl"

	int TextureIndex < Attribute( "TextureIndex" ); >; 
	int SamplerIndex < Attribute( "SamplerIndex"); >;
	float g_FogStrength < Attribute( "g_FogStrength" ); >;

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );



	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	float2 RotateTexCoord( float2 vTexCoord, float angle, float2 offset = 0.5 )
	{
		float2x2 m = float2x2( cos(angle), -sin(angle), sin(angle), cos(angle) );
		return mul( m, vTexCoord - offset ) + offset ;
	}

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

		float2 vTexCoord = i.vTexCoord.xy;

		float mipBias = -1.5;
		SamplerState sampler = Bindless::GetSampler( NonUniformResourceIndex( SamplerIndex ) );
		Texture2D tex = GetBindlessTexture2D( TextureIndex + 1 );
		float4 vColor = tex.SampleBias( sampler, vTexCoord, mipBias );

		o.vColor = vColor;

		#if ( D_BLENDMODE == 3 )
			o.vColor *= i.vColor.a;
		#else
			o.vColor.a *= i.vColor.a;
		#endif

		// Apply fog only on world panels
		#if D_WORLDPANEL
			ApplyFog( i.vPositionWs.xyz, i.vPositionPs.xy, o.vColor );
		#endif

		return o;
	}
}
