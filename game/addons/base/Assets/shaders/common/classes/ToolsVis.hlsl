#ifndef TOOLVIS_HLSL
#define TOOLVIS_HLSL

#include "ambient_cube.fxc"
#include "common/BRDF.hlsl"
#include "common/lightbinner.hlsl"

#ifdef S_MODE_TOOLS_VIS
#error "Don't define S_MODE_TOOLS_VIS in your shader anymore. Remove ToolsVis() MODE and StaticCombo, it's all handled better now."
#endif

enum ToolsVisMode
{
    // Lighting
    Fullbright = 1,      // mat_fullbright 1 from Source
    DiffuseLighting = 2, // mat_fullbright 2 from Source
    SpecularLighting = 3,
    TransmissiveLighting = 4,
    LightingComplexity = 5,
    ShowUVs = 6,
    IndexedLightingCount = 7,

    // Shading
    Albedo = 10,
    Reflectivity = 11,
    Roughness = 12,
    Reflectance = 13,
    DiffuseAmbientOcclusion = 14,
    SpecularAmbientOcclusion = 15,
    ShaderIdColor = 16,
    CubemapReflections = 17,

    // Normals/Tangents
    NormalTs = 20,
    NormalWs = 21,
    TangentUWs = 22,
    TangentVWs = 23,
    BentNormalWs = 25,

    // Shading Terms
    GeometricRoughness = 30,
    Curvature = 31,
    EyeAndMouthMask = 32,
    Wrinkle = 33,

    // Facepunch Pipeline
    TiledRenderingColors = 50,
    TiledRenderingShadingComplexity = 51,

    // Flat color overlay specified by the scene objects
    ObjectFlatOverlayColor = 60,

    // Visualization of mip used as fraction of loaded
    TextureMipUsage = 70
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

int 	ToolsVisMode 		< Attribute("ToolsVisMode"); Default(0); > ;
float3 	FlatOverlayColor 	< Attribute("FlatOverlayColor"); Default3(1.0, 0.0, 1.0); > ;
float4 	ShaderIDColor 		< Source(ShaderIDColor); > ;
float4 	ShadingComplexity 	< Source(ShadingComplexity); > ;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
class ToolsVis
{
    // The actual lighting result
    float3 DiffuseLighting;
    float3 SpecularLighting;
    float3 IndirectDiffuseLighting;
    float3 IndirectSpecularLighting;
    float3 TransmissiveLighting;

    static ToolsVis Init( inout float4 vColor, float3 DiffuseLighting, float3 SpecularLighting, float3 IndirectDiffuseLighting, float3 IndirectSpecularLighting, float3 TransmissiveLighting)
    {
        vColor.rgb = 0.1f; // Initialize

        ToolsVis vis;
        vis.DiffuseLighting = DiffuseLighting;
        vis.SpecularLighting = SpecularLighting;
        vis.IndirectDiffuseLighting = IndirectDiffuseLighting;
        vis.IndirectSpecularLighting = IndirectSpecularLighting;
        vis.TransmissiveLighting = TransmissiveLighting;
        return vis;
    }

    static bool WantsToolsVis()
    {
        return ToolsVisMode != 0;
    }

