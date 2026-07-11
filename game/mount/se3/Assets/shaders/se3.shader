
HEADER
{
	Description = "Serious Engine 3 model material";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Translucent" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define S_UV2 1
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
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/utils/Material.CommonInputs.hlsl"

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );

	float3 UnpackSe3Normal( float2 uv, float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
	{
		float4 t = g_tNormal.Sample( TextureFiltering, uv );

		float3 vNormalTs;
		vNormalTs.x = t.r;
		vNormalTs.y = t.b > 0 ? t.a : t.g;
		vNormalTs.xy = vNormalTs.xy * 2.0 - 1.0;
		vNormalTs.y = -vNormalTs.y;
		vNormalTs.z = sqrt( saturate( 1.0 - dot( vNormalTs.xy, vNormalTs.xy ) ) );

		return TransformNormal( vNormalTs, vNormalWs, vTangentUWs, vTangentVWs );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float4 col = g_tColor.Sample( TextureFiltering, i.vTextureCoords.xy );
		m.Albedo = col.rgb;
		m.Opacity = col.a;

		m.Transmission = 0.0;
		m.Metalness = 0.0;
		m.AmbientOcclusion = 1.0;

		m.Normal = UnpackSe3Normal( i.vTextureCoords.zw, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.TextureCoords = i.vTextureCoords.xy;
		return ShadingModelStandard::Shade( i, m );
	}
}
