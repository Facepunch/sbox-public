HEADER
{
	Description = "Source 1 projected eyes";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_SOURCE_EYE_REFRACT, 0..1, "Source 1" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define S_UV2 1
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
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

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	StaticCombo( S_SOURCE_EYE_REFRACT, F_SOURCE_EYE_REFRACT, Sys( ALL ) );
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	SamplerState g_sSourceSclera < Attribute( "g_tSourceScleraSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceEye < Attribute( "g_tSourceIrisSampler" ); Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;

	CreateInputTexture2D( SourceSclera, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceIris, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 0 ) );
	Texture2D g_tSourceSclera < Channel( RGBA, Box( SourceSclera ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tSourceIris < Channel( RGBA, Box( SourceIris ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	float3 g_vSourceEyeTint < Default3( 1, 1, 1 ); >;
	float g_flSourceDilation < Default( 0.5 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material material = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		material.Normal = normalize( i.vNormalWs );
		material.WorldTangentU = i.vTangentUWs;
		material.WorldTangentV = i.vTangentVWs;
		material.LightmapUV = i.vLightmapUV;
		material.TextureCoords = i.vTextureCoords.xy;
		#if S_SOURCE_EYE_REFRACT
			float2 offset = i.vTextureCoords.zw * 0.5 - 0.25;
			float radius = saturate( length( offset ) / 0.2 );
			float dilation = saturate( g_flSourceDilation ) * 2.5 - 1.25;
			float2 uv = 0.5 + offset * lerp( 1.0, radius, dilation );
			// EyeRefract stores sclera in iris RGB; its alpha is corneal noise, not coverage.
			material.Albedo = g_tSourceIris.Sample( g_sSourceEye, uv ).rgb * g_vSourceEyeTint;
			material.Roughness = 0.15;
		#else
			float4 sclera = g_tSourceSclera.Sample( g_sSourceSclera, i.vTextureCoords.xy );
			float4 iris = g_tSourceIris.Sample( g_sSourceEye, i.vTextureCoords.zw );
			material.Albedo = lerp( sclera.rgb, iris.rgb, iris.a );
			material.Opacity = sclera.a;
			material.Roughness = 1;
		#endif
		float4 output = ShadingModelStandard::Shade( material );
		return output;
	}
}