    void HandleFlatOverlayColor(float3 vAlbedo, inout float4 vColor);
    void HandleFullbright(inout float4 vColor, float3 vAlbedo, float3 vPositionWs, float3 vNormalWs);
    void HandleDiffuseLighting(inout float4 vColor);
    void HandleSpecularLighting(inout float4 vColor);
    void HandleTransmissiveLighting(inout float4 vColor);
    void HandleLightingComplexity(inout float4 vColor, uint nNumLights);
    void HandleLightingComplexity(inout float4 vColor, float3 WorldPosition, float3 Normal);
    void HandleAlbedo(inout float4 vColor, float3 vAlbedo);
    void HandleReflectivity(inout float4 vColor, float3 vAlbedo);
    void HandleRoughness(inout float4 vColor, float2 vRoughness);
    void HandleReflectance(inout float4 vColor, float3 vReflectance);
    void HandleDiffuseAmbientOcclusion(inout float4 vColor, float3 vDiffuseAmbientOcclusion);
    void HandleSpecularAmbientOcclusion(inout float4 vColor, float3 vSpecularAmbientOcclusion);
    void HandleShaderIDColor(inout float4 vColor);
    void HandleCubemapReflections(inout float4 vColor, float3 WorldPosition, float3 WorldNormal);
    void HandleNormalTs(inout float4 vColor, float3 vNormalTs);
    void HandleNormalWs(inout float4 vColor, float3 vNormalWs);
    void HandleTangentUWs(inout float4 vColor, float3 vTangentWs);
    void HandleTangentVWs(inout float4 vColor, float3 vTangentWs);
    void HandleBentNormalWs(inout float4 vColor, float3 vBentNormalWs);
    void HandleGeometricRoughness(inout float4 vColor, float3 vGeometricNormalWs);
    void HandleCurvature(inout float4 vColor, float flCurvature);
    static void HandleEyeAndMouthMask(inout float4 vColor, float flEyeAndMouthMask);
    void HandleWrinkle(inout float4 vColor, float flWrinkle);
    void HandleTiledRenderingColors(inout float4 vColor, float3 vAlbedo, float3 WorldPosition);
    void ShadingComplexity(inout float4 vColor, float4 vComplexity);
    void ShowUVs(inout float4 vColor, float3 vAlbedo, Texture2D tex, float2 flUVs);
    void ShowMipUtilization(inout float4 vColor, float3 vAlbedo, Texture2D tex, float2 flUVs);
    void HandleBakedLight(inout float4 vColor,
                          float3 BakedLightingPrimaryDirection,
                          float3 BakedLightingPrimaryColor,
                          float3 BakedLightingAmbientColor,
                          float3 BakedLightingAmbientOcclusion,
                          float3 BakedLightingResult,
                          float3 BakedLightingDiffuse,
                          float3 BakedLightingSpecular,
                          float3 BakedLightingSpecularWithProbes,
                          float3 BakedLightingChartColor,
                          float BakedLightingLightCount); // Wat da fuk
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleFlatOverlayColor(float3 vAlbedo, inout float4 vColor)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::ObjectFlatOverlayColor)
    {
        float flBright = max(0.2, (vAlbedo.r + vAlbedo.g + vAlbedo.b) / 3);
        vColor.rgb = FlatOverlayColor * flBright;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleFullbright(inout float4 vColor, float3 vAlbedo, float3 vPositionWs, float3 vNormalWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Fullbright)
    {
        float3 vPositionToCameraDirWs = CalculatePositionToCameraDirWs(vPositionWs.xyz);
        float flVDotN = saturate(dot(vNormalWs.xyz, vPositionToCameraDirWs.xyz));

        float flAmbientCubeAmount = lerp(0.3, 1.0, flVDotN);
        float flAmbientCubeFactor = EvalGreyAmbientCube(vNormalWs.xyz, float3(0.6, 0.4, 1.0), float3(0.6, 0.4, 0.2));

        float3 vReflectionVectorWs = reflect(-vPositionToCameraDirWs.xyz, vNormalWs.xyz);
        float flHeadlightSpec = saturate(dot(vPositionToCameraDirWs.xyz, vReflectionVectorWs.xyz));
        flHeadlightSpec = 0.05 * pow(flHeadlightSpec, 4.0);

        vColor.rgb = vAlbedo.rgb * flAmbientCubeFactor * flAmbientCubeAmount + flHeadlightSpec.xxx;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleDiffuseLighting(inout float4 vColor)
{
#if (!S_UNLIT)
    {
        [flatten]
        if (ToolsVisMode == ToolsVisMode::DiffuseLighting)
        {
            vColor.rgb = DiffuseLighting.rgb + IndirectDiffuseLighting.rgb;
            vColor.rgb *= 0.5;
            vColor.rgb *= g_flToneMapScalarLinear;
        }
    }
#endif
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleSpecularLighting(inout float4 vColor)
{
#if (!S_UNLIT)
    {
        [flatten]
        if (ToolsVisMode == ToolsVisMode::SpecularLighting)
        {
            vColor.rgb = SpecularLighting.rgb + IndirectSpecularLighting.rgb;
            vColor.rgb *= g_flToneMapScalarLinear;
        }
    }
#endif
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleTransmissiveLighting(inout float4 vColor)
{
#if (!S_UNLIT)
    {
        [flatten]
        if (ToolsVisMode == ToolsVisMode::TransmissiveLighting)
        {
            vColor.rgb = TransmissiveLighting.rgb;
            vColor.rgb *= g_flToneMapScalarLinear;
        }
    }
#endif
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleLightingComplexity(inout float4 vColor, uint nNumLights)
{
#if (!S_UNLIT)
    {
        [flatten]
        if (ToolsVisMode == ToolsVisMode::LightingComplexity)
        {
            if (nNumLights < 1)
                vColor.rgb = float3(0.0, 0.0, 0.0);
            else if (nNumLights < 2)
                vColor.rgb = float3(0.0, 1.0, 0.0);
            else if (nNumLights < 3)
                vColor.rgb = float3(0.5, 1.0, 0.0);
            else if (nNumLights < 4)
                vColor.rgb = float3(1.0, 1.0, 0.0);
            else if (nNumLights < 5)
                vColor.rgb = float3(1.0, 0.5, 0.0);
            else
                vColor.rgb = float3(1.0, 0.0, 0.0);
        }
        [flatten]
        if (ToolsVisMode == ToolsVisMode::IndexedLightingCount )
        {
            vColor.rgb = float3(0.0f, 0.0f, 0.0f);
            if (nNumLights == 1)
                vColor.rgb = float3(1.0f, 0.0f, 0.0f);
            if (nNumLights == 2)
                vColor.rgb = float3(1.0f, 0.5f, 0.0f);
            if (nNumLights == 3)
                vColor.rgb = float3(1.0f, 1.0f, 0.0f);
            if (nNumLights == 4)
                vColor.rgb = float3(1.0f, 1.0f, 1.0f);
            if (nNumLights >= 5)
                vColor.rgb = Blink( 0.5f ) ? float3( 1.0f, 0.0f, 1.0f ) : float3(0.0f, 1.0f, 1.0f);
        }
    }
#endif
}

void ToolsVis::HandleLightingComplexity(inout float4 vColor, float3 WorldPosition, float3 Normal)
{
    uint nNumLights = 0;

    ClusterRange lightRange = Cluster::Query( ClusterItemType_Light, WorldPosition );

    for (uint index = 0; index < lightRange.Count; index++)
    {
        uint lightIndex = Cluster::LoadItem( lightRange, index );
        DynamicLight light;
        light.Init( WorldPosition, DynamicLightConstantByIndex( lightIndex ) );

        if (ToolsVisMode == ToolsVisMode::IndexedLightingCount && !light.LightData.IsIndexedLight() )
            continue;
        
        if (light.Visibility > 0.0f && light.Attenuation > 0.0f && dot(light.Direction, Normal) > 0.0f)
            nNumLights++;
    }

    HandleLightingComplexity(vColor, nNumLights);
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleAlbedo(inout float4 vColor, float3 vAlbedo)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Albedo)
    {
        vColor.rgb = vAlbedo.rgb;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleReflectivity(inout float4 vColor, float3 vAlbedo)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Reflectivity)
    {
        float flReflectivity = dot(vAlbedo.rgb, float3(0.3, 0.59, 0.11));
        vColor.rgb = (floor(flReflectivity * 20.0 + 0.5) * (1.0 / 20.0)).xxx;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleRoughness(inout float4 vColor, float2 vRoughness)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Roughness)
    {
#if (S_ANISOTROPIC_GLOSS)
        {
            vColor.rg = vRoughness.xy;
            vColor.b = 0.0;
        }
#else
        {
            vColor.rgb = IsotropicRoughnessFromAnisotropicRoughness(vRoughness.xy).xxx;
        }
#endif

        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleReflectance(inout float4 vColor, float3 vReflectance)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Reflectance)
    {
        vColor.rgb = vReflectance.xyz;
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleDiffuseAmbientOcclusion(inout float4 vColor, float3 vDiffuseAmbientOcclusion)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::DiffuseAmbientOcclusion)
    {
        vColor.rgb = SrgbGammaToLinear(vDiffuseAmbientOcclusion.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleSpecularAmbientOcclusion(inout float4 vColor, float3 vSpecularAmbientOcclusion)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::SpecularAmbientOcclusion)
    {
        vColor.rgb = SrgbGammaToLinear(vSpecularAmbientOcclusion.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleShaderIDColor(inout float4 vColor)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::ShaderIdColor)
    {
        vColor.rgb = ShaderIDColor.rgb;
        vColor.a = 1.0;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleCubemapReflections(inout float4 vColor, float3 WorldPosition, float3 WorldNormal)
{
    [flatten]
    if ( ToolsVisMode == ToolsVisMode::CubemapReflections )
    {
        vColor.rgb = EnvMap::From(WorldPosition, WorldNormal);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleNormalTs(inout float4 vColor, float3 vNormalTs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::NormalTs)
    {
        vNormalTs.y = -vNormalTs.y;
        vColor.rgb = PackToColor(vNormalTs.xyz);
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleNormalWs(inout float4 vColor, float3 vNormalWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::NormalWs)
    {
        vColor.rgb = PackToColor(vNormalWs.xyz);
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleTangentUWs(inout float4 vColor, float3 vTangentWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::TangentUWs)
    {
        vColor.rgb = PackToColor(vTangentWs.xyz);
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleTangentVWs(inout float4 vColor, float3 vTangentWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::TangentVWs)
    {
        vColor.rgb = PackToColor(vTangentWs.xyz);
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleBentNormalWs(inout float4 vColor, float3 vBentNormalWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::BentNormalWs)
    {
        vColor.rgb = PackToColor(vBentNormalWs.xyz);
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleGeometricRoughness(inout float4 vColor, float3 vGeometricNormalWs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::GeometricRoughness)
    {
        vColor.rgb = CalculateGeometricRoughnessFactor(vGeometricNormalWs.xyz).xxx;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleCurvature(inout float4 vColor, float flCurvature)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Curvature)
    {
        vColor.rgb = flCurvature.xxx;
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleEyeAndMouthMask(inout float4 vColor, float flEyeAndMouthMask)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::EyeAndMouthMask)
    {
        vColor.rgb = flEyeAndMouthMask.xxx;
        vColor.rgb = SrgbGammaToLinear(vColor.rgb);
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleWrinkle(inout float4 vColor, float flWrinkle)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::Wrinkle)
    {
#if (S_WRINKLE)
        {
            vColor.rgb = lerp(float3(0.0, 1.0, 1.0), float3(1.0, 0.0, 0.0), saturate(0.5 * flWrinkle + 0.5));
        }
#endif
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::HandleTiledRenderingColors(inout float4 vColor, float3 vAlbedo, float3 WorldPosition)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::TiledRenderingColors)
    {
        ClusterRange lightRange = Cluster::Query( ClusterItemType_Light, WorldPosition );
        ClusterRange envRange = Cluster::Query( ClusterItemType_EnvMap, WorldPosition );
        ClusterRange decalRange = Cluster::Query( ClusterItemType_Decal, WorldPosition );

        float lightRatio = lightRange.Count / 8.0f;
        float envRatio = envRange.Count / 4.0f;

        float3 vClusterColor = lerp( float3( 0.0f, 1.0f, 0.0f ), float3( 1.0f, 0.0f, 0.0f ), lightRatio );

        vColor.rgb = vClusterColor * vAlbedo;
        vColor.b += envRatio;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::ShowUVs(inout float4 vColor, float3 vAlbedo, Texture2D tex, float2 flUVs)
{
    [flatten]
    if (ToolsVisMode == ToolsVisMode::ShowUVs)
    {
        vColor.rgb = vAlbedo;

        uint2 vDims = TextureDimensions2D(tex, 0);

        uint testVal = ((flUVs.x < 0) != (flUVs.y < 0)) ? 0 : 1;
        uint2 vUVInPixels = uint2(abs(flUVs) * vDims.xy);
        if (((vUVInPixels.x + vUVInPixels.y) & 1) == testVal)
        {
            vColor.rgb *= 0.8;
        }

        uint2 vUVIn16Pixels = vUVInPixels / 16;
        if (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal)
        {
            vColor.rgb *= 0.5;
        }
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::ShadingComplexity(inout float4 vColor, float4 vComplexity)
{
    if (ToolsVisMode == ToolsVisMode::TiledRenderingShadingComplexity)
    {
        vColor.rgb = vComplexity.rgb;
    }
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
void ToolsVis::ShowMipUtilization(inout float4 vColor, float3 vAlbedo, Texture2D tex, float2 flUVs)
{
    [branch]
    if (ToolsVisMode == ToolsVisMode::TextureMipUsage)
    {
        vColor.rgb = vAlbedo;

        int2 vTexDimensions = TextureDimensions2DS(tex, 0);
        float flMipLevel = tex.CalculateLevelOfDetail(g_sTrilinearWrap, flUVs);
        float flMipLevels = log2(max(vTexDimensions.x, vTexDimensions.y));

        uint testVal = ((flUVs.x < 0) != (flUVs.y < 0)) ? 0 : 1;
        uint2 vUVInPixels = uint2(abs(flUVs) * vTexDimensions.xy);
        uint2 vUVIn16Pixels = vUVInPixels / (8 * round(1 + flMipLevel));

        float fIntensity = (((vUVIn16Pixels.x + vUVIn16Pixels.y) & 1) == testVal) ? .75 : .25f;
        vColor.rgb = lerp(vColor.rgb, float3(1.0f, 0.0f, 0.0f), fIntensity);
    }
}

#endif
