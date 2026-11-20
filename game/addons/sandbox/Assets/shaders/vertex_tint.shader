// Standard PBR material (Material.CommonInputs: color, normal, roughness, metalness, ambient occlusion, tint mask)
// tinted by the mesh's vertex colours where the tint mask is white, so trimsheet props can colour their parts per
// vertex while sharing one material.

HEADER
{
	Description = "Standard PBR, tinted by mesh vertex colour through the tint mask";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
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
		o.vVertexColor.rgb *= SrgbGammaToLinear( i.vColor.rgb );
		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureColor, 			Srgb, 	8, "", "_color", "Material, 10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureNormal, 			Linear, 8, "NormalizeNormals", "_normal", "Material, 10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureNormalDetail, 		Linear, 8, "NormalizeNormals", "_detail", "Material, 10/25", Default3( 0, 0, 1 ) );
	CreateInputTexture2D( TextureRoughness, 		Linear, 8, "", "_rough", "Material, 10/30", Default( 0.5 ) );
	CreateInputTexture2D( TextureMetalness, 		Linear, 8, "", "_metal", "Material, 10/40", Default( 1.0 ) );
	CreateInputTexture2D( TextureAmbientOcclusion, 	Linear, 8, "", "_ao", "Material, 10/50", Default( 1.0 ) );
	CreateInputTexture2D( TextureTintMask, 			Linear, 8, "", "_tint", "Material, 10/70", Default( 1.0 ) );

	Texture2D g_tColor 			< Channel( RGB, Box( TextureColor ), Srgb ); OutputFormat( BC7); SrgbRead( true ); >;
	Texture2D g_tNormal 		< Channel( RGB, Box( TextureNormal ), Linear ); Channel( A, Box( TextureTintMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tNormalDetail 	< Channel( RGB, Box( TextureNormalDetail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tRma 			< Channel( R, Box( TextureRoughness), Linear ); Channel( G, Box( TextureMetalness ), Linear ); Channel( B, Box( TextureAmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float DetailIntensity 	< UiGroup( "Material, 10/26" ); Default( 1 ); Range( 0, 1 ); >; 
	float DetailScale 		< UiGroup( "Material, 10/27" ); Default( 2.5f ); Range( 0.25, 8 ); >;

	float RoughnessScale < UiGroup( "Material, 10/31" ); Default( 1 ); Range( 0, 1 ); >;
	float MetalnessScale < UiGroup( "Material, 10/41" ); Default( 1 ); Range( 0, 1 ); >;

	float3 BlendNormals( float3 n1, float3 n2 )
	{
		float3 t = n1.xyz*float3( 2,  2, 2) + float3(-1, -1,  0);
		float3 u = n2.xyz*float3(-2, -2, 2) + float3( 1,  1, -1);
		float3 r = t*dot(t, u) - u*t.z;

		return normalize(r);
	}

	// Non-decoded normals only
	float3 AdjustNormalIntensity( float3 normal, float intensity )
	{
		normal = normal * 2 - 1;
		normal.xy *= intensity;
		return normal * 0.5 + 0.5; 
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );
		
		float3 vColor = g_tColor.Sample( g_sAniso, i.vTextureCoords.xy ).rgb;
		
        float4 vNormalTs = g_tNormal.Sample( g_sAniso, i.vTextureCoords.xy ).rgba;
        float3 vRMA = g_tRma.Sample( g_sAniso, i.vTextureCoords.xy ).rgb;
		float3 vDetail = g_tNormalDetail.Sample( g_sAniso, i.vTextureCoords.xy * DetailScale ).rgb;
		vDetail = AdjustNormalIntensity( vDetail, DetailIntensity );

		float3 vFinalNormalTs = BlendNormals( vNormalTs.xyz, vDetail );

		m.Albedo = lerp( vColor, vColor * i.vVertexColor.rgb, vNormalTs.a );
		m.Normal = TransformNormal( vFinalNormalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.Roughness = vRMA.r * RoughnessScale;
		m.Metalness = vRMA.g * MetalnessScale;
		m.AmbientOcclusion = vRMA.b;
		m.TintMask = vNormalTs.a;

		return ShadingModelStandard::Shade( i, m );
	}
}
