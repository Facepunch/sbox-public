HEADER
{
	Description = "Max Payne 2 lightmapped world geometry";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"

	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
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
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSampler1 < Filter( Bilinear ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Lightmap, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tLightmap < Channel( RGBA, Box( Lightmap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float4 albedo = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );

		#if ( S_ALPHA_TEST )
			clip( albedo.a - 0.5 );
		#endif

		float3 lightmap = Tex2DS( g_tLightmap, g_sSampler1, i.vTextureCoords.zw ).rgb;

		m.Albedo = albedo.rgb;
		m.Normal = i.vNormalWs;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = albedo.a;
		m.Emission = albedo.rgb * lightmap * 2.0;
		m.Transmission = 0;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
