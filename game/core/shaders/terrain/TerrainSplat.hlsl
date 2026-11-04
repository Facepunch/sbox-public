//
// Helper functions for terrain splat sampling, you shouldn't really use these directly,
// if you just want a terrain splat then use Terrain::Sample( ... )
//

#ifndef TERRAIN_SPLAT_H
#define TERRAIN_SPLAT_H

#include "terrain/TerrainCommon.hlsl"

#if PROGRAM == VFX_PROGRAM_PS

    void Terrain_AddToStack( uint materialIndex, float weight, inout uint outIndices[4], inout float outWeights[4], inout int count )
    {
        for ( int i = 0; i < count; i++ )
        {
            if ( outIndices[i] == materialIndex )
            {
                outWeights[i] += weight;
                return;
            }
        }

        if ( weight <= 0.001 ) return;

        if ( count < 4 )
        {
            outIndices[count] = materialIndex;
            outWeights[count] = weight;
            count++;
        }
    }

    //
    // Samples neighbors material stack(4 material and weight). Pick the top-4 heaviest material
    //
    void Terrain_MergeBilinearStacks(
        uint indices00[4], float weights00[4], float blend00,
        uint indices10[4], float weights10[4], float blend10,
        uint indices01[4], float weights01[4], float blend01,
        uint indices11[4], float weights11[4], float blend11,
        out uint outIndices[4], out float outWeights[4] )
    {
        for( int i = 0; i < 4; i++ )
        {
            outIndices[i] = 0;
            outWeights[i] = 0;
        }
        int count = 0;

        // 0, 0
        Terrain_AddToStack( indices00[0], weights00[0] * blend00, outIndices, outWeights, count );
        Terrain_AddToStack( indices00[1], weights00[1] * blend00, outIndices, outWeights, count );
        Terrain_AddToStack( indices00[2], weights00[2] * blend00, outIndices, outWeights, count );
        Terrain_AddToStack( indices00[3], weights00[3] * blend00, outIndices, outWeights, count );

        // 1, 0
        Terrain_AddToStack( indices10[0], weights10[0] * blend10, outIndices, outWeights, count );
        Terrain_AddToStack( indices10[1], weights10[1] * blend10, outIndices, outWeights, count );
        Terrain_AddToStack( indices10[2], weights10[2] * blend10, outIndices, outWeights, count );
        Terrain_AddToStack( indices10[3], weights10[3] * blend10, outIndices, outWeights, count );

        // 0, 1
        Terrain_AddToStack( indices01[0], weights01[0] * blend01, outIndices, outWeights, count );
        Terrain_AddToStack( indices01[1], weights01[1] * blend01, outIndices, outWeights, count );
        Terrain_AddToStack( indices01[2], weights01[2] * blend01, outIndices, outWeights, count );
        Terrain_AddToStack( indices01[3], weights01[3] * blend01, outIndices, outWeights, count );

        // 1, 1
        Terrain_AddToStack( indices11[0], weights11[0] * blend11, outIndices, outWeights, count );
        Terrain_AddToStack( indices11[1], weights11[1] * blend11, outIndices, outWeights, count );
        Terrain_AddToStack( indices11[2], weights11[2] * blend11, outIndices, outWeights, count );
        Terrain_AddToStack( indices11[3], weights11[3] * blend11, outIndices, outWeights, count );

        // Sort by material index to maintain consistent blend order
        for ( int pass = 0; pass < 3; pass++ )
        {
            for ( int i = 0; i < 3 - pass; i++ )
            {
                if ( outIndices[i] > outIndices[i + 1] && outWeights[i + 1] > 0 )
                {
                    uint tempIndex = outIndices[i];
                    outIndices[i] = outIndices[i + 1];
                    outIndices[i + 1] = tempIndex;

                    float tempWeight = outWeights[i];
                    outWeights[i] = outWeights[i + 1];
                    outWeights[i + 1] = tempWeight;
                }
            }
        }
    }

    Material Terrain_SampleLayer( TerrainMaterial mat, float2 tileUV, float2 tileDdx, float2 tileDdy, out float height )
    {
        float2 layerUV = tileUV * mat.uvscale;
        float2x2 uvAngle = float2x2( 1, 0, 0, 1 );

        if ( mat.HasFlag( TerrainFlags::NoTile ) )
            layerUV = Terrain_SampleSeamlessUV( layerUV, uvAngle );

        SamplerState materialSampler = Bindless::GetSampler( Terrain::Get().samplerindex );
        float2 layerDdx = mul( uvAngle, tileDdx * mat.uvscale );
        float2 layerDdy = mul( uvAngle, tileDdy * mat.uvscale );

        float4 bcr = Bindless::GetTexture2D( mat.bcr_texid ).SampleGrad( materialSampler, layerUV, layerDdx, layerDdy );
        float4 nho = Bindless::GetTexture2D( mat.nho_texid ).SampleGrad( materialSampler, layerUV, layerDdx, layerDdy );

        float3 normal = ComputeNormalFromRGTexture( nho.rg );
        normal.xy = mul( uvAngle, normal.xy );
        normal.xz *= mat.normalstrength;
        normal = normalize( normal );

        Material o = Material::Init();
        o.Albedo = SrgbGammaToLinear( bcr.rgb );
        o.Normal = normal;
        o.Roughness = bcr.a;
        o.AmbientOcclusion = nho.a;
        o.Metalness = mat.metalness;

        height = nho.b * mat.heightstrength;
        return o;
    }

    Material Terrain_SplatCompact( float2 localPos, float2 localDdx, float2 localDdy, CompactTerrainMaterial material )
    {
        float2 tileUV  = localPos  / TERRAIN_SPLAT_TILE_UNITS;
        float2 tileDdx = localDdx / TERRAIN_SPLAT_TILE_UNITS;
        float2 tileDdy = localDdy / TERRAIN_SPLAT_TILE_UNITS;

        float baseHeight;
        Material base = Terrain_SampleLayer( g_TerrainMaterials[material.BaseTextureId], tileUV, tileDdx, tileDdy, baseHeight );

        float blend = material.GetNormalizedBlend();

        if ( blend <= 0.0f && !Terrain::Get().HeightBlending )
            return base;

        float overlayHeight;
        Material overlay = Terrain_SampleLayer( g_TerrainMaterials[material.OverlayTextureId], tileUV, tileDdx, tileDdy, overlayHeight );

        if ( Terrain::Get().HeightBlending )
            blend = Terrain_HeightBlendWeight( blend, baseHeight, overlayHeight, Terrain::Get().HeightBlendSharpness );

        Material o = base;
        o.Albedo = lerp( base.Albedo, overlay.Albedo, blend );
        o.Normal = lerp( base.Normal, overlay.Normal, blend );
        o.Roughness = lerp( base.Roughness, overlay.Roughness, blend );
        o.AmbientOcclusion = lerp( base.AmbientOcclusion, overlay.AmbientOcclusion, blend );
        o.Metalness = lerp( base.Metalness, overlay.Metalness, blend );
        return o;
    }

    Material Terrain_SplatStack( float2 localPos, float2 localDdx, float2 localDdy, uint indices[4], float weights[4] )
    {
        float2 tileUV  = localPos  / TERRAIN_SPLAT_TILE_UNITS;
        float2 tileDdx = localDdx / TERRAIN_SPLAT_TILE_UNITS;
        float2 tileDdy = localDdy / TERRAIN_SPLAT_TILE_UNITS;

        Material layers[4];
        float heights[4];

        // Sample materials by index
        for ( int i = 0; i < 4; i++ )
        {
            if ( weights[i] <= 0.0f )
            {
                layers[i] = Material::Init();
                layers[i].Albedo = 0;
                layers[i].Roughness = 0;
                layers[i].AmbientOcclusion = 0;
                layers[i].Metalness = 0;
                heights[i] = 0;
                continue;
            }

            layers[i] = Terrain_SampleLayer( g_TerrainMaterials[ indices[i] ], tileUV, tileDdx, tileDdy, heights[i] );
        }

        // Normalize base weights
        float baseWeights[4] = { weights[0], weights[1], weights[2], weights[3] };
        float sum = baseWeights[0] + baseWeights[1] + baseWeights[2] + baseWeights[3];
        if ( sum > 0 && sum != 1.0 )
        {
            float scale = 1.0 / sum;
            baseWeights[0] *= scale;
            baseWeights[1] *= scale;
            baseWeights[2] *= scale;
            baseWeights[3] *= scale;
        }

        float blendWeights[4];

        if ( Terrain::Get().HeightBlending )
        {
            float avgHeight = ( heights[0] * baseWeights[0] + heights[1] * baseWeights[1] +
                                heights[2] * baseWeights[2] + heights[3] * baseWeights[3] );

            float sharpness = Terrain::Get().HeightBlendSharpness * 10.0; // Scale for better control

            for ( int idx = 0; idx < 4; idx++ )
            {
                if ( baseWeights[idx] > 0.0 )
                {
                    float heightBias = ( heights[idx] - avgHeight ) * sharpness;
                    blendWeights[idx] = baseWeights[idx] * pow( 2.0, heightBias );
                }
                else
                {
                    blendWeights[idx] = 0.0;
                }
            }

            // Normalize adjusted weights
            float total = blendWeights[0] + blendWeights[1] + blendWeights[2] + blendWeights[3];
            if ( total > 0.0 )
            {
                blendWeights[0] /= total;
                blendWeights[1] /= total;
                blendWeights[2] /= total;
                blendWeights[3] /= total;
            }
        }
        else
        {
            blendWeights[0] = baseWeights[0];
            blendWeights[1] = baseWeights[1];
            blendWeights[2] = baseWeights[2];
            blendWeights[3] = baseWeights[3];
        }

        // Blend all materials simultaneously
        Material o = Material::Init();
        o.Albedo = 0;
        o.Normal = 0;
        o.Roughness = 0;
        o.AmbientOcclusion = 0;
        o.Metalness = 0;

        [unroll]
        for ( int b = 0; b < 4; b++ )
        {
            o.Albedo += layers[b].Albedo * blendWeights[b];
            o.Normal += layers[b].Normal * blendWeights[b];
            o.Roughness += layers[b].Roughness * blendWeights[b];
            o.AmbientOcclusion += layers[b].AmbientOcclusion * blendWeights[b];
            o.Metalness += layers[b].Metalness * blendWeights[b];
        }

        return o;
    }

    Material Terrain_SplatControlQuad( float2 localPos, float2 localDdx, float2 localDdy, uint4 controlBits, float4 quadWeights )
    {
        CompactTerrainMaterial mat00 = CompactTerrainMaterial::Decode( controlBits.x );
        CompactTerrainMaterial mat10 = CompactTerrainMaterial::Decode( controlBits.y );
        CompactTerrainMaterial mat01 = CompactTerrainMaterial::Decode( controlBits.z );
        CompactTerrainMaterial mat11 = CompactTerrainMaterial::Decode( controlBits.w );

        if ( controlBits.x == controlBits.y && controlBits.x == controlBits.z && controlBits.x == controlBits.w )
        {
            return Terrain_SplatCompact( localPos, localDdx, localDdy, mat00 );
        }

        if ( Terrain::Get().HeightBlending )
        {
            CompactTerrainMaterial mats[4];
            mats[0] = mat00; mats[1] = mat10; mats[2] = mat01; mats[3] = mat11;

            Material o = Material::Init();
            o.Albedo = 0;
            o.Normal = 0;
            o.Roughness = 0;
            o.AmbientOcclusion = 0;
            o.Metalness = 0;

            [unroll]
            for ( int c = 0; c < 4; c++ )
            {
                Material corner = Terrain_SplatCompact( localPos, localDdx, localDdy, mats[c] );

                o.Albedo += corner.Albedo * quadWeights[c];
                o.Normal += corner.Normal * quadWeights[c];
                o.Roughness += corner.Roughness * quadWeights[c];
                o.AmbientOcclusion += corner.AmbientOcclusion * quadWeights[c];
                o.Metalness += corner.Metalness * quadWeights[c];
            }

            return o;
        }

        uint indices00[4], indices10[4], indices01[4], indices11[4];
        float weights00[4], weights10[4], weights01[4], weights11[4];
        mat00.GetMaterialStack( indices00, weights00 );
        mat10.GetMaterialStack( indices10, weights10 );
        mat01.GetMaterialStack( indices01, weights01 );
        mat11.GetMaterialStack( indices11, weights11 );

        uint mergedIndices[4];
        float mergedWeights[4];
        Terrain_MergeBilinearStacks(
            indices00, weights00, quadWeights.x,
            indices10, weights10, quadWeights.y,
            indices01, weights01, quadWeights.z,
            indices11, weights11, quadWeights.w,
            mergedIndices, mergedWeights );

        return Terrain_SplatStack( localPos, localDdx, localDdy, mergedIndices, mergedWeights );
    }

#endif // PROGRAM == VFX_PROGRAM_PS

#endif // TERRAIN_SPLAT_H
