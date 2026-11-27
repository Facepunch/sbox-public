// moviemaker_alphadivide_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).

#include "sbox/pass.slangh"

Mode( Default );

#if ( PROGRAM == VFX_PROGRAM_CS )
    #include "sbox/post/shared.slangh"

    RWTexture2D<float4> Accumulated    < Attribute("Accumulated"); >;

    [numthreads(16, 16, 1)]
    [shader( "compute" )] void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        float4 sample = Accumulated[DTid.xy].rgba;
        float invAlpha = sample.a > 0.0 ? 1.0 / sample.a : 0.0;
        Accumulated[DTid.xy].rgb *= invAlpha;
    }

#endif // CS

