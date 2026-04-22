//
// Simple Terrain shader with 4 layer splat
//

HEADER
{
	Description = "Terrain";
    DevShader = true;
    DebugInfo = false;
}

FEATURES
{
    // gonna go crazy the amount of shit this stuff adds and fails to compile without
    #include "vr_common_features.fxc"
}

MODES
{
    Forward();
    Depth( S_MODE_DEPTH );
}

COMMON
{
    // Opt out of stupid shitCould
    #define CUSTOM_MATERIAL_INPUTS

    #include "common/shared.hlsl"
    #include "common/Bindless.hlsl"
    #include "terrain/TerrainCommon.hlsl"

    int g_nDebugView < Attribute( "DebugView" ); >;
    int g_nPreviewLayer < Attribute( "PreviewLayer" ); >;

    bool g_bVertexDisplacement < Attribute( "VertexDisplacement" ); Default( 0 ); >;
}

struct VertexInput
{
	float3 PositionAndLod : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
    float3 LocalPosition : TEXCOORD0;
    float3 WorldPosition : TEXCOORD1;
    uint LodLevel : COLOR0;

    #if ( PROGRAM == VFX_PROGRAM_VS )
        float4 PixelPosition : SV_Position;
    #endif

    #if ( PROGRAM == VFX_PROGRAM_PS )
        float4 ScreenPosition : SV_Position;
    #endif
};

VS
{
    #include "terrain/TerrainClipmap.hlsl"

	PixelInput MainVs( VertexInput i )
	{
        PixelInput o;

        Texture2D tHeightMap = Bindless::GetTexture2D( Terrain::Get().HeightMapTexture );
        o.LocalPosition = Terrain_ClipmapSingleMesh( i.PositionAndLod, tHeightMap, Terrain::Get().Resolution, Terrain::Get().TransformInv );

        o.LocalPosition.z *= Terrain::Get().HeightScale;

        // Calculate UV coordinates for sampling control map and material textures
        float2 texSize = TextureDimensions2D( tHeightMap, 0 );
        float2 uv = o.LocalPosition.xy / ( texSize * Terrain::Get().Resolution );

        // Calculate the normal of the displacement using 3 samples
        float2 texelSize = 1.0f / texSize;
        float center = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv, 0 ).r );
        float r = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 1, 0 ), 0 ).r );
        float b = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 0, 1 ), 0 ).r );

        float normalStrength = Terrain::Get().HeightScale / Terrain::Get().Resolution;
        float3 geoNormal = normalize( float3( center - r, (b - center) * -1, 1.0f / normalStrength ) );

        // Transform normal to world space
        geoNormal = normalize( mul( Terrain::Get().Transform, float4( geoNormal, 0.0 ) ).xyz );

        // Vertex displacement
        if ( g_bVertexDisplacement )
        {
            // Blend displacement between all materials
            float totalDisplacement = 0.0f;

            // Use compact control map with point sampling
            Texture2D tControlMap = Bindless::GetTexture2D( Terrain::Get().ControlMapTexture );
            float rawPixel = tControlMap.SampleLevel( g_sPointClamp, uv, 0 ).r;
            CompactTerrainMaterial material = CompactTerrainMaterial::DecodeFromFloat( rawPixel );
            
            // Sample base material displacement
            TerrainMaterial mat = g_TerrainMaterials[material.BaseTextureId];
            float2 baseLayerUV = ( o.LocalPosition.xy / 32.0f ) * mat.uvscale;

            if( mat.HasFlag( TerrainFlags::NoTile ) )
                baseLayerUV = Terrain_SampleSeamlessUV( baseLayerUV );

            float4 baseNho = Bindless::GetTexture2D( mat.nho_texid ).SampleLevel( g_sAnisotropic, baseLayerUV, 0 );
            float baseDisplacement = baseNho.b * mat.displacementscale;
            
            // Sample overlay material displacement
            mat = g_TerrainMaterials[material.OverlayTextureId];
            float2 overlayLayerUV = ( o.LocalPosition.xy / 32.0f ) * mat.uvscale;
            
            if( mat.HasFlag( TerrainFlags::NoTile ) )
                overlayLayerUV = Terrain_SampleSeamlessUV( overlayLayerUV );

            float4 overlayNho = Bindless::GetTexture2D( mat.nho_texid ).SampleLevel( g_sAnisotropic, overlayLayerUV, 0 );
            float overlayDisplacement = overlayNho.b * mat.displacementscale;
            
            // Blend between base and overlay displacement
            float blend = material.GetNormalizedBlend();
            totalDisplacement = lerp( baseDisplacement, overlayDisplacement, blend );

            // Fade displacement on coarse LODs to prevent seam cracks
            // Displacement fading starts at LOD 2 and completes by LOD 4 to prevent seam cracks between LODs.
            static const float DISPLACEMENT_FADE_START_LOD = 2.0f; // LOD at which displacement fading starts
            static const float DISPLACEMENT_FADE_RANGE = 2.0f;     // Number of LODs over which fading occurs
            float lodLevel = i.PositionAndLod.z;
            float displacementFade = saturate(1.0 - (lodLevel - DISPLACEMENT_FADE_START_LOD) / DISPLACEMENT_FADE_RANGE);

            // Displace vertex along geometric normal
            o.LocalPosition.xyz += geoNormal * totalDisplacement * displacementFade;
        }

        o.WorldPosition = mul( Terrain::Get().Transform, float4( o.LocalPosition, 1.0 ) ).xyz;
        o.PixelPosition = Position3WsToPs( o.WorldPosition.xyz );
        o.LodLevel = i.PositionAndLod.z;

        // Check for holes in vertex shader using control map's extra data
        if ( Terrain::Get().ControlMapTexture != 0 )
        {
            Texture2D tControlMap = Bindless::GetTexture2D( Terrain::Get().ControlMapTexture );
            float rawPixel = tControlMap.SampleLevel( g_sPointClamp, uv, 0 ).r;
            CompactTerrainMaterial material = CompactTerrainMaterial::DecodeFromFloat( rawPixel );
            
            if ( material.IsHole )
            {
                o.LocalPosition = float3( 0. / 0., 0, 0 );
                o.WorldPosition = mul( Terrain::Get().Transform, float4( o.LocalPosition, 1.0 ) ).xyz;
                o.PixelPosition = Position3WsToPs( o.WorldPosition.xyz );
            }
        }

		return o;
	}
}

