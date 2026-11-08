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
    #include "terrain/TerrainSeamlessUV.hlsl"

    Texture2D g_tBCR < Attribute( "BCR" ); SrgbRead( false ); >;
    Texture2D g_tNHO < Attribute( "NHO" ); SrgbRead( false ); >;

    float g_flUVScale < Attribute( "UVScale" ); Default( 1.0 ); >;
    float g_flNormalStrength < Attribute( "NormalStrength" ); Default( 1.0 ); >;
    float g_flMetalness < Attribute( "Metalness" ); Default( 0.0 ); >;
    float g_flRoughness < Attribute( "Roughness" ); Default( 1.0 ); >;
    bool g_bNoTile < Attribute( "NoTile" ); Default( 0 ); >;
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

	float4 MainPs( PixelInput i ) : SV_Target0
	{
        float2 uv = i.vTextureCoords.xy * g_flUVScale;

        // Same per-tile shuffle the terrain applies, so No Tiling shows on the preview
        float2 sampleUV = g_bNoTile ? Terrain_SampleSeamlessUV( uv ) : uv;
        float2 uvDdx = ddx( uv );
        float2 uvDdy = ddy( uv );

        float4 bcr = g_tBCR.SampleGrad( g_sAniso, sampleUV, uvDdx, uvDdy );
        float4 nho = g_tNHO.SampleGrad( g_sAniso, sampleUV, uvDdx, uvDdy );

        // Matches how terrain.shader unpacks a terrain material
        float3 normal = ComputeNormalFromRGTexture( nho.rg );
        normal.xz *= g_flNormalStrength;
        normal = normalize( normal );

        Material m = Material::Init();

        m.Albedo = SrgbGammaToLinear( bcr.rgb );
        m.Normal = TransformNormal( normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
        m.Roughness = bcr.a * g_flRoughness;
        m.Metalness = g_flMetalness;
        m.AmbientOcclusion = nho.a;
        m.TextureCoords = uv;

        // The lighting model derives tangent space from these, terrain.shader fills them too -
        // left at zero the basis is degenerate
        m.WorldTangentU = i.vTangentUWs;
        m.WorldTangentV = i.vTangentVWs;

        return ShadingModelStandard::Shade( i, m );
	}
}
