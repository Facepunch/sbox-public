#ifndef NORMALS_HLSL
#define NORMALS_HLSL

#include "common/GBuffer.hlsl"
#include "common/classes/Bindless.hlsl"
#include "common/classes/Depth.hlsl"

int NormalsTextureIndex < Attribute("NormalsTextureIndex"); Default(-1); >;

struct Normals
{
    /// <summary>
    /// Loads the depth-normals G-buffer texel. It has the view's sample count, and a single-sampled one can't be read as Texture2DMS.
    /// </summary>
    static float4 Load(int2 screenPos, uint msaaSampleIndex = 0 )
    {
        if ( g_nMSAASampleCount > 1 )
            return Bindless::GetTexture2DMS( asDynamicUniform( UniformIndex( NormalsTextureIndex ) ) ).Load( screenPos + g_vViewportOffset, msaaSampleIndex );
        return Bindless::GetTexture2D( asDynamicUniform( UniformIndex( NormalsTextureIndex ) ) ).Load( int3( screenPos + g_vViewportOffset, 0 ) );
    }

    /// <summary>
    /// Reconstructs the world-space normal at the given screen position from the depth buffer.
    /// </summary>
    static float3 SampleFromDepth(int2 screenPos)
    {
        float3 posCenter = Depth::GetWorldPosition(screenPos);
        float3 posRight = Depth::GetWorldPosition(screenPos + int2(1, 0));
        float3 posUp = Depth::GetWorldPosition(screenPos + int2(0, 1));

        float3 dx = posRight - posCenter;
        float3 dy = posUp - posCenter;

        return -normalize(cross(dx, dy));
    }

    /// <summary>
    /// Samples the world-space normal at the given screen position.
    /// </summary>
    static float3 Sample(int2 screenPos, uint msaaSampleIndex = 0 )
    {
        // Reconstruct normal from depth buffer if depth normals are not available
        if (NormalsTextureIndex == -1)
            return Normals::SampleFromDepth(screenPos);

        // Load normals from DepthNormals G-buffer
        float3 normals = Normals::Load( screenPos, msaaSampleIndex ).xyz;

        // Rebuild from depth if the normal is invalid
        if( all( normals == 0 ) )
            return Normals::SampleFromDepth(screenPos);
        
        // Convert from [0, 1] to [-1, 1]
        return ( 2.0f * normals - 1.0f );
    }
};

struct Roughness
{
    static float Sample(int2 screenPos, uint msaaSampleIndex = 0 )
    {
        // No G-buffer: fully rough, so nothing traces reflections
        if (NormalsTextureIndex == -1)
            return 1.0f;

        // Load roughness from DepthNormals G-buffer
        return Normals::Load( screenPos, msaaSampleIndex ).w;
    }
};
#endif // NORMALS_HLSL