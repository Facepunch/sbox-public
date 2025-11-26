#ifndef COMMON_PIXEL_MATERIAL_H
#define COMMON_PIXEL_MATERIAL_H

#include "common/utils/normal.hlsl"
class Material
{
    // Material properties
    float3 Albedo;
    float  Metalness;
    float  Roughness;
    float3 Emission;                // Emissive color
    float3 Normal;                  // World normal
    float  TintMask;
    float  AmbientOcclusion;
    float3 Transmission;
    float  Opacity;

    // Geometric properties
    // These should not be in Material definition and might be moved in the future
    float3 WorldPosition;
    float3 WorldPositionWithOffset; // World position relative to the camera
    float4 ScreenPosition;           // SV_Position
    
    // Tangent space and lighting
    float3 TangentNormal;
    float3 WorldTangentU;           // Probably shouldn't be here
    float3 WorldTangentV;           // Probably shouldn't be here
    float2 LightmapUV;              // if D_BAKED_LIGHTING_FROM_LIGHTMAP

    float2 TextureCoords; // if TOOL_VIS

    // DEPRECATED
    float3 GeometricNormal;
    
    /// <summary>
    /// Initialize a material
    /// </summary>
    static Material Init( float3 RelativeWorldPosition = 0.0f, float4 ScreenPosition = 0.0f );
    
    /// <summary>
    /// Lerp between two materials
    /// This is useful for blending materials in the pixel shader
    /// </summary>
    static Material lerp(Material a, Material b, float amount);

#if defined( PixelInput )
    /// <summary>
    /// Initialize a material with the surface properties of from Vertex Shader
    /// </summary>
    static Material Init( PixelInput i );

    /// <summary>
    /// Deprecated helpers if referencing Material.CommonInputs.hlsl
    /// </summary>
    static Material From(PixelInput i);
#endif

};

//--------------------------------------------
// Function implementations
//--------------------------------------------

/// <summary>
/// Initialize a material
/// </summary>
Material Material::Init(float3 RelativeWorldPosition, float4 ScreenPosition )
{
    Material m;

    m.Albedo = float3(1.0, 1.0, 1.0);
    m.Metalness = 0.0;
    m.Roughness = 1.0;
    m.Emission = float3(0.0, 0.0, 0.0);
    m.Normal = float3(0.0, 0.0, 1.0);
    m.TintMask = 1.0;
    m.AmbientOcclusion = 1.0f;
    m.Transmission = float3(0.0, 0.0, 0.0);
    m.Opacity = 1;

    m.WorldPosition = RelativeWorldPosition + g_vHighPrecisionLightingOffsetWs.xyz;
    m.WorldPositionWithOffset = RelativeWorldPosition;
    m.ScreenPosition = ScreenPosition;
    
    m.TangentNormal = float3(0.0, 0.0, 1.0);
    m.WorldTangentU = float3(1.0, 0.0, 0.0);
    m.WorldTangentV = float3(0.0, 1.0, 0.0);
    m.LightmapUV = Blink(0.5f);

    m.TextureCoords = float2(0, 0);

    return m;
}

Material Material::lerp(Material a, Material b, float amount)
{
    Material o = a;
    
    o.Albedo = ::lerp(a.Albedo, b.Albedo, amount);
    o.Emission = ::lerp(a.Emission, b.Emission, amount);
    o.Opacity = ::lerp(a.Opacity, b.Opacity, amount);

    o.TintMask = ::lerp(a.TintMask, b.TintMask, amount);
    
    // no other field is available with the unlit shading model
    o.Normal = normalize(::lerp(a.Normal, b.Normal, amount));
    o.Roughness = ::lerp(a.Roughness, b.Roughness, amount);
    o.Metalness = ::lerp(a.Metalness, b.Metalness, amount);
    o.AmbientOcclusion = ::lerp(a.AmbientOcclusion, b.AmbientOcclusion, amount);
    
    return o;
}

#if defined( PixelInput )

Material Material::Init( PixelInput i )
{
    // Still with this bullshit but much saner
#if defined( CUSTOM_MATERIAL_INPUTS )
    return Material::Init();
#else
    Material m = Material::Init(i.vPositionWithOffsetWs.xyz, i.vPositionSs);

    m.Normal = i.vNormalWs.xyz;
    m.WorldTangentU = i.vTangentUWs;
    m.WorldTangentV = i.vTangentVWs;
    m.LightmapUV = i.vLightmapUV;
    m.TextureCoords = i.vTextureCoords.xy;

    return m;
#endif

}

// "Material::From() requires Material.CommonInputs.hlsl to be included" to properly function
// This is a legacy helper, use Material::Init( PixelInput i ) instead
Material Material::From(PixelInput i)
{
    Material material = Material::Init( i );

    // Last bit of bullshit for legacy support, otherwise same as Material::Init( i )
    #ifdef MATERIAL_COMMON_INPUTS_HLSL
        float4 vColor = g_tColor.Sample(TextureFiltering, i.vTextureCoords.xy);
        float4 vNormalTs = g_tNormal.Sample(TextureFiltering, i.vTextureCoords.xy);
        float4 vRMA = g_tRma.Sample(TextureFiltering, i.vTextureCoords.xy);
        float3 vTintColor = g_flTintColor * i.vVertexColor.rgb;
        float3 vEmission = float3(0.0f, 0.0f, 0.0f); // Default emission value

        material.Albedo = vColor.rgb * vTintColor.rgb;
        material.Opacity = vColor.a;
        material.Metalness = vRMA.g;
        material.Roughness = vRMA.r;
        material.AmbientOcclusion = vRMA.b;
        material.Transmission = vRMA.a;
        material.TintMask = vNormalTs.a;
        material.Normal = TransformNormal(DecodeNormal(vNormalTs.xyz), i.vNormalWs, i.vTangentUWs, i.vTangentVWs);
    #endif

    return material;
}

#endif

#endif // COMMON_PIXEL_MATERIAL_H