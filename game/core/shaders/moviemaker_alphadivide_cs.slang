// moviemaker_alphadivide_cs.shader as a pass module: Basalt.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// MODES: Default();

#include "sbox/pass.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
    #include "postprocess/shared.hlsl"

    RWTexture2D<float4> Accumulated    < Attribute("Accumulated"); >;

    [numthreads(16, 16, 1)]
    [shader( "compute" )] void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        float4 sample = Accumulated[DTid.xy].rgba;
        float invAlpha = sample.a > 0.0 ? 1.0 / sample.a : 0.0;
        Accumulated[DTid.xy].rgb *= invAlpha;
    }

#endif // CS

