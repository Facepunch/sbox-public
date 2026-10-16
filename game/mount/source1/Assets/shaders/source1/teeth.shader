HEADER
{
	Description = "Source 1 model teeth";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_SOURCE_TEETH_BUMP, 0..1, "Source 1" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
	float g_flSourceIllumFactor < Default( 1 ); >;
	float3 g_vSourceTeethForward < Default3( 1, 0, 0 ); >;
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
	float2 vTexCoord2 : TEXCOORD1 < Semantic( LowPrecisionUv1 ); >;
	float4 vNormalOs : NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;
	#if VS_INPUT_HAS_TANGENT_BASIS
		float4 vTangentUOs_flTangentVSign : TANGENT < Semantic( TangentU_SignV ); >;
	#endif
	#if D_CS_VERTEX_ANIMATION
		float4 vBlendWeight : BLENDWEIGHT < Semantic( BlendWeight ); >;
		float nVertexIndex : TEXCOORD14 < Semantic( MorphIndex ); >;
		float nVertexCacheIndex : TEXCOORD15 < Semantic( MorphIndex ); >;
	#endif
	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
	uint nBoneIndex : BLENDINDICES < Semantic( BlendIndices ); >;
	#if D_BAKED_LIGHTING_FROM_LIGHTMAP
		float2 vLightmapUV : TEXCOORD3 < Semantic( LightmapUV ); >;
	#endif
	float4 vMouthForward : TEXCOORD4 < Semantic( None ); >;
	#define COMMON_VS_INPUT_DEFINED
	#define SHARED_STANDARD_VS_INPUT_DEFINED
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float flMouthDarkening : TEXCOORD16;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput o = ProcessVertex( v );
		float3 forward = g_vSourceTeethForward;
		if ( v.vMouthForward.w >= 0 )
			forward = mul( GetTransformMatrix( v.nInstanceTransformID, (uint)v.vMouthForward.w ), float4( v.vMouthForward.xyz, 0 ) );
		o.flMouthDarkening = g_flSourceIllumFactor * saturate( dot( normalize( o.vNormalWs ), forward ) );
		return FinalizeVertex( o );
	}
}

PS
{
	StaticCombo( S_SOURCE_TEETH_BUMP, F_SOURCE_TEETH_BUMP, Sys( ALL ) );
	#define CULL_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	SamplerState g_sSourceTeeth < Attribute( "g_tSourceTeethSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sSourceTeethNormal < Attribute( "g_tSourceTeethNormalSampler" ); Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	CreateInputTexture2D( SourceTeeth, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( SourceTeethNormal, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
	Texture2D g_tSourceTeeth < Channel( RGBA, Box( SourceTeeth ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tSourceTeethNormal < Channel( RGBA, Box( SourceTeethNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
	float g_flSourcePhongExponent < Default( 100 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material material = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		material.Normal = normalize( i.vNormalWs );
		material.WorldTangentU = i.vTangentUWs;
		material.WorldTangentV = i.vTangentVWs;
		material.LightmapUV = i.vLightmapUV;
		material.TextureCoords = i.vTextureCoords.xy;
		float4 base = g_tSourceTeeth.Sample( g_sSourceTeeth, i.vTextureCoords.xy );
		float mouthDarkening = max( i.flMouthDarkening, 0 );
		// Direct diffuse does not use material AO, so darken albedo as well.
		material.Albedo = base.rgb * mouthDarkening;
		material.AmbientOcclusion = saturate( mouthDarkening );
		material.Opacity = base.a;
		material.Roughness = 1;
		#if S_SOURCE_TEETH_BUMP
			float4 normal = g_tSourceTeethNormal.Sample( g_sSourceTeethNormal, i.vTextureCoords.xy );
			material.TangentNormal = normalize( normal.xyz * 2 - 1 );
			material.Normal = normalize( material.TangentNormal.x * i.vTangentUWs
				+ material.TangentNormal.y * i.vTangentVWs + material.TangentNormal.z * i.vNormalWs );
			float exponent = g_flSourcePhongExponent == 0 ? lerp( 1.0, 150.0, normal.a ) : g_flSourcePhongExponent;
			// Standard uses perceptual roughness and fixed dielectric F0; approximate the Phong mask with gloss.
			float roughness = pow( 2.0 / (max( exponent, 0 ) + 2.0), 0.25 );
			material.Roughness = lerp( 1.0, roughness, base.a );
			material.Opacity = 1;
		#endif
		float4 output = ShadingModelStandard::Shade( material );
		return output;
	}
}
