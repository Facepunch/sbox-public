HEADER
{
    Description = "CMAA2 color composite";
}

MODES
{
    Default();
    Forward();
}

COMMON
{
    #include "postprocess/shared.hlsl"
}

struct VertexInput
{
    float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
    float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
    float2 uv : TEXCOORD0;
    float4 vPositionSs : SV_Position;
};

VS
{
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionSs = float4( i.vPositionOs.xyz, 1.0f );
        o.uv = i.vTexCoord;
        return o;
    }
}

PS
{
    #include "postprocess/common.hlsl"

    SamplerState PointClamp < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
    Texture2D<float4> Cmaa2Color < Attribute( "Cmaa2Color" ); SrgbRead( true ); >;

    RenderState( ColorWriteEnable0, RGBA );
    RenderState( BlendEnable, false );
    RenderState( DepthEnable, false );
    RenderState( DepthWriteEnable, false );

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        return Cmaa2Color.SampleLevel( PointClamp, i.uv, 0.0f );
    }
}
