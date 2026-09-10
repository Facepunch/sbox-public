#ifndef GBUFFER_H
#define GBUFFER_H
//
// For DepthNormals MODE
//
// Depth Prepass, Normals and Roughness all in one pass. Oh my, we have a G-Buffer now!
//

StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );

struct DepthNormals
{
    static float4 Output( float3 normal, float roughness = 1.0f, float opacity = 1.0f )
    {
        // Remap normal from [-1, 1] to [0, 1]
         normal = 0.5f * ( normal + 1.0f );
         
         #if ( S_ALPHA_TEST )
            // Alphatest outputs alpha-to-coverage, we need to store the opacity in the alpha channel for that
            // Doesn't matter on gbuffer since A2C also implies alpha-to-one when writing to color
            return float4( normal, opacity );
         #endif

         return float4( normal, roughness );
    }

    static bool WantsDepthNormals()
    {
#ifdef S_MODE_DEPTH
        return S_MODE_DEPTH > 0;
#endif
        return false;
    }
};
#endif // GBUFFER_H