//=========================================================================================================================

PS
{
    DynamicCombo( D_GRID, 0..1, Sys( ALL ) );
    DynamicCombo( D_AUTO_SPLAT, 0..1, Sys( ALL ) );

    #include "common/pixel.hlsl"
    #include "common/material.hlsl"
    #include "common/shadingmodel.hlsl"

    /// <summary>
    /// Add a material to the output list, or add to existing if already present.
    /// </summary>
    void AddMaterial( uint materialIndex, float weight, inout uint outIndices[4], inout float outWeights[4], inout int count )
    {
        // Check if material already exists (always accumulate, even tiny weights)
        for ( int i = 0; i < count; i++ )
        {
            if ( outIndices[i] == materialIndex )
            {
                outWeights[i] += weight;
                return;
            }
        }

        // Only skip adding NEW materials if weight is negligible
        if ( weight <= 0.001 ) return;

        // Add new material if we have space
        if ( count < 4 )
        {
            outIndices[count] = materialIndex;
            outWeights[count] = weight;
            count++;
        }
    }

    /// <summary>
    /// Samples neighbors material stack(4 material & weight). Pick the top-4 heaviest material
    /// </summary>
    void MergeBilinearMaterials(
        uint indices00[4], float weights00[4], float blend00,
        uint indices10[4], float weights10[4], float blend10,
        uint indices01[4], float weights01[4], float blend01,
        uint indices11[4], float weights11[4], float blend11,
        out uint outIndices[4], out float outWeights[4] )
    {
        for(int i = 0; i < 4; i++)
        {
            outIndices[i] = 0;
            outWeights[i] = 0;
        }
        int count = 0;

        // 0, 0
        AddMaterial( indices00[0], weights00[0] * blend00, outIndices, outWeights, count );
        AddMaterial( indices00[1], weights00[1] * blend00, outIndices, outWeights, count );
        AddMaterial( indices00[2], weights00[2] * blend00, outIndices, outWeights, count );
        AddMaterial( indices00[3], weights00[3] * blend00, outIndices, outWeights, count );

        // 1, 0
        AddMaterial( indices10[0], weights10[0] * blend10, outIndices, outWeights, count );
        AddMaterial( indices10[1], weights10[1] * blend10, outIndices, outWeights, count );
        AddMaterial( indices10[2], weights10[2] * blend10, outIndices, outWeights, count );
        AddMaterial( indices10[3], weights10[3] * blend10, outIndices, outWeights, count );

        // 0, 1
        AddMaterial( indices01[0], weights01[0] * blend01, outIndices, outWeights, count );
        AddMaterial( indices01[1], weights01[1] * blend01, outIndices, outWeights, count );
        AddMaterial( indices01[2], weights01[2] * blend01, outIndices, outWeights, count );
        AddMaterial( indices01[3], weights01[3] * blend01, outIndices, outWeights, count );

        // 1, 1
        AddMaterial( indices11[0], weights11[0] * blend11, outIndices, outWeights, count );
        AddMaterial( indices11[1], weights11[1] * blend11, outIndices, outWeights, count );
        AddMaterial( indices11[2], weights11[2] * blend11, outIndices, outWeights, count );
        AddMaterial( indices11[3], weights11[3] * blend11, outIndices, outWeights, count );

        // Sort by material index to maintain consistent blend order
        // This prevents harsh cutoffs from materials flipping order at pixel boundaries
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

    float HeightBlend( float h1, float h2, float c1, float c2, out float ctrlHeight )
    {
        float h1Prefilter = h1 * sign( c1 );
        float h2Prefilter = h2 * sign( c2 );
        float height1 = h1Prefilter + c1;
        float height2 = h2Prefilter + c2;
        float blendFactor = (clamp(((height1 - height2) / ( 1.0f - Terrain::Get().HeightBlendSharpness )), -1, 1) + 1) / 2;
        ctrlHeight = c1 + c2;
        return blendFactor;
    }

    void Terrain_SplatIndexed( in float2 texUV, in uint indices[4], in float weights[4],
        out float3 albedo, out float3 normal, out float roughness, out float ao, out float metal )
    {
        texUV /= 32;

        float3 albedos[4], normals[4];
        float heights[4], roughnesses[4], aos[4], metalness[4];

        // Sample materials by index
        for ( int i = 0; i < 4; i++ )
        {
            TerrainMaterial mat = g_TerrainMaterials[ i ];
            float2 layerUV = texUV * mat.uvscale;
            float2x2 uvAngle = float2x2( 1, 0, 0, 1 );

            // Apply NoTile if needed
            if ( mat.HasFlag( TerrainFlags::NoTile ) )
            {
                layerUV = Terrain_SampleSeamlessUV( layerUV, uvAngle );
            }

            Texture2D tBcr = Bindless::GetTexture2D( mat.bcr_texid );
            Texture2D tNho = Bindless::GetTexture2D( mat.nho_texid );

            float4 bcr = tBcr.Sample( g_sAnisotropic, layerUV );
            float4 nho = tNho.Sample( g_sAnisotropic, layerUV );

            float3 normal = ComputeNormalFromRGTexture( nho.rg );
            normal.xy = mul( uvAngle, normal.xy );
            normal.xz *= mat.normalstrength;
            normal = normalize( normal );

            albedos[i] = SrgbGammaToLinear( bcr.rgb );
            normals[i] = normal;
            roughnesses[i] = bcr.a;
            heights[i] = nho.b * mat.heightstrength;
            aos[i] = nho.a;
            metalness[i] = mat.metalness;
        }

        // Normalize base weights
        float sum = weights[0] + weights[1] + weights[2] + weights[3];
        if ( sum > 0 && sum != 1.0 )
        {
            float scale = 1.0 / sum;
            weights[0] *= scale;
            weights[1] *= scale;
            weights[2] *= scale;
            weights[3] *= scale;
        }

        float blendWeights[4];

        if ( Terrain::Get().HeightBlending )
        {
            // Parallel height blending (order-independent)
            // Calculate average height
            float avgHeight = (heights[0] * weights[0] + heights[1] * weights[1] +
                              heights[2] * weights[2] + heights[3] * weights[3]);

            // Modulate weights based on height differences
            float sharpness = Terrain::Get().HeightBlendSharpness * 10.0; // Scale for better control

            for ( int idx = 0; idx < 4; idx++ )
            {
                if ( weights[idx] > 0.0 )
                {
                    // Boost weight based on how much higher this material is than average
                    float heightBias = (heights[idx] - avgHeight) * sharpness;
                    blendWeights[idx] = weights[idx] * pow( 2.0, heightBias );
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
            // No height blending - use base weights directly
            blendWeights[0] = weights[0];
            blendWeights[1] = weights[1];
            blendWeights[2] = weights[2];
            blendWeights[3] = weights[3];
        }

        // Blend all materials simultaneously (order-independent)
        albedo = albedos[0] * blendWeights[0] + albedos[1] * blendWeights[1] + albedos[2] * blendWeights[2] + albedos[3] * blendWeights[3];
        normal = normals[0] * blendWeights[0] + normals[1] * blendWeights[1] + normals[2] * blendWeights[2] + normals[3] * blendWeights[3];
        roughness = roughnesses[0] * blendWeights[0] + roughnesses[1] * blendWeights[1] + roughnesses[2] * blendWeights[2] + roughnesses[3] * blendWeights[3];
        ao = aos[0] * blendWeights[0] + aos[1] * blendWeights[1] + aos[2] * blendWeights[2] + aos[3] * blendWeights[3];
        metal = metalness[0] * blendWeights[0] + metalness[1] * blendWeights[1] + metalness[2] * blendWeights[2] + metalness[3] * blendWeights[3];
    }

    /// <summary>
    /// Witcher format splatting - blends base and overlay materials with blend factor
    /// </summary>
    void Terrain_Splat( in float2 texUV, in CompactTerrainMaterial material,
        out float3 albedo, out float3 normal, out float roughness, out float ao, out float metal )
    {
        texUV /= 32;

        // Sample base material with optional seamless UVs when requested
        TerrainMaterial baseMat = g_TerrainMaterials[material.BaseTextureId];
        float2 baseUV = texUV * baseMat.uvscale;
        float2x2 baseUvAngle = float2x2( 1, 0, 0, 1 );
        float2 baseSampleUV = baseUV;

        if ( baseMat.HasFlag( TerrainFlags::NoTile ) )
        {
            baseSampleUV = Terrain_SampleSeamlessUV( baseUV, baseUvAngle );
        }
        
        float4 baseBcr = Bindless::GetTexture2D( baseMat.bcr_texid ).Sample( g_sAnisotropic, baseSampleUV );
        float4 baseNho = Bindless::GetTexture2D( baseMat.nho_texid ).Sample( g_sAnisotropic, baseSampleUV );

        float3 baseNormal = ComputeNormalFromRGTexture( baseNho.rg );
        baseNormal.xy = mul( baseUvAngle, baseNormal.xy );
        baseNormal.xz *= baseMat.normalstrength;
        baseNormal = normalize( baseNormal );

        // Sample overlay material with optional seamless UVs when requested
        TerrainMaterial overlayMat = g_TerrainMaterials[material.OverlayTextureId];
        float2 overlayUV = texUV * overlayMat.uvscale;
        float2x2 overlayUvAngle = float2x2( 1, 0, 0, 1 );
        float2 overlaySampleUV = overlayUV;

        if ( overlayMat.HasFlag( TerrainFlags::NoTile ) )
        {
            overlaySampleUV = Terrain_SampleSeamlessUV( overlayUV, overlayUvAngle );
        }
        
        float4 overlayBcr = Bindless::GetTexture2D( overlayMat.bcr_texid ).Sample( g_sAnisotropic, overlaySampleUV );
        float4 overlayNho = Bindless::GetTexture2D( overlayMat.nho_texid ).Sample( g_sAnisotropic, overlaySampleUV );

        float3 overlayNormal = ComputeNormalFromRGTexture( overlayNho.rg );
        overlayNormal.xy = mul( overlayUvAngle, overlayNormal.xy );
        overlayNormal.xz *= overlayMat.normalstrength;
        overlayNormal = normalize( overlayNormal );

        // Get normalized blend factor
        float blend = material.GetNormalizedBlend();

        // Height blending if enabled
        if ( Terrain::Get().HeightBlending )
        {
            float baseHeight = baseNho.b * baseMat.heightstrength;
            float overlayHeight = overlayNho.b * overlayMat.heightstrength;
            
            float heightDiff = overlayHeight - baseHeight;
            float sharpness = Terrain::Get().HeightBlendSharpness * 10.0;
            blend = saturate( blend + heightDiff * sharpness );
        }

        // Blend materials
        albedo = lerp( SrgbGammaToLinear( baseBcr.rgb ), SrgbGammaToLinear( overlayBcr.rgb ), blend );
        normal = lerp( baseNormal, overlayNormal, blend );
        roughness = lerp( baseBcr.a, overlayBcr.a, blend );
        ao = lerp( baseNho.a, overlayNho.a, blend );
        metal = lerp( baseMat.metalness, overlayMat.metalness, blend );
    }

	// 
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
        Texture2D tHeightMap = Bindless::GetTexture2D( Terrain::Get().HeightMapTexture );
        float2 texSize = TextureDimensions2D( tHeightMap, 0 );
        float2 uv = i.LocalPosition.xy / ( texSize * Terrain::Get().Resolution );

        // Clip any of the clipmap that exceeds the heightmap bounds
        if ( uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0 )
        {
            clip( -1 );
            return float4( 0, 0, 0, 0 );
        }

        float3 tangentU, tangentV;
        float3 geoNormal;

        // Calculate base normal from heightmap
        geoNormal = Terrain_Normal( tHeightMap, uv, Terrain::Get().HeightScale, tangentU, tangentV );

        // Transform to world space
        geoNormal = mul( Terrain::Get().Transform, float4( geoNormal, 0.0 ) ).xyz;
        tangentU = mul( Terrain::Get().Transform, float4( tangentU, 0.0 ) ).xyz;
        tangentV = mul( Terrain::Get().Transform, float4( tangentV, 0.0 ) ).xyz;

        // Re-orthonormalize in case transform had scaling
        geoNormal = normalize( geoNormal );
        tangentU = normalize( tangentU - geoNormal * dot( tangentU, geoNormal ) );
        tangentV = normalize( cross( geoNormal, tangentU ) );

        float3 albedo = float3( 1, 1, 1 );
        float3 norm = float3( 0, 0, 1 );
        float roughness = 1;
        float ao = 1;
        float metalness = 0;

    #if D_GRID
        Terrain_ProcGrid( i.LocalPosition.xy, albedo, roughness );
    #else
        // Compact format: simple base/overlay blending
        if ( Terrain::Get().ControlMapTexture != 0 )
        {
            Texture2D tControlMap = Terrain::GetControlMap();
            
            // Manual bilinear filtering using Gather4
            float2 controlTexSize;
            tControlMap.GetDimensions( controlTexSize.x, controlTexSize.y );
            float2 pixelUV = uv * controlTexSize - 0.5;
            float2 fracUV = frac( pixelUV );
            
            // Sample 4 neighboring pixels using point sampling
            float2 texelSize = 1.0 / controlTexSize;
            float2 baseUV = (floor( pixelUV ) + 0.5) / controlTexSize;
            
            float sample00 = tControlMap.Sample( g_sPointClamp, baseUV ).r;
            float sample10 = tControlMap.Sample( g_sPointClamp, baseUV + float2(texelSize.x, 0) ).r;
            float sample01 = tControlMap.Sample( g_sPointClamp, baseUV + float2(0, texelSize.y) ).r;
            float sample11 = tControlMap.Sample( g_sPointClamp, baseUV + float2(texelSize.x, texelSize.y) ).r;
            
            // Decode all 4 materials
            CompactTerrainMaterial mat00 = CompactTerrainMaterial::DecodeFromFloat( sample00 );
            CompactTerrainMaterial mat10 = CompactTerrainMaterial::DecodeFromFloat( sample10 );
            CompactTerrainMaterial mat01 = CompactTerrainMaterial::DecodeFromFloat( sample01 );
            CompactTerrainMaterial mat11 = CompactTerrainMaterial::DecodeFromFloat( sample11 );
            
            // Calculate bilinear weights
            float blend00 = (1 - fracUV.x) * (1 - fracUV.y);
            float blend10 = fracUV.x * (1 - fracUV.y);
            float blend01 = (1 - fracUV.x) * fracUV.y;
            float blend11 = fracUV.x * fracUV.y;
            
            // Check for holes - blend hole values
            float holeBlend = 0.0;
            if ( mat00.IsHole ) holeBlend += blend00;
            if ( mat10.IsHole ) holeBlend += blend10;
            if ( mat01.IsHole ) holeBlend += blend01;
            if ( mat11.IsHole ) holeBlend += blend11;
            
            // Clip if predominantly a hole
            if ( holeBlend > 0.5 )
            {
                clip( -1 );
                return float4( 0, 0, 0, 0 );
            }
            
            // Sample materials from all 4 pixels
            float3 albedo00, albedo10, albedo01, albedo11;
            float3 normal00, normal10, normal01, normal11;
            float rough00, rough10, rough01, rough11;
            float ao00, ao10, ao01, ao11;
            float metal00, metal10, metal01, metal11;
            
            Terrain_Splat( i.LocalPosition.xy, mat00, albedo00, normal00, rough00, ao00, metal00 );
            Terrain_Splat( i.LocalPosition.xy, mat10, albedo10, normal10, rough10, ao10, metal10 );
            Terrain_Splat( i.LocalPosition.xy, mat01, albedo01, normal01, rough01, ao01, metal01 );
            Terrain_Splat( i.LocalPosition.xy, mat11, albedo11, normal11, rough11, ao11, metal11 );
            
            // Bilinear blend between the 4 samples
            albedo = albedo00 * blend00 + albedo10 * blend10 + albedo01 * blend01 + albedo11 * blend11;
            norm = normal00 * blend00 + normal10 * blend10 + normal01 * blend01 + normal11 * blend11;
            roughness = rough00 * blend00 + rough10 * blend10 + rough01 * blend01 + rough11 * blend11;
            ao = ao00 * blend00 + ao10 * blend10 + ao01 * blend01 + ao11 * blend11;
            metalness = metal00 * blend00 + metal10 * blend10 + metal01 * blend01 + metal11 * blend11;
        }
    #endif

        Material p = Material::Init();
        p.Albedo = albedo;
        p.Normal = TransformNormal( norm, geoNormal, tangentU, tangentV );
        p.Roughness = roughness;
        p.Metalness = metalness;
        p.AmbientOcclusion = ao;
        p.TextureCoords = uv;

        p.WorldPosition = i.WorldPosition;
        p.WorldPositionWithOffset = i.WorldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
        p.ScreenPosition = i.ScreenPosition;

        p.WorldTangentU = tangentU;
        p.WorldTangentV = tangentV;

        if ( g_nDebugView != 0 )
        {
            // return Terrain_Debug( i.LodLevel, p.TextureCoords );
        }

	    return ShadingModelStandard::Shade( p );
	}
}
