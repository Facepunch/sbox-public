#ifndef TERRAIN_SEAMLESS_UV_H
#define TERRAIN_SEAMLESS_UV_H

// Get UV with per-tile UV offset to reduce visible tiling
// Works by offsetting UVs within each tile using a hash of the tile coordinate
float2 Terrain_SampleSeamlessUV( float2 uv, out float2x2 uvAngle )
{
    float2 tileCoord = floor( uv );
    float2 localUV = frac( uv );

    // Generate random values for this tile
    float2 hash = frac(tileCoord * float2(443.897f, 441.423f));
    hash += dot(hash, hash.yx + 19.19f);
    hash = frac((hash.xx + hash.yx) * hash.xy);

    // Random rotation (0 to 2π)
    float angle = hash.x * 6.28318530718;
    float cosA = cos(angle);
    float sinA = sin(angle);
    float2x2 rot = float2x2(cosA, -sinA, sinA, cosA);

    // Output rotation matrix 
    uvAngle = rot;

    // Rotate around center
    localUV = mul(rot, localUV - 0.5) + 0.5;

    // Apply random offset
    return tileCoord + frac(localUV + hash);
}

float2 Terrain_SampleSeamlessUV( float2 uv ) 
{
    float2x2 dummy;
    return Terrain_SampleSeamlessUV( uv, dummy ); 
}

#endif
