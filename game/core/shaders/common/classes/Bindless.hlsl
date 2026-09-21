#ifndef BINDLESS_H
#define BINDLESS_H

Texture2D g_bindless_Texture2D[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );
Texture2DMS<float4> g_bindless_Texture2DMS[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );
Texture3D g_bindless_Texture3D[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );
TextureCube g_bindless_TextureCube[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );
Texture2DArray g_bindless_Texture2DArray[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );
TextureCubeArray g_bindless_TextureCubeArray[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 16 );

SamplerState g_bindless_Sampler[2048] EXTERNAL_DESC_SET( s, g_globalLateBoundBindlessSet, 15 );
SamplerComparisonState g_bindless_SamplerComparison[2048] EXTERNAL_DESC_SET( s, g_globalLateBoundBindlessSet, 15 );

#if PROGRAM == VFX_PROGRAM_CS
RWTexture2D<float4> g_bindless_RWTexture2D[] EXTERNAL_DESC_SET( u, g_globalLateBoundBindlessSet, 16 );
RWTexture3D<float4> g_bindless_RWTexture3D[] EXTERNAL_DESC_SET( u, g_globalLateBoundBindlessSet, 16 );
RWTexture2DArray<float4> g_bindless_RWTexture2DArray[] EXTERNAL_DESC_SET( u, g_globalLateBoundBindlessSet, 16 );
#endif

// Stable, pipeline-level texture slots. These are full-screen resources produced once per frame
// by procedural layers (AO, SSR). Rather than route a dynamic bindless index through the racy
// per-view render attributes, the scene system publishes the indices in a single small structured
// buffer at a fixed binding. The buffer persists frame-to-frame, so consumers automatically read
// last frame's result if a producer is skipped. Binding MUST match RENDER_GLOBAL_BINDING_PIPELINE_TEX_INDICES
// in renderdevicetypes.h
StructuredBuffer<int> g_PipelineTextureIndices EXTERNAL_DESC_SET(t, g_globalLateBoundBindlessSet, 14);

enum PipelineTextureSlot
{
    PipelineTextureSlotAO = 0,
    PipelineTextureSlotSSR = 1
};

// An explicitly uniform descriptor index. Accessors validate the complete value at the point of use.
struct UniformIndex
{
    int index;
};

//
// Sampled textures support non-uniform indices by default in every shader stage:
//
//   Bindless::GetTexture2D( i )                 Safe for per-instance/per-pixel indices, including clustered
//                                               light lists, terrain layers and per-pixel cascade selection.
//
//   Bindless::GetTexture2D( UniformIndex( i ) ) Explicit fast path for a uniform index, such as a per-draw or
//                                               dispatch constant.
//
// GetTexture2DSrgb has the same overloads and selects the paired sRGB view with a fixed +1 offset.
//
// UniformIndex is a checked promise, not a broadcast. A false promise can select the wrong descriptor.
// Leave uncertain indices on the default path; use asDynamicUniform only for documented guarantees
// that the compiler cannot establish.
//
struct Bindless
{
    static inline Texture2D GetTexture2D( int nIndex ) { return g_bindless_Texture2D[ NonUniformResourceIndex( nIndex ) ]; }
    static inline Texture2DMS<float4> GetTexture2DMS( int nIndex ) { return g_bindless_Texture2DMS[ NonUniformResourceIndex( nIndex ) ]; }
    static inline Texture3D GetTexture3D( int nIndex ) { return g_bindless_Texture3D[ NonUniformResourceIndex( nIndex ) ]; }
    static inline TextureCube GetTextureCube( int nIndex ) { return g_bindless_TextureCube[ NonUniformResourceIndex( nIndex ) ]; }
    static inline Texture2DArray GetTexture2DArray( int nIndex ) { return g_bindless_Texture2DArray[ NonUniformResourceIndex( nIndex ) ]; }
    static inline TextureCubeArray GetTextureCubeArray( int nIndex ) { return g_bindless_TextureCubeArray[ NonUniformResourceIndex( nIndex ) ]; }

