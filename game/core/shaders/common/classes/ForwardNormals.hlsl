#ifndef NORMALS_HLSL
#define NORMALS_HLSL

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Normals and Roughness for Borealis's screen-space reflections, in place of common/classes/Normals.hlsl: the pass modules
// screen_space_reflections_cs.slang and screen_space_reflections_classify_cs.slang include this first. Borealis draws no
// depth-normals prepass. Its opaque pass writes each surface's normal and roughness beside colour while reflections are on
// (sbox/surface_passes.slangh, SurfaceKeepNormals: octahedral normal in red and green, roughness in blue, alpha 1 where
// written), and the trace, which runs before this frame's opaque pass, reads last frame's where this frame's surface was
// then. A pixel nothing wrote there has its normal rebuilt from depth and doesn't reflect.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "common/classes/Bindless.hlsl"
#include "common/classes/Depth.hlsl"

// The opaque pass's target, multisampled as the view (D_MSAA_NORMALS): its first sample, as the trace read the
// depth-normals G-buffer's
#if D_MSAA_NORMALS
    Texture2DMS<float4> g_tNormalsGBuffer < Attribute("NormalsGBuffer"); >;
#else
    Texture2D<float4>   g_tNormalsGBuffer < Attribute("NormalsGBuffer"); >;
#endif

struct Normals
{
    /// <summary>
    /// The opaque pass's texel from last frame where the surface at this screen position was then; zero (nothing written)
    /// off screen.
    /// </summary>
    static float4 Load( int2 screenPos, uint msaaSampleIndex = 0 )
    {
        float3 previous = ReprojectFromLastFrameSs( Depth::GetWorldPosition( screenPos + 0.5 ) );
        int2 texel = int2( floor( previous.xy ) );
        if ( any( texel < 0 ) || any( texel >= int2( g_vViewportSize ) ) || !( previous.z >= 0 && previous.z <= 1 ) )
            return 0;

    #if D_MSAA_NORMALS
        return g_tNormalsGBuffer.Load( texel + g_vViewportOffset, 0 );
    #else
        return g_tNormalsGBuffer.Load( int3( texel + g_vViewportOffset, 0 ) );
    #endif
    }

    /// <summary>
    /// Reconstructs the world-space normal at the given screen position from the depth buffer.
    /// </summary>
    static float3 SampleFromDepth( int2 screenPos )
    {
        float3 posCenter = Depth::GetWorldPosition( screenPos );
        float3 posRight = Depth::GetWorldPosition( screenPos + int2( 1, 0 ) );
        float3 posUp = Depth::GetWorldPosition( screenPos + int2( 0, 1 ) );
        return -normalize( cross( posRight - posCenter, posUp - posCenter ) );
    }

    /// <summary>
    /// The octahedrally encoded normal SurfaceKeepNormals wrote.
    /// </summary>
    static float3 Decode( float2 encoded )
    {
        float2 e = encoded * 2.0 - 1.0;
        float3 n = float3( e, 1.0 - abs( e.x ) - abs( e.y ) );
        float t = saturate( -n.z );
        n.xy += float2( n.x >= 0 ? -t : t, n.y >= 0 ? -t : t );
        return normalize( n );
    }

    /// <summary>
    /// The world-space normal at the given screen position: the surface's, or rebuilt from depth where none was written.
    /// </summary>
    static float3 Sample( int2 screenPos, uint msaaSampleIndex = 0 )
    {
        float4 texel = Normals::Load( screenPos, msaaSampleIndex );
        return texel.a < 0.5 ? Normals::SampleFromDepth( screenPos ) : Normals::Decode( texel.xy );
    }
};

struct Roughness
{
    /// <summary>
    /// The surface's roughness at the given screen position; fully rough, so it doesn't reflect, where none was written.
    /// </summary>
    static float Sample( int2 screenPos, uint msaaSampleIndex = 0 )
    {
        float4 texel = Normals::Load( screenPos, msaaSampleIndex );
        return texel.a < 0.5 ? 1.0 : texel.z;
    }
};
#endif // NORMALS_HLSL
