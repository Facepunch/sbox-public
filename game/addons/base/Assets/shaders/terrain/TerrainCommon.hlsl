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

    float UnitsPerTexel;
    float HeightScale;

    // Height Blending
    bool HeightBlending;
    float HeightBlendSharpness;

    int samplerindex;

    int NormalMapTexture;
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

float Terrain_HeightBlendWeight( float t, float baseHeight, float overlayHeight, float sharpness )
{
    // Normalize so splat coverage governs the extremes regardless of heightstrength scale
    float hmax = max( max( baseHeight, overlayHeight ), 1e-4 );
    baseHeight /= hmax;
    overlayHeight /= hmax;

    float depth = lerp( 0.3, 0.02, saturate( sharpness ) );
    float a = baseHeight + ( 1.0 - t );
    float b = overlayHeight + t;
    float ma = max( a, b ) - depth;
    float wb = max( a - ma, 0.0 );
    float wo = max( b - ma, 0.0 );
    return wo / ( wb + wo + 1e-6 );
}

// This will get more complex with regions as we grow.. Regions means multiple heightmaps
// So lets have a nice helper class for most things
// This should just be for accessing data, rendering related methods shouldn't be crammed in here
class Terrain
{
    static int Count() { return g_nTerrainCount; }
    static TerrainStruct Get() { return g_Terrains[0]; }

    static Texture2D GetHeightMap() { return Bindless::GetTexture2D( Get().HeightMapTexture ); }
    static Texture2D GetControlMap() { return Bindless::GetTexture2D( Get().ControlMapTexture ); }
    static Texture2D GetNormalMap() { return Bindless::GetTexture2D( Get().NormalMapTexture ); }

    // Baked terrain-local geometric normal. Falls back to a flat up-normal if not built.
    static float3 SampleNormal( float2 uv )
    {
        if ( Get().NormalMapTexture == 0 )
            return float3( 0, 0, 1 );

        float3 n;
        n.xy = GetNormalMap().SampleLevel( g_sBilinearClamp, uv, 0 ).xy * 2.0 - 1.0;
        n.z = sqrt( saturate( 1.0 - dot( n.xy, n.xy ) ) );
        return n;
    }

    // Terrain-local geometric normal and tangent basis from the baked map
    static float3 NormalBasis( float2 uv, out float3 tangentU, out float3 tangentV )
    {
        float3 normal = SampleNormal( uv );
        tangentU = normalize( cross( normal, float3( 0, -1, 0 ) ) );
        tangentV = normalize( cross( normal, -tangentU ) );
        return normal;
    }

    static float3 WorldToLocal( float3 worldPos )
    {
        return mul( Get().TransformInv, float4( worldPos, 1.0 ) ).xyz;
    }

    static float2 GetUV( float3 worldPos )
    {
        float3 localPos = WorldToLocal( worldPos );
        Texture2D tHeightMap = GetHeightMap();
        float2 texSize = TextureDimensions2D( tHeightMap, 0 );
        return localPos.xy / ( texSize * Get().UnitsPerTexel );
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

        float2 heightUv = localPos.xy / ( texSize * Get().UnitsPerTexel );
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
            blend = Terrain_HeightBlendWeight( blend, baseHeight, overlayHeight, Get().HeightBlendSharpness );
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
            blend = Terrain_HeightBlendWeight( blend, baseHeight, overlayHeight, Get().HeightBlendSharpness );
        }

        return lerp( baseColor, SrgbGammaToLinear( overlayBcr.rgb ), blend );
    }

    // Fetch the 2x2 control quad bilinear filtering would use at uv, plus matching weights, in
    // (0,0) (1,0) (0,1) (1,1) order. Gathers at the quad's shared corner, derived from the same floor
    // as the weights, so the texture unit's fixed-point rounding can't pick a different quad. Returns
    // raw bits: the packed values are denormal floats and a flush-to-zero float compare calls them all
    // equal - compare and decode the uints.
    static uint4 GatherControlQuad( float2 uv, out float4 weights )
    {
        Texture2D tControlMap = GetControlMap();
        float2 texSize = TextureDimensions2D( tControlMap, 0 );

        float2 pixelUV = uv * texSize - 0.5;
        float2 fracUV = frac( pixelUV );

        weights = float4(
            (1.0 - fracUV.x) * (1.0 - fracUV.y),
            fracUV.x * (1.0 - fracUV.y),
            (1.0 - fracUV.x) * fracUV.y,
            fracUV.x * fracUV.y
        );

        // Gather returns w=(0,0) z=(1,0) x=(0,1) y=(1,1); reorder to match the weights
        float2 gatherUV = ( floor( pixelUV ) + 1.0 ) / texSize;
        return asuint( tControlMap.GatherRed( g_sPointClamp, gatherUV ).wzxy );
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
        float2 uv = localPos.xy / ( texSize * Get().UnitsPerTexel );

        if ( any( uv < 0.0 ) || any( uv > 1.0 ) )
            return false;

        uint4 controlBits = GatherControlQuad( uv, weights );
        mat00 = CompactTerrainMaterial::Decode( controlBits.x );
        mat10 = CompactTerrainMaterial::Decode( controlBits.y );
        mat01 = CompactTerrainMaterial::Decode( controlBits.z );
        mat11 = CompactTerrainMaterial::Decode( controlBits.w );

        texUV = localPos.xy;
        return true;
    }

    // Sample the terrain surface color at a world position.
    // Matches terrain rendering by bilinearly blending neighboring compact control-map materials.
    static float3 SampleColor( float3 worldPos )
    {
        float2 texUV;
        float4 weights;
        CompactTerrainMaterial mat00 = CompactTerrainMaterial::Decode( 0 ), mat10 = CompactTerrainMaterial::Decode( 0 ), mat01 = CompactTerrainMaterial::Decode( 0 ), mat11 = CompactTerrainMaterial::Decode( 0 );

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
        CompactTerrainMaterial mat00 = CompactTerrainMaterial::Decode( 0 ), mat10 = CompactTerrainMaterial::Decode( 0 ), mat01 = CompactTerrainMaterial::Decode( 0 ), mat11 = CompactTerrainMaterial::Decode( 0 );

        if ( !FetchTerrainMaterials( worldPos, texUV, mat00, mat10, mat01, mat11, weights ) )
            return float3( 1, 1, 1 );

        return
            SampleMaterialColor( texUV, mat00, mipLevel ) * weights.x +
            SampleMaterialColor( texUV, mat10, mipLevel ) * weights.y +
            SampleMaterialColor( texUV, mat01, mipLevel ) * weights.z +
            SampleMaterialColor( texUV, mat11, mipLevel ) * weights.w;
    }
};

//
// Compatibility shim for custom terrain shaders. HeightMap/maxheight are unused - the baked map
// already encodes them.
//
float3 Terrain_Normal( Texture2D HeightMap, float2 uv, float maxheight, out float3 TangentU, out float3 TangentV )
{
    return Terrain::NormalBasis( uv, TangentU, TangentV );
}

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
