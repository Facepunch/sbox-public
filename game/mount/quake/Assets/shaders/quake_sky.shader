
HEADER
{
	Description = "Quake sky (two scrolling layers projected onto a flattened dome)";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

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

	SamplerState g_sSampler0 < Filter( Point ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( SolidLayer, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 1.00 ) );
	CreateInputTexture2D( AlphaLayer, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 0.00 ) );
	Texture2D g_tSolidLayer < Channel( RGBA, Box( SolidLayer ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tAlphaLayer < Channel( RGBA, Box( AlphaLayer ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	// Quake scrolls the background layer at 8 units/sec and the cloud layer over it at 16,
	// wrapping to the 128 unit texture size so the offset doesn't lose precision over time.
	float Scroll( float flSpeed )
	{
		return fmod( g_flTime * flSpeed, 128.0 ) / 128.0;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 vDirection = i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs.xyz;

		// EmitSkyPolys: squash the view vector on Z and rescale it to a fixed radius, which is
		// what gives the sky its shallow dome look. Texture space is 128 units across.
		vDirection.z *= 3.0;
		float2 vSkyCoords = vDirection.xy * ( ( 6.0 * 63.0 / 128.0 ) / length( vDirection ) );

		float4 vSolid = Tex2DS( g_tSolidLayer, g_sSampler0, vSkyCoords + Scroll( 8.0 ) );
		float4 vClouds = Tex2DS( g_tAlphaLayer, g_sSampler0, vSkyCoords + Scroll( 16.0 ) );

		Material m = Material::Init();
		m.Albedo = float3( 0, 0, 0 );
		m.Emission = lerp( vSolid.rgb, vClouds.rgb, vClouds.a ) * 2.0;
		m.Normal = i.vNormalWs;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.Opacity = 1;

		return ShadingModelStandard::Shade( i, m );
	}
}
