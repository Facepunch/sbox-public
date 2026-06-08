#ifndef DYNAMIC_REFLECTIONS_HLSL
#define DYNAMIC_REFLECTIONS_HLSL

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Accessor for the result of dynamic reflections, whether they are SSR or eventually Raytraced Reflections
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
class DynamicReflections
{
    static float4 Sample(float2 ScreenPosition, float Roughness = 0.0f)
    {
        if (!IsEnabled())
            return 0;

        uint index = Bindless::GetPipelineTextureIndex(PipelineTextureSlotSSR);
        Texture2D ReflectionColor = Bindless::GetTexture2D( index );

        // If the texture has mips, we can sample it at a specific level based on roughness.
        // Eg Planar Reflections with mip chain.
        int2 nDim;
        int nLevels;
        ReflectionColor.GetDimensions(0, nDim.x, nDim.y, nLevels);

        float flLevel = ( Roughness * (nLevels - 1) );

        // Sample the reflection color at the specified screen position and roughness level
        return ReflectionColor.SampleLevel( g_sTrilinearClamp, ScreenPosition * g_vInvViewportSize, flLevel ); // Could probably use SampleScreenSsMSAA
    }

    static bool IsEnabled()
    {
        uint index = Bindless::GetPipelineTextureIndex(PipelineTextureSlotSSR);
        return index != 0;
    }
};


#endif // DYNAMIC_REFLECTIONS_HLSL