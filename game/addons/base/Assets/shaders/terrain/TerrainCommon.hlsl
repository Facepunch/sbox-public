// Copyright (c) Facepunch. All Rights Reserved.

//
// Terrain API
// Not stable, shit will change and custom shaders using this API will break until I'm satisfied.
// But they will break for good reason and I will tell you why and how to update.
//
// 12/9/25: Added NoTile Flag
// 23/07/24: Initial global structured buffers
//

#ifndef TERRAIN_H
#define TERRAIN_H

#include "terrain/TerrainSplatFormat.hlsl"

struct TerrainStruct
{
    // Immediately I don't like transforms on terrain - it's wasteful and you should really only have 1 terrain.
    float4x4 Transform;
    float4x4 TransformInv;

    // Bindless texture maps
    int HeightMapTexture;
    int ControlMapTexture;

    float Resolution; // should be inv?
    float HeightScale;

    // Height Blending
    bool HeightBlending;
    float HeightBlendSharpness;

    int samplerindex;
};

enum TerrainFlags
{
    NoTile = 1 // (1 << 0)
};

struct TerrainMaterial
{
    int bcr_texid;
    int nho_texid;
    float uvscale;
    uint flags;
    float metalness;
    float heightstrength;
    float normalstrength;
    float displacementscale;

    bool HasFlag( TerrainFlags flag )
    {
        return (flags & flag) != 0;
    }
};

SamplerState g_sBilinearBorder < Filter( BILINEAR ); AddressU( BORDER ); AddressV( BORDER ); >;
SamplerState g_sAnisotropic < Filter( ANISOTROPIC ); MaxAniso(8); >;

int g_nTerrainCount < Attribute( "TerrainCount" ); Default( 0 ); >;

StructuredBuffer<TerrainStruct> g_Terrains < Attribute( "Terrain" ); >;
StructuredBuffer<TerrainMaterial> g_TerrainMaterials < Attribute( "TerrainMaterials" ); >;

float2 Terrain_SampleSeamlessUV( float2 uv );
float2 Terrain_SampleSeamlessUV( float2 uv, out float2x2 uvAngle );

// This will get more complex with regions as we grow.. Regions means multiple heightmaps
// So lets have a nice helper class for most things
// This should just be for accessing data, rendering related methods shouldn't be crammed in here
class Terrain
{
    static int Count() { return g_nTerrainCount; }
    static TerrainStruct Get() { return g_Terrains[0]; }

    static Texture2D GetHeightMap() { return Bindless::GetTexture2D( Get().HeightMapTexture ); }
    static Texture2D GetControlMap() { return Bindless::GetTexture2D( Get().ControlMapTexture ); }

    static float3 WorldToLocal( float3 worldPos )
    {
        return mul( Get().TransformInv, float4( worldPos, 1.0 ) ).xyz;
    }

    static float2 GetUV( float3 worldPos )
    {
        float3 localPos = WorldToLocal( worldPos );
        Texture2D tHeightMap = GetHeightMap();
        float2 texSize = TextureDimensions2D( tHeightMap, 0 );
        return localPos.xy / ( texSize * Get().Resolution );
    }

    static bool IsInBounds( float3 worldPos )
    {
        if ( Count() <= 0 )
            return false;

        float2 uv = GetUV( worldPos );
        return all( uv >= 0.0 ) && all( uv <= 1.0 );
    }

    static float GetHeight( float2 localPos )
    {
        Texture2D tHeightMap = GetHeightMap();
        float2 texSize = TextureDimensions2D( tHeightMap, 0 );

        float2 heightUv = localPos.xy / ( texSize * Get().Resolution );
        return tHeightMap.SampleLevel( g_sBilinearBorder, heightUv, 0 ).r * Get().HeightScale;
    }

    static float GetWorldHeight( float3 worldPos )
    {
        float3 localPos = WorldToLocal( worldPos );
        float localHeight = GetHeight( localPos.xy );
        return mul( Get().Transform, float4( localPos.xy, localHeight, 1.0 ) ).z;
    }

    static float GetDistanceToSurface( float3 worldPos )
    {
        return worldPos.z - GetWorldHeight( worldPos );
    }

    // Get a 0-1 blend factor for mesh blending based on distance to terrain surface
    // Returns 1 at terrain surface, fading to 0 at blendLength distance above
    static float GetBlendFactor( float3 worldPos, float blendLength )
    {
        float dist = GetDistanceToSurface( worldPos );
        return 1.0 - saturate( dist / max( blendLength, 0.001 ) );
    }

    static float3 SampleMaterialColor( float2 texUV, CompactTerrainMaterial material )
    {
        texUV /= 32.0;

        TerrainMaterial baseMat = g_TerrainMaterials[material.BaseTextureId];
        if ( baseMat.bcr_texid <= 0 )
            return float3( 1, 1, 1 );

        SamplerState baseSampler = Bindless::GetSampler( Get().samplerindex );

        float2 baseUV = texUV * baseMat.uvscale;
        if ( baseMat.HasFlag( TerrainFlags::NoTile ) )
            baseUV = Terrain_SampleSeamlessUV( baseUV );

        float4 baseBcr = Bindless::GetTexture2D( baseMat.bcr_texid ).Sample( baseSampler, baseUV );
        float3 baseColor = SrgbGammaToLinear( baseBcr.rgb );

        float blend = material.GetNormalizedBlend();
        TerrainMaterial overlayMat = g_TerrainMaterials[material.OverlayTextureId];
        if ( blend <= 0.01 || overlayMat.bcr_texid <= 0 )
            return baseColor;

        SamplerState overlaySampler = Bindless::GetSampler( Get().samplerindex );

        float2 overlayUV = texUV * overlayMat.uvscale;
        if ( overlayMat.HasFlag( TerrainFlags::NoTile ) )
            overlayUV = Terrain_SampleSeamlessUV( overlayUV );

        float4 overlayBcr = Bindless::GetTexture2D( overlayMat.bcr_texid ).Sample( overlaySampler, overlayUV );

        if ( Get().HeightBlending && baseMat.nho_texid > 0 && overlayMat.nho_texid > 0 )
        {
            float baseHeight = Bindless::GetTexture2D( baseMat.nho_texid ).Sample( baseSampler, baseUV ).b * baseMat.heightstrength;
            float overlayHeight = Bindless::GetTexture2D( overlayMat.nho_texid ).Sample( overlaySampler, overlayUV ).b * overlayMat.heightstrength;
            blend = saturate( blend + (overlayHeight - baseHeight) * Get().HeightBlendSharpness * 10.0 );
        }

        return lerp( baseColor, SrgbGammaToLinear( overlayBcr.rgb ), blend );
    }

    static float3 SampleMaterialColor( float2 texUV, CompactTerrainMaterial material, float mipLevel )
    {
        texUV /= 32.0;

        TerrainMaterial baseMat = g_TerrainMaterials[material.BaseTextureId];
        if ( baseMat.bcr_texid <= 0 )
            return float3( 1, 1, 1 );

        SamplerState baseSampler = Bindless::GetSampler( Get().samplerindex );

        float2 baseUV = texUV * baseMat.uvscale;
        if ( baseMat.HasFlag( TerrainFlags::NoTile ) )
            baseUV = Terrain_SampleSeamlessUV( baseUV );

        float4 baseBcr = Bindless::GetTexture2D( baseMat.bcr_texid ).SampleLevel( baseSampler, baseUV, mipLevel );
        float3 baseColor = SrgbGammaToLinear( baseBcr.rgb );

        float blend = material.GetNormalizedBlend();
        TerrainMaterial overlayMat = g_TerrainMaterials[material.OverlayTextureId];
        if ( blend <= 0.01 || overlayMat.bcr_texid <= 0 )
            return baseColor;

        SamplerState overlaySampler = Bindless::GetSampler( Get().samplerindex );

        float2 overlayUV = texUV * overlayMat.uvscale;
        if ( overlayMat.HasFlag( TerrainFlags::NoTile ) )
            overlayUV = Terrain_SampleSeamlessUV( overlayUV );

        float4 overlayBcr = Bindless::GetTexture2D( overlayMat.bcr_texid ).SampleLevel( overlaySampler, overlayUV, mipLevel );

        if ( Get().HeightBlending && baseMat.nho_texid > 0 && overlayMat.nho_texid > 0 )
        {
            float baseHeight = Bindless::GetTexture2D( baseMat.nho_texid ).SampleLevel( baseSampler, baseUV, mipLevel ).b * baseMat.heightstrength;
            float overlayHeight = Bindless::GetTexture2D( overlayMat.nho_texid ).SampleLevel( overlaySampler, overlayUV, mipLevel ).b * overlayMat.heightstrength;
            blend = saturate( blend + (overlayHeight - baseHeight) * Get().HeightBlendSharpness * 10.0 );
        }

        return lerp( baseColor, SrgbGammaToLinear( overlayBcr.rgb ), blend );
    }

    static bool FetchTerrainMaterials( float3 worldPos, out float2 texUV,
        out CompactTerrainMaterial mat00, out CompactTerrainMaterial mat10, out CompactTerrainMaterial mat01, out CompactTerrainMaterial mat11,
        out float4 weights )
    {
        texUV = 0.0;
        mat00 = CompactTerrainMaterial::Decode( 0 );
        mat10 = CompactTerrainMaterial::Decode( 0 );
        mat01 = CompactTerrainMaterial::Decode( 0 );
        mat11 = CompactTerrainMaterial::Decode( 0 );
        weights = 0.0;

        if ( Get().ControlMapTexture <= 0 )
            return false;

        float3 localPos = WorldToLocal( worldPos );
        Texture2D tControlMap = GetControlMap();
        float2 texSize = TextureDimensions2D( tControlMap, 0 );
        float2 uv = localPos.xy / ( texSize * Get().Resolution );

        if ( any( uv < 0.0 ) || any( uv > 1.0 ) )
            return false;

        float2 pixelUV = uv * texSize - 0.5;
        float2 fracUV = frac( pixelUV );
        float2 texelSize = 1.0 / texSize;
        float2 baseUV = (floor( pixelUV ) + 0.5) / texSize;

        mat00 = CompactTerrainMaterial::DecodeFromFloat( tControlMap.SampleLevel( g_sPointClamp, baseUV, 0 ).r );
        mat10 = CompactTerrainMaterial::DecodeFromFloat( tControlMap.SampleLevel( g_sPointClamp, baseUV + float2( texelSize.x, 0 ), 0 ).r );
        mat01 = CompactTerrainMaterial::DecodeFromFloat( tControlMap.SampleLevel( g_sPointClamp, baseUV + float2( 0, texelSize.y ), 0 ).r );
        mat11 = CompactTerrainMaterial::DecodeFromFloat( tControlMap.SampleLevel( g_sPointClamp, baseUV + texelSize, 0 ).r );

        weights = float4(
            (1.0 - fracUV.x) * (1.0 - fracUV.y),
            fracUV.x * (1.0 - fracUV.y),
            (1.0 - fracUV.x) * fracUV.y,
            fracUV.x * fracUV.y
        );

        texUV = localPos.xy;
        return true;
    }

    // Sample the terrain surface color at a world position.
    // Matches terrain rendering by bilinearly blending neighboring compact control-map materials.
    static float3 SampleColor( float3 worldPos )
    {
        float2 texUV;
        float4 weights;
        CompactTerrainMaterial mat00, mat10, mat01, mat11;

        if ( !FetchTerrainMaterials( worldPos, texUV, mat00, mat10, mat01, mat11, weights ) )
            return float3( 1, 1, 1 );

        return
            SampleMaterialColor( texUV, mat00 ) * weights.x +
            SampleMaterialColor( texUV, mat10 ) * weights.y +
            SampleMaterialColor( texUV, mat01 ) * weights.z +
            SampleMaterialColor( texUV, mat11 ) * weights.w;
    }

    static float3 SampleColor( float3 worldPos, float mipLevel )
    {
        float2 texUV;
        float4 weights;
        CompactTerrainMaterial mat00, mat10, mat01, mat11;

        if ( !FetchTerrainMaterials( worldPos, texUV, mat00, mat10, mat01, mat11, weights ) )
            return float3( 1, 1, 1 );

        return
            SampleMaterialColor( texUV, mat00, mipLevel ) * weights.x +
            SampleMaterialColor( texUV, mat10, mipLevel ) * weights.y +
            SampleMaterialColor( texUV, mat01, mipLevel ) * weights.z +
            SampleMaterialColor( texUV, mat11, mipLevel ) * weights.w;
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

//
// Takes 4 samples
// This is easy for now, an optimization would be to generate this once in a compute shader
// Less texture sampling but higher memory requirements
// This is between -1 and 1;
//
float3 Terrain_Normal( Texture2D HeightMap, float2 uv, float maxheight, out float3 TangentU, out float3 TangentV )
{
    float2 texelSize = 1.0f / ( float2 )TextureDimensions2D( HeightMap, 0 );

    float l = abs( HeightMap.SampleLevel( g_sBilinearClamp, uv + texelSize * float2( -1, 0 ), 0 ).r );
    float r = abs( HeightMap.SampleLevel( g_sBilinearClamp, uv + texelSize * float2( 1, 0 ), 0 ).r );
    float t = abs( HeightMap.SampleLevel( g_sBilinearClamp, uv + texelSize * float2( 0, -1 ), 0 ).r );
    float b = abs( HeightMap.SampleLevel( g_sBilinearClamp, uv + texelSize * float2( 0, 1 ), 0 ).r );

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

#ifdef COMMON_COLOR_H
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

#endif
