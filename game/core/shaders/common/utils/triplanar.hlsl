#ifndef PIXEL_TRIPLANNAR_H
#define PIXEL_TRIPLANNAR_H

//
// Sample a texture using tri-plannar mapping
//
float4 Tex2DTriplanar( in Texture2D texture, in SamplerState samplerState, float3 vPositionWs, float3 vNormalWs, float2 vTile = 512.0f, float flBlend = 1.0f, float2 vTexScale = 1.0f )
{
    // Calculate blending coefficients
    vNormalWs = abs( normalize(vNormalWs) );
    vNormalWs = pow( vNormalWs, flBlend );
    vNormalWs /= dot( vNormalWs, 1.0f ); // (vNormalWs.x + vNormalWs.y + vNormalWs.z);

    // Inches to meters. Since source does everything in inches it makes our texture really small!
    // Lets stretch it out so our values are nicer to play with
    vPositionWs /= 39.3701;

    // Fetch our samples

    vTexScale *= vTile;

    float4 vC0 = texture.Sample( samplerState, vPositionWs.zy * vTexScale );
    float4 vC1 = texture.Sample( samplerState, vPositionWs.xz * vTexScale );
    float4 vC2 = texture.Sample( samplerState, vPositionWs.xy * vTexScale );

    // Blend & Return
    return vC0 * vNormalWs.x + vC1 * vNormalWs.y + vC2 * vNormalWs.z;
}


#ifdef COMMON_PS_INPUT_DEFINED

//
// Sample a texture using tri-plannar mapping using the current world position as an input
//
float4 Tex2DTriplanar( in Texture2D texture, in SamplerState samplerState, PixelInput pixelInput, float2 vTile = 512.0f, float flBlend = 1.0f, float2 vTexScale = 1.0f )
{
    float3 vPositionWs = pixelInput.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz;
    return Tex2DTriplanar( texture, samplerState, vPositionWs, pixelInput.vNormalWs, vTile, flBlend, vTexScale );
}

#endif

#endif