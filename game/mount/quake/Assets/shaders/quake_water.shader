
HEADER
{
	Description = "Quake liquid surface (turbulent warp)";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
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

	SamplerState g_sSampler0 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	float g_flAlpha < Default( 1.0 ); >;

	static const float kWarpAmp = 0.0625;
	static const float kWarpSpeed = 0.33333;
	static const float kTwoPi = 6.2831853;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();
		m.Normal = float3( 0, 0, 1 );
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;

		float2 uv = i.vTextureCoords.xy;

		float t = g_flTime * kWarpSpeed;
		float2 warp;
		warp.x = uv.y + ( kWarpAmp + sin( ( t + uv.x ) * kTwoPi ) * kWarpAmp );
		warp.y = uv.x + ( kWarpAmp + sin( ( t + uv.y ) * kTwoPi ) * kWarpAmp );

		float4 albedo = Tex2DS( g_tColor, g_sSampler0, warp );

		m.Albedo = float3( 0, 0, 0 );
		m.Emission = albedo.xyz;
		m.Opacity = g_flAlpha;
		m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;

		return ShadingModelStandard::Shade( i, m );
	}
}
