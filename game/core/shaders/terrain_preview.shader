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
    #include "common/shared.hlsl"

    Texture2D g_tBCR < Attribute( "BCR" ); SrgbRead( false ); >;
    Texture2D g_tNHO < Attribute( "NHO" ); SrgbRead( false ); >;
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
        float4 bcr = g_tBCR.Sample( g_sAniso, i.vTextureCoords.xy );
        float4 nho = g_tNHO.Sample( g_sAniso, i.vTextureCoords.xy );

        Material m = Material::Init();

        m.Albedo = SrgbGammaToLinear( bcr.rgb );
        m.Normal = TransformNormal( ComputeNormalFromRGTexture( nho.rg ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
        m.Roughness = bcr.a;
        m.AmbientOcclusion = nho.a;

        return ShadingModelStandard::Shade( i, m );
	}
}