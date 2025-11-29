// Copyright (c) Facepunch. All Rights Reserved.

//
// Terrain API
// Not stable, shit will change and custom shaders using this API will break until I'm satisfied.
// But they will break for good reason and I will tell you why and how to update.
//
// 23/07/24: Initial global structured buffers
//

#ifndef TERRAIN_H
#define TERRAIN_H

struct TerrainStruct
{
    // Immediately I don't like transforms on terrain - it's wasteful and you should really only have 1 terrain.
    float4x4 Transform;
    float4x4 TransformInv;

    // Bindless texture maps
    int HeightMapTexture;
    int ControlMapTexture;
    int HolesMapTexture;
    int Padding;

    float Resolution; // should be inv?
    float HeightScale;

    // Height Blending
    bool HeightBlending;
    float HeightBlendSharpness;
};

struct TerrainMaterial
{
    int bcr_texid;
    int nho_texid;
    float uvscale;
    float uvrotation;
    float metalness;
    float heightstrength;
    float normalstrength;
    float displacementscale;
};

// What's these doing here
SamplerState g_sBilinearBorder < Filter( BILINEAR ); AddressU( BORDER ); AddressV( BORDER ); >;
SamplerState g_sAnisotropic < Filter( ANISOTROPIC ); MaxAniso(8); >;

StructuredBuffer<TerrainStruct> g_Terrains < Attribute( "Terrain" ); >;
StructuredBuffer<TerrainMaterial> g_TerrainMaterials < Attribute( "TerrainMaterials" ); >;

// This will get more complex with regions as we grow.. Regions means multiple heightmaps
// So lets have a nice helper class for most things
// This should just be for accessing data, rendering related methods shouldn't be crammed in here
class Terrain
{
    static TerrainStruct Get() { return g_Terrains[0]; }

    static Texture2D GetHeightMap() { return GetBindlessTexture2D( Get().HeightMapTexture ); }
    static Texture2D GetControlMap() { return GetBindlessTexture2D( Get().ControlMapTexture ); }
    static Texture2D GetHolesMap() { return GetBindlessTexture2D( Get().HolesMapTexture ); }

    static float GetHeight( float2 worldPos )
    {
        Texture2D tHeightMap = GetHeightMap();
        float2 texSize = TextureDimensions2D( tHeightMap, 0 ); // todo: store me in the struct...

        float2 heightUv = ( worldPos.xy ) / ( texSize * Get().Resolution );
        return tHeightMap.SampleLevel( g_sBilinearBorder, heightUv, 0 ).r * Get().HeightScale;
    }
};

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

// Move to another file:

//
// Takes 4 samples
// This is easy for now, an optimization would be to generate this once in a compute shader
// Less texture sampling but higher memory requirements
// This is between -1 and 1;
//
float3 Terrain_Normal( Texture2D HeightMap, float2 uv, float maxheight, out float3 TangentU, out float3 TangentV )
{
    float2 texelSize = 1.0f / ( float2 )TextureDimensions2D( HeightMap, 0 );

    float l = abs( HeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( -1, 0 ), 0 ).r );
    float r = abs( HeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 1, 0 ), 0 ).r );
    float t = abs( HeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 0, -1 ), 0 ).r );
    float b = abs( HeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 0, 1 ), 0 ).r );

    // Compute dx using central differences
    float dX = l - r;

    // Compute dy using central differences
    float dY = b - t;

    // Normal strength needs to take in account terrain dimensions rather than just texel scale
    float normalStrength = maxheight / Terrain::Get(  ).Resolution;

    float3 normal = normalize( float3( dX, dY * -1, 1.0f / normalStrength ) );

    TangentU = normalize( cross( normal, float3( 0, -1, 0 ) ) );
    TangentV = normalize( cross( normal, -TangentU ) );

    return normal;
}

//
// Nice box filtered checkboard pattern, useful when you have no textures
//
void Terrain_ProcGrid( in float2 p, out float3 albedo, out float roughness )
{
    p /= 64;

    float2 w = fwidth( p ) + 0.001;
    float2 i = 2.0 * ( abs( frac( ( p - 0.5 * w ) * 0.5 ) - 0.5 ) - abs( frac( ( p + 0.5 * w ) * 0.5 ) - 0.5 ) ) / w;
    float v = ( 0.5 - 0.5 * i.x * i.y );

    albedo = 0.7f + v * 0.3f;
    roughness = 0.8f + ( 1 - v ) * 0.2f;
}

float4 Terrain_Debug( uint nDebugView, uint lodLevel, float2 uv )
{
    if ( nDebugView == 1 )
    {
        float3 hsv = float3( lodLevel / 10.0f, 1.0f, 0.8f );
        return float4( SrgbGammaToLinear( HsvToRgb( hsv ) ), 1.0f );
    }

    if ( nDebugView == 2 )
    {
       // return float4( g_tControlMap.Sample( g_sBilinearBorder, uv ).a, 0.0f, 0.0f, 1.0f );
    }        

    return float4( 0, 0, 0, 1 );
}

// black wireframe if we're looking at lods, otherwise lod color
float4 Terrain_WireframeColor( uint lodLevel )
{       
    return float4( SrgbGammaToLinear( HsvToRgb( float3( lodLevel / 10.0f, 0.6f, 1.0f ) ) ), 1.0f );
}

#endif