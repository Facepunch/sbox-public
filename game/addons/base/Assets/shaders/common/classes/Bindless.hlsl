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

class Bindless
{
    // everything applies NonUniformResourceIndex because that's what you want 99% of the time
    // we can do uniform variants but only if we prove that they're actually faster in the cases
    static inline Texture2D GetTexture2D( int nIndex, bool srgb = false ){ return g_bindless_Texture2D[NonUniformResourceIndex(nIndex + (srgb ? 1 : 0))]; }
    static inline Texture2DMS<float4> GetTexture2DMS( int nIndex ) { return g_bindless_Texture2DMS[ NonUniformResourceIndex(nIndex) ]; }
    static inline Texture3D GetTexture3D( int nIndex ) { return g_bindless_Texture3D[ NonUniformResourceIndex(nIndex) ]; }
    static inline TextureCube GetTextureCube( int nIndex ) { return g_bindless_TextureCube[ NonUniformResourceIndex(nIndex) ]; }
    static inline Texture2DArray GetTexture2DArray( int nIndex ) { return g_bindless_Texture2DArray[ NonUniformResourceIndex(nIndex) ]; }
    static inline TextureCubeArray GetTextureCubeArray( int nIndex ) { return g_bindless_TextureCubeArray[ NonUniformResourceIndex(nIndex) ]; }

    static inline SamplerState GetSampler( int nIndex ) { return g_bindless_Sampler[ NonUniformResourceIndex(nIndex) ]; }
    static inline SamplerComparisonState GetSamplerComparison( int nIndex ) { return g_bindless_SamplerComparison[ NonUniformResourceIndex(nIndex) ]; }

#if PROGRAM == VFX_PROGRAM_CS
    static inline RWTexture2D<float4> GetRWTexture2D( int nIndex ) { return g_bindless_RWTexture2D[ NonUniformResourceIndex(nIndex) ]; }
    static inline RWTexture3D<float4> GetRWTexture3D( int nIndex ) { return g_bindless_RWTexture3D[ NonUniformResourceIndex(nIndex) ]; }
    static inline RWTexture2DArray<float4> GetRWTexture2DArray( int nIndex ) { return g_bindless_RWTexture2DArray[ NonUniformResourceIndex(nIndex) ]; }
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
