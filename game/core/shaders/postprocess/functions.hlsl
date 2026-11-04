#ifndef PP_FUNCTIONS_H
#define PP_FUNCTIONS_H

#include "postprocess/common.hlsl"


float3 Saturation( float3 vColor, float flSaturationAmount, bool saturateResult = true )
{
    float3x3 mSaturationMatrix = float3x3(
        (1.0f - flSaturationAmount) + flSaturationAmount, (1.0f - flSaturationAmount), (1.0f - flSaturationAmount),
        (1.0f - flSaturationAmount), (1.0f - flSaturationAmount) + flSaturationAmount, (1.0f - flSaturationAmount),
        (1.0f - flSaturationAmount), (1.0f - flSaturationAmount), (1.0f - flSaturationAmount) + flSaturationAmount
    );

    if(saturateResult)
        return saturate( mul(mSaturationMatrix, vColor) );
    else
        return mul(mSaturationMatrix, vColor);
}


float2 PaniniProjection( float2 vTexCoords, float flDistance )
{
    float flViewDistance = 1.0f + flDistance;
    float flHypotenuse = vTexCoords.x * vTexCoords.x + flViewDistance * flViewDistance;

    float flIntersectionDistance = vTexCoords.x * flDistance;
    float flIntersectionDiscriminator = sqrt( flHypotenuse - flIntersectionDistance * flIntersectionDistance );

    float flCylindricalDistanceNoD = ( -flIntersectionDistance * vTexCoords.x + flViewDistance * flIntersectionDiscriminator ) / flHypotenuse;
    float flCylindricalDistance = flCylindricalDistanceNoD + flDistance;

    float2 vPosition = vTexCoords * (flCylindricalDistance / flViewDistance);
    return vPosition / flCylindricalDistanceNoD;
}




float3 MotionBlur( Texture2D tColorBuffer, SamplerState sSampler, float2 vTexCoords, float2 vVelocityVector, int sNumSamples )
{
    float3 vColor = tColorBuffer.Sample( sSampler, vTexCoords ).rgb;
    vTexCoords += vVelocityVector;
    for(int i = 1; i < sNumSamples; i++, vTexCoords += vVelocityVector)
    {
        vColor += tColorBuffer.Sample( sSampler, vTexCoords ).rgb;
    }
    return vColor / (float)sNumSamples;
}

float3 GaussianBlur( Texture2D tColorBuffer, SamplerState sSampler, float2 vTexCoords, float2 flSize )
{
    float fl2PI = 6.28318530718f;
    float flDirections = 16.0f;
    float flQuality = 4.0f;
    float flTaps = 1.0f;

    float3 vColor = tColorBuffer.Sample( sSampler, vTexCoords ).rgb;

    [unroll]
    for( float d=0.0; d<fl2PI; d+=fl2PI/flDirections)
    {
        [unroll]
        for(float j=1.0/flQuality; j<=1.0; j+=1.0/flQuality)
        {
            flTaps += 1;
            vColor += tColorBuffer.Sample( sSampler, vTexCoords + float2( cos(d ), sin(d) ) * flSize * j ).rgb;    
        }
    }
    return vColor / flTaps;
}

float CircleOfConfusion( float flDepth, float flFocalLength, float flFocalDistance, float flFocalRegion, float flAperture )
{   
    [flatten]
    if( flDepth > flFocalDistance )
    {
        flDepth = flFocalDistance + max( 0.0f, flDepth - flFocalDistance - flFocalRegion );
    }

    // to mm
    flDepth *= 0.0393701f;
    flFocalDistance *= 0.0393701f;

    float flCoC = flAperture * flFocalLength * ( flFocalDistance - flDepth ) / ( flDepth * ( flFocalDistance - flFocalLength ) );
    return saturate( abs( flCoC ) );
}

float3 Sharpen( Texture2D tColorBuffer, SamplerState sSampler, float3 vColor, float2 vTexCoords, float flStrength )
{
    float2 vSize = (1.0f / g_vRenderTargetSize);

    float3 vFinalColor = vColor * (1.0f + (4.0f * flStrength));
    vFinalColor += (tColorBuffer.Sample( sSampler, vTexCoords + float2(vSize.x, 0.0f )).rgb * (-1.0f * flStrength));
    vFinalColor += (tColorBuffer.Sample( sSampler, vTexCoords + float2(-vSize.x, 0.0f )).rgb * (-1.0f * flStrength));
    vFinalColor += (tColorBuffer.Sample( sSampler, vTexCoords + float2(0.0f, vSize.y )).rgb * (-1.0f * flStrength));
    vFinalColor += (tColorBuffer.Sample( sSampler, vTexCoords + float2(0.0f, -vSize.y )).rgb * (-1.0f * flStrength));

    return vFinalColor;
}

#endif