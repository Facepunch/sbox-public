
HEADER
{
	Description = "";
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
	#ifndef S_ALPHA_TEST
	#define S_ALPHA_TEST 1
	#endif
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
	#endif
	
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	uint4 vLightStyles : BLENDINDICES10 < Semantic( BlendIndices ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	float4 vLightStyle : TEXCOORD10;
};

VS
{
	#include "common/vertex.hlsl"

	CreateInputTexture2D( LightstyleTex, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tLightstyleTex < Channel( RGBA, Box( LightstyleTex ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	float GetLightstyleValue(int styleIndex)
	{
		int frame = (int)(g_flTime * 10.0) % 64;
		int row = styleIndex / 4;
		int channel = styleIndex % 4;
		uint2 texelCoords = uint2(frame, row);
		float4 rgba = g_tLightstyleTex.Load(int3(texelCoords, 0));
		float ch = rgba[channel] * 255.0;
		if (ch < 97.0 || ch > 122.0) ch = 109.0;
		return (ch - 97.0) / 12.0;
	}

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		i.vLightStyle[0] = GetLightstyleValue(v.vLightStyles[0]);
		i.vLightStyle[1] = GetLightstyleValue(v.vLightStyles[1]);
		i.vLightStyle[2] = GetLightstyleValue(v.vLightStyles[2]);
		i.vLightStyle[3] = GetLightstyleValue(v.vLightStyles[3]);

		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );

		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/classes/ScreenSpaceAmbientOcclusion.hlsl"
	
	SamplerState g_sSampler0 < Filter( Point ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSampler1 < Filter( Anisotropic ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( Color, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( Fullbright, Linear, 8, "None", "_mask", ",0/,0/0", Default4( 0.00, 0.00, 0.00, 0.00 ) );
	CreateInputTexture2D( LightmapR, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( LightmapG, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	CreateInputTexture2D( LightmapB, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tColor < Channel( RGBA, Box( Color ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tFullbright < Channel( R, Box( Fullbright ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); >;
	Texture2D g_tLightmapR < Channel( RGBA, Box( LightmapR ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLightmapG < Channel( RGBA, Box( LightmapG ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	Texture2D g_tLightmapB < Channel( RGBA, Box( LightmapB ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();
		m.Albedo = float3( 1, 1, 1 );
		m.Normal = float3( 0, 0, 1 );
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Emission = float3( 0, 0, 0 );
		m.Transmission = 0;
		
		float4 albedo = Tex2DS( g_tColor, g_sSampler0, i.vTextureCoords.xy );
		clip( albedo.a - 0.5 );

		float2 l_1 = i.vTextureCoords.zw;

		float4 lmRed = Tex2DS( g_tLightmapR, g_sSampler1, l_1 );
		float4 lmGreen = Tex2DS( g_tLightmapG, g_sSampler1, l_1 );
		float4 lmBlue = Tex2DS( g_tLightmapB, g_sSampler1, l_1 );

		float4 lightStyle = i.vLightStyle;

		float3 style0 = float3( lmRed.x, lmGreen.x, lmBlue.x ) * lightStyle.x;
		float3 style1 = float3( lmRed.y, lmGreen.y, lmBlue.y ) * lightStyle.y;
		float3 style2 = float3( lmRed.z, lmGreen.z, lmBlue.z ) * lightStyle.z;
		float3 style3 = float3( lmRed.w, lmGreen.w, lmBlue.w ) * lightStyle.w;

		float3 lightColor = style0 + style1 + style2 + style3;

		float fullbright = Tex2DS( g_tFullbright, g_sSampler0, i.vTextureCoords.xy ).r;
		float ssao = ScreenSpaceAmbientOcclusion::Sample( i.vPositionSs );
		float3 litColor = albedo.xyz * (lightColor * 2.0);
		float3 finalColor = lerp( litColor * ssao, albedo.xyz * 2, fullbright );

		m.Albedo = albedo.xyz;
		m.Emission = finalColor;
		m.Opacity = 1;
		m.Roughness = 1;
		m.Metalness = 0;
		m.AmbientOcclusion = 1;
		m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
		
		float4 shade = ShadingModelStandard::Shade( i, m );
		
		return shade;
	}
}
