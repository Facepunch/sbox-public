#ifndef PP_COMMON_H
#define PP_COMMON_H

RenderState( DepthWriteEnable, false );
RenderState( DepthEnable, false );

float GetLuminance( float3 vColor )
{
    // Convert to XYZ color space, but only the Y component
    return dot( vColor, float3( 0.2126729f, 0.7151522f, 0.0721750f ) );
}

float DistanceFalloff( float currentDistance, float startDistance, float endDistance, float falloffExponent )
{
    if(currentDistance <= startDistance) return 0.0f;
    if(currentDistance >= endDistance) return 1.0f;

    float flRemaining = abs(endDistance - startDistance);
    float flTraversed = currentDistance - startDistance;

    return saturate( pow( flTraversed / flRemaining, falloffExponent ) );
}

#endif