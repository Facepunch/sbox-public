FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	VrForward();
	Depth();
}

COMMON
{
	#define S_SPECULAR 1
	#include "common/shared.hlsl" 
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

PS
{
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
  
	bool g_bRefraction < Default(0.0f); Attribute( "HasRefractionTexture" ); > ;
	CreateTexture2D( g_RefractionTexture ) < Attribute("RefractionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); > ;    
	float RefractionNormalScale < Default(0.5f); Range(0.01f, 1.0f); UiGroup("Refraction"); > ; 
	float Translucency < Default(1.0f); Range(0.0f, 1.0f); UiGroup("Refraction"); > ;

	bool g_bReflection < Default(0.0f); Attribute( "HasReflectionTexture" ); > ;
	CreateTexture2D( g_ReflectionTexture ) < Attribute("ReflectionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); > ;    
	float RelectionNormalScale < Default(0.5f); Range(0.01f, 1.0f); UiGroup("Reflection"); > ;

	float Refraction < Default(1.0f); Range(0.0f, 1); UiGroup("Water"); > ;

	float BigWaveSize < Default(0.5f); Range(0.0f, 1); UiGroup("Wave"); > ;
	float BigWaveScale < Default(100.0f); Range(0.0f, 256); UiGroup("Wave"); > ;
	float BigWaveTime < Default(1.0f); Range(0.0f, 10); UiGroup("Wave"); > ;

	float NormalScale < Default(0.5f); Range(0.0f, 2); UiGroup("Tweaks"); > ;

	float g_fRoughness < Default(0.02f); Range(0.01f, 1.0f); UiGroup("Tweaks"); > ;
	float g_fMetalness < Default(0.85f); Range(0.01f, 1.0f); UiGroup("Tweaks"); > ;
	float g_fAmbientOcclusion < Default(0.5f); Range(0.01f, 1.0f); UiGroup("Tweaks"); > ;

	Material GetWave( PixelInput ii, float waveTime, float waveScale, float scale )
	{
		PixelInput i = ii;

		float3 worldPos = g_vCameraPositionWs + i.vPositionWithOffsetWs;
		
		worldPos.x += sin( (worldPos.x / 12) + (g_flTime * 16) ) * 0.1;
		worldPos.y += cos( (worldPos.y / 12) + (g_flTime * 22) ) * 0.1;

		worldPos /= scale;

		worldPos.x += sin( (worldPos.x ) + (g_flTime * waveTime) ) * waveScale;
		worldPos.y += cos( (worldPos.y ) + (g_flTime * waveTime) ) * waveScale;

		i.vTextureCoords.xy = worldPos.xy;

		Material m = Material::From( i );

		return m;
	}

	float4 MainPs( PixelInput i ) : SV_Target
	{
		float3 worldPos = g_vCameraPositionWs + i.vPositionWithOffsetWs;

		Material a = GetWave( i, BigWaveTime, BigWaveSize, BigWaveScale );
		Material b = GetWave( i, BigWaveTime, BigWaveSize, BigWaveScale * -1 );

		Material m = Material::lerp( a, b, 0.5 ); // could do this with big noise to reduce tiling?

		float3 camdir = CalculatePositionToCameraDirWs( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz );

		// scale normals, artist adjustment
		m.Normal = lerp( i.vNormalWs, m.Normal, NormalScale );

		float fresnel = pow( 1.0 - dot( ( m.Normal ), camdir ), 5 );
	
		m.Opacity = 1;
		m.Roughness = g_fRoughness;
		m.Metalness = g_fMetalness;
		m.AmbientOcclusion = g_fAmbientOcclusion;

		// let depth write
		if( DepthNormals::WantsDepthNormals() )
			return DepthNormals::Output( m.Normal, m.Roughness, 1 );

		// 
		// Add reflection
		//
		if ( g_bReflection )
        {
			float2 distortion = m.Normal.xy;

            float2 vPositionSs = i.vPositionSs.xy;
			float2 uv = i.vPositionSs.xy * g_vInvViewportSize; 
			uv += distortion * 0.1;
			float3 col = g_ReflectionTexture.Sample(g_ReflectionTexture_sampler, uv).rgb;

			m.Emission.rgb += col * fresnel;
        }

		float4 outCol = ShadingModelStandard::Shade( i, m );
		outCol.rgb = Fog::Apply( worldPos, i.vPositionSs.xy, outCol.rgb );

		//
		// Add refraction
		//
		if ( g_bRefraction )
		{
			float colorSplit = 1;
			float2 uv = i.vPositionSs.xy * g_vInvViewportSize; 
			uv += -m.Normal.xy * Refraction * 0.1 * RefractionNormalScale;

           	float3 col = Tex2DLevel( g_RefractionTexture, uv, 0 ).rgb;
			outCol.rgb = lerp( outCol.rgb, col, (1-fresnel) * Translucency );
		}

		return outCol;
	}
}
