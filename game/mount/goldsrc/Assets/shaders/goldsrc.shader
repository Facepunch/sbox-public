
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
	
	Feature( F_CHROME, 0..1, "Chrome" );
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"

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
	
	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	
	StaticCombo( S_CHROME, F_CHROME, Sys( ALL ) )
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		m.Albedo = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy ).rgb;
		m.Normal = i.vNormalWs;
		m.TextureCoords = i.vTextureCoords.xy;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = 0;
		m.Transmission = 0;

		if ( S_CHROME )
		{
			m.Roughness = length( m.Albedo ) * 0.5; // Use brightness for roughness
			m.Albedo = m.Albedo.x > 0.0f ? normalize(m.Albedo) : 0.04; // Just get hue from albedo, fix overdark areas
			m.Metalness = 1.0f;
		}

		return ShadingModelStandard::Shade( i, m );
	}
}
