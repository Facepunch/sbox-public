#ifndef BRDF_HLSL
#define BRDF_HLSL

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// BRDF (Bidirectional Reflectance Distribution Function)
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// A BRDF is a function that defines how light is reflected at an opaque surface. It is used in rendering to simulate the way light interacts with surfaces,
// taking into account the angle of incidence and the angle of reflection. The BRDF is essential for achieving realistic lighting in computer graphics.
// It describes the relationship between the incoming light direction, the outgoing light direction, and the surface normal, providing a way to calculate
// the amount of light reflected in a given direction.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------

// BRDF Lookup Texture
Texture2D BRDFLookup < Attribute("BRDFLookup"); > ;

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Sample the BRDF lookup texture
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float4 SampleBRDF(float2 vBRDFLookup)
{
    return BRDFLookup.SampleLevel( g_sTrilinearClamp, vBRDFLookup.xy, 0.0 );
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Compute the GGX BRDF
// NOTE: Returns D * PI.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float2 ComputeGGXBRDF(float2 vRoughness, float flNdotL, float flNdotV, float flNdotH, float2 vPositionSs)
{
    float2 vAlpha = vRoughness.xy * vRoughness.xy;

    // GGX D
    float2 vD = vAlpha.xy / (flNdotH * flNdotH * (vAlpha.xy * vAlpha.xy - float2(1.0, 1.0)) + float2(1.0, 1.0));
    vD.xy = vD.xy * vD.xy;

    // Schlick-Smith G
    float2 vK = vRoughness.xy + float2(1.0, 1.0);
    vK = vK.xy * vK.xy / 8.0;
    float2 vOoG = float2(4.0, 4.0);
    vOoG *= (flNdotL.xx * (float2(1.0, 1.0) - vK.xy) + vK.xy);
    vOoG *= (flNdotV.xx * (float2(1.0, 1.0) - vK.xy) + vK.xy);

    return vD.xy / vOoG.xy;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Compute the GGX Anisotropic BRDF
// NOTE: Returns D * PI.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float ComputeGGXAnisoBRDF(float2 vRoughness, float flNdotL, float flNdotV, float flNdotH, float flXdotH, float flYdotH, float flVdotH, float2 vPositionSs)
{
    float2 vAlpha = vRoughness.xy * vRoughness.xy;

    // GGX D
    float3 vD = float3(flXdotH, flYdotH, flNdotH) / float3(vAlpha.xy, 1.0);
    float flD = dot(vD.xyz, vD.xyz);
    flD *= flD * vAlpha.x * vAlpha.y;

    // Schlick-Smith G
    float flK = max(vRoughness.x, vRoughness.y) + 1.0;
    flK *= flK * (1.0 / 8.0);
    float flG = 4.0;
    flG *= (flNdotL * (1.0 - flK) + flK);
    flG *= (flNdotV * (1.0 - flK) + flK);

    return 1.0 / (flD * flG);
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Compute the Charlie Sheen BRDF
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float ComputeCharlieSheenBRDF(float flRoughness, float flNdotL, float flNdotV, float flNdotH)
{
    float flInvRoughness = 1.0 / flRoughness;
    float cos2h = flNdotH * flNdotH;
    float sin2h = 1.0 - cos2h;
    float d = (2.0 + flInvRoughness) * pow(sin2h, flInvRoughness * 0.5) / 6.28319;

    // Charlie Sheen V is expensive, use Ashikhmin V term instead
    flNdotV = saturate(flNdotV + 0.001); // avoid NaNs when both NdotL and NdotV are 0.
    float v = 1.0 / (4.0 * (flNdotL + flNdotV - flNdotL * flNdotV));
    
    return d * v * 3.14159;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Calculate the BRDF reflection factor
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float3 CalcBRDFReflectionFactor(float flNDotV, float flRoughness, float3 vSpecularColor)
{
    float2 vBRDFLookup = float2(flNDotV, flRoughness);

    float2 vBRDFTerms = SampleBRDF(vBRDFLookup.xy).rg;
    vBRDFTerms.xy *= vBRDFTerms.xy;

    return vSpecularColor * vBRDFTerms.x + vBRDFTerms.y;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Convert anisotropic roughness to isotropic roughness
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float IsotropicRoughnessFromAnisotropicRoughness(float2 vRoughness)
{
    float flRoughness = dot(vRoughness, float2(0.5f, 0.5f));
    return flRoughness;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Calculate the geometric roughness factor
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float CalculateGeometricRoughnessFactor(float3 vGeometricNormalWs)
{
    float3 vNormalWsDdx = ddx(vGeometricNormalWs.xyz);
    float3 vNormalWsDdy = ddy(vGeometricNormalWs.xyz);
    float flGeometricRoughnessFactor = pow(saturate(max(dot(vNormalWsDdx.xyz, vNormalWsDdx.xyz), dot(vNormalWsDdy.xyz, vNormalWsDdy.xyz))), 0.333);
    return flGeometricRoughnessFactor;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Adjust roughness by geometric normal
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float2 AdjustRoughnessByGeometricNormal(float2 vRoughness, float3 vGeometricNormalWs)
{
    float flGeometricRoughnessFactor = CalculateGeometricRoughnessFactor(vGeometricNormalWs.xyz);
    vRoughness.xy = max(vRoughness.xy, flGeometricRoughnessFactor.xx);
    return vRoughness.xy;
}

#endif /* BRDF_HLSL */