//
// Borealis's test material for the Slang material tier, written as today's .shader: the same material as the surface in
// engine/Launcher/BorealisLab/Surfaces/rock.slang (colour, normal map, roughness-metalness-occlusion, tint, roughness
// scale), lit by ShadingModelStandard. borealislab -surface-parity draws the two side by side and compares them.
//
HEADER
{
	Description = "Borealis surface test: rock.slang as a .shader";
	DevShader = true;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
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
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureRma, Linear, 8, "", "_rma", "Roughness,10/30", Default3( 0.5, 0.0, 1.0 ) );

	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRma < Channel( RGBA, Box( TextureRma ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float3 g_vTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Color,10/40" ); >;
	float g_flRoughnessScale < Default( 1.0 ); Range( 0.0, 2.0 ); UiGroup( "Roughness,10/50" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv );
		float3 rma = g_tRma.Sample( g_sAniso, uv ).rgb;
		float2 n = g_tNormal.Sample( g_sAniso, uv ).xy * 2 - 1;
		float3 tangentNormal = float3( n, sqrt( saturate( 1 - dot( n, n ) ) ) );

		Material m = Material::Init( i );
		m.Albedo = color.rgb * g_vTint * i.vVertexColor.rgb;
		m.Opacity = color.a;
		m.Roughness = saturate( rma.r * g_flRoughnessScale );
		m.Metalness = rma.g;
		m.AmbientOcclusion = rma.b;
		m.Normal = normalize( tangentNormal.x * i.vTangentUWs + tangentNormal.y * i.vTangentVWs + tangentNormal.z * normalize( i.vNormalWs.xyz ) );
		return ShadingModelStandard::Shade( i, m );
	}
}
