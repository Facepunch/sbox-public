// moviemaker_accumulate_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// MODES: Default();

#include "sbox/pass.slangh"

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