    static inline Texture2D GetTexture2D( dynamic_uniform UniformIndex nIndex ) { return g_bindless_Texture2D[ nIndex.index ]; }
    static inline Texture2DMS<float4> GetTexture2DMS( dynamic_uniform UniformIndex nIndex ) { return g_bindless_Texture2DMS[ nIndex.index ]; }
    static inline Texture3D GetTexture3D( dynamic_uniform UniformIndex nIndex ) { return g_bindless_Texture3D[ nIndex.index ]; }
    static inline TextureCube GetTextureCube( dynamic_uniform UniformIndex nIndex ) { return g_bindless_TextureCube[ nIndex.index ]; }
    static inline Texture2DArray GetTexture2DArray( dynamic_uniform UniformIndex nIndex ) { return g_bindless_Texture2DArray[ nIndex.index ]; }
    static inline TextureCubeArray GetTextureCubeArray( dynamic_uniform UniformIndex nIndex ) { return g_bindless_TextureCubeArray[ nIndex.index ]; }

    // Textures are registered in linear/sRGB pairs. Apply the offset before descriptor indexing.
    static inline Texture2D GetTexture2DSrgb( int nIndex ) { return GetTexture2D( nIndex + 1 ); }
    static inline Texture2D GetTexture2DSrgb( dynamic_uniform UniformIndex nIndex ) { return GetTexture2D( UniformIndex( nIndex.index + 1 ) ); }

    // Samplers can't take NonUniformResourceIndex unconditionally - it crashes AMD RDNA 1/2 drivers
    static inline SamplerState GetSampler( dynamic_uniform int nIndex ) { return g_bindless_Sampler[ nIndex ]; }
    static inline SamplerComparisonState GetSamplerComparison( dynamic_uniform int nIndex ) { return g_bindless_SamplerComparison[ nIndex ]; }
    static inline SamplerState GetSamplerNonUniform( int nIndex ) { return g_bindless_Sampler[ NonUniformResourceIndex( nIndex ) ]; }
    static inline SamplerComparisonState GetSamplerComparisonNonUniform( int nIndex ) { return g_bindless_SamplerComparison[ NonUniformResourceIndex( nIndex ) ]; }

    static inline int GetPipelineTextureIndex( dynamic_uniform PipelineTextureSlot slot )
    {
        // Each fixed slot is published once before rendering; every invocation reads the same index.
        return asDynamicUniform( g_PipelineTextureIndices[slot] );
    }

#if PROGRAM == VFX_PROGRAM_CS
    // No non-uniform variants: storage images are a separate Vulkan capability
    // (shaderStorageImageArrayNonUniformIndexing) and we don't enable it - see renderdevicevulkan.cpp.
    // Keep UAV indices wave-uniform.
    static inline RWTexture2D<float4> GetRWTexture2D( dynamic_uniform int nIndex ) { return g_bindless_RWTexture2D[ nIndex ]; }
    static inline RWTexture3D<float4> GetRWTexture3D( dynamic_uniform int nIndex ) { return g_bindless_RWTexture3D[ nIndex ]; }
    static inline RWTexture2DArray<float4> GetRWTexture2DArray( dynamic_uniform int nIndex ) { return g_bindless_RWTexture2DArray[ nIndex ]; }
#endif
};

// Keep these for now but don't use them or document them
#define GetBindlessTexture2D( nIndex ) g_bindless_Texture2D[ nIndex ]
#define GetBindlessTexture2DMS( nIndex ) g_bindless_Texture2DMS[ nIndex ]
#define GetBindlessTexture3D( nIndex ) g_bindless_Texture3D[ nIndex ]
#define GetBindlessTextureCube( nIndex ) g_bindless_TextureCube[ nIndex ]
#define GetBindlessTexture2DArray( nIndex ) g_bindless_Texture2DArray[ nIndex ]
#define GetBindlessTextureCubeArray( nIndex ) g_bindless_TextureCubeArray[ nIndex ]
#define GetBindlessSampler( nIndex ) g_bindless_Sampler[ nIndex ]
#define GetBindlessSamplerComparison( nIndex ) g_bindless_SamplerComparison[ nIndex ]

#if PROGRAM == VFX_PROGRAM_CS
#define GetBindlessRWTexture2D( nIndex ) g_bindless_RWTexture2D[ nIndex ]
#define GetBindlessRWTexture3D( nIndex ) g_bindless_RWTexture3D[ nIndex ]
#define GetBindlessRWTexture2DArray( nIndex ) g_bindless_RWTexture2DArray[ nIndex ]
#endif

#endif /* BINDLESS_H */
