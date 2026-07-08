#ifndef AMBIENTOCCLUSION_HLSL
#define AMBIENTOCCLUSION_HLSL

#include "common/utils/MSAAUtils.hlsl"

class ScreenSpaceAmbientOcclusion
{
    // Samples ambient occlusion texture at the given screen position
    // Does depth comparison to find the best sample in MSAA
    static float Sample( float4 ScreenPosition )
    {
        uint index = Bindless::GetPipelineTextureIndex(PipelineTextureSlotAO);

        if ( index == 0 )
            return 1.0f; // Ambient occlusion is disabled

        Texture2D tAO = Bindless::GetTexture2D( index );

        return MSAAUtils::SampleRed( tAO, ScreenPosition );
    }
};

#endif //AMBIENTOCCLUSION_HLSL