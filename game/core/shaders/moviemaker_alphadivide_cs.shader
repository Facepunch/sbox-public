MODES
{
    Default();
}

CS
{
    #include "postprocess/shared.hlsl"

    RWTexture2D<float4> Accumulated    < Attribute("Accumulated"); >;

    [numthreads(16, 16, 1)]
    void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        float4 sample = Accumulated[DTid.xy].rgba;
        float invAlpha = sample.a > 0.0 ? 1.0 / sample.a : 0.0;
        Accumulated[DTid.xy].rgb *= invAlpha;
    }
}
