MODES
{
    Default();
}

CS
{
    #include "postprocess/shared.hlsl"

    Texture2DMS<float4> Subframe       < Attribute("Subframe"); >;
    RWTexture2D<float4> Accumulated    < Attribute("Accumulated"); >;

    float InvFrames                    < Attribute("InvFrames"); >;

    [numthreads(16, 16, 1)]
    void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        float4 sample = Subframe.Load(DTid.xy, DTid.z).rgba;
        Accumulated[DTid.xy].rgba += sample * InvFrames;
    }
}
