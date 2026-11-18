// moviemaker_accumulate_cs: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.

#include "sbox/pass.slangh"

Mode( Default );

#if ( PROGRAM == VFX_PROGRAM_CS )
    #include "postprocess/shared.hlsl"

    Texture2DMS<float4> Subframe       < Attribute("Subframe"); >;
    RWTexture2D<float4> Accumulated    < Attribute("Accumulated"); >;

    int SampleCount                    < Attribute("SampleCount"); >;
    float InvFrames                    < Attribute("InvFrames"); >;

    [numthreads(16, 16, 1)]
    [shader( "compute" )] void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        float4 result = 0.0;

        [unroll(16)]
        for (int i = 0; i < SampleCount; i++)
        {
            result += Subframe.Load(DTid.xy, i).rgba;
        }

        Accumulated[DTid.xy].rgba += result * InvFrames;
    }

#endif // CS

