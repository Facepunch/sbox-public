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
    // Opt out of stupid shit
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

        o.LocalPosition = Terrain_ClipmapSingleMesh( i.PositionAndLod, Terrain::GetHeightMap(), Terrain::Get().Resolution, Terrain::Get().TransformInv );

        o.LocalPosition.z *= Terrain::Get().HeightScale;

        if ( g_bVertexDisplacement )
        {
            // Calculate UV coordinates for sampling control map and material textures
            Texture2D tHeightMap = Terrain::GetHeightMap();
            float2 texSize = TextureDimensions2D( tHeightMap, 0 );
            float2 uv = o.LocalPosition.xy / ( texSize * Terrain::Get().Resolution );

            // Get material weights
            float4 control = Terrain::GetControlMap().SampleLevel( g_sBilinearBorder, uv, 0 );

            // Calculate the normal of the displacement using 3 samples
            float2 texelSize = 1.0f / texSize;
            float center = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv, 0 ).r );
            float r = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 1, 0 ), 0 ).r );
            float b = abs( tHeightMap.SampleLevel( g_sBilinearBorder, uv + texelSize * float2( 0, 1 ), 0 ).r );

            float normalStrength = Terrain::Get().HeightScale / Terrain::Get().Resolution;
            float3 geoNormal = normalize( float3( center - r, (b - center) * -1, 1.0f / normalStrength ) );

            // Transform normal to world space
            geoNormal = normalize( mul( Terrain::Get().Transform, float4( geoNormal, 0.0 ) ).xyz );

            // Blend displacement between all materials(blending)
            float totalDisplacement = 0.0f;
            for ( int matIdx = 0; matIdx < 4; matIdx++ )
            {
                float weight = control[matIdx];
                if ( weight > 0.0f )
                {
                    float2 layerUV = ( o.LocalPosition.xy / 32.0f ) * g_TerrainMaterials[matIdx].uvscale;
                    float2 seamlessUV = Terrain_SampleSeamlessUV( layerUV );

                    // Sample height from Height(blue channel) of NHO texture
                    Texture2D tNHO = Bindless::GetTexture2D( g_TerrainMaterials[matIdx].nho_texid );
                    
                    float height = tNHO.SampleLevel( g_sAnisotropic, seamlessUV, 0 ).b;
                    float centeredHeight = (height - 0.5f) * 2.0f;
					
                    // Accumulate displacement
                    totalDisplacement += centeredHeight * weight * g_TerrainMaterials[matIdx].displacementscale;
                }
            }

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

        // check for holes in vertex shader, better results and faster
        float hole = Terrain::GetHolesMap().Load( int3( o.LocalPosition.xy / Terrain::Get().Resolution, 0 ) ).r;
        if ( hole > 0.0f )
        {
            o.LocalPosition = float3( 0. / 0., 0, 0 );
            o.WorldPosition = mul( Terrain::Get().Transform, float4( o.LocalPosition, 1.0 ) ).xyz;
            o.PixelPosition = Position3WsToPs( o.WorldPosition.xyz );
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

    #include "terrain/TerrainNoTile.hlsl"

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

    void Terrain_Splat4( in float2 texUV, in float4 control,
        out float3 albedo, out float3 normal, out float roughness, out float ao, out float metal )
    {
        texUV /= 32;

        float3 albedos[4], normals[4];
        float heights[4], roughnesses[4], aos[4], metalness[4];
        for ( int i = 0; i < 4; i++ )
        {
            float2 layerUV = texUV * g_TerrainMaterials[ i ].uvscale;

            float2x2 uvRotation;
            float2 seamlessUV = Terrain_SampleSeamlessUV( layerUV, uvRotation );

            Texture2D tBcr = Bindless::GetTexture2D( g_TerrainMaterials[ i ].bcr_texid );
            Texture2D tNho = Bindless::GetTexture2D( g_TerrainMaterials[ i ].nho_texid );

            float4 bcr = tBcr.Sample( g_sAnisotropic, seamlessUV );
            float4 nho = tNho.Sample( g_sAnisotropic, seamlessUV );

            float3 normal = ComputeNormalFromRGTexture( nho.rg );
            normal.xz *= g_TerrainMaterials[ i ].normalstrength;
            
            // Rotate normal map to match seamless UV angle and avoid shading errors 
            normal.xy = mul( uvRotation, normal.xy );

            normal = normalize( normal );

            albedos[i] = SrgbGammaToLinear( bcr.rgb );
            normals[i] = normal;
            roughnesses[i] = bcr.a;
            heights[i] = nho.b * g_TerrainMaterials[ i ].heightstrength;
            aos[i] = nho.a;
            metalness[i] = g_TerrainMaterials[ i ].metalness;
        }

        float ctrlHeight1, ctrlHeight2, ctrlHeight3;
        float blend01 = HeightBlend( heights[0], heights[1], control.r, control.g, ctrlHeight1 );
        float blend12 = HeightBlend( heights[1], heights[2], ctrlHeight1, control.b, ctrlHeight2 );
        float blend23 = HeightBlend( heights[2], heights[3], ctrlHeight2, control.a, ctrlHeight3 );

        if ( Terrain::Get().HeightBlending )
        {
            // Blend Textures based on calculated blend factors
            albedo = albedos[0] * blend01 + albedos[1] * (1 - blend01);
            albedo = albedo * blend12 + albedos[2] * (1 - blend12);
            albedo = albedo * blend23 + albedos[3] * (1 - blend23);

            normal = normals[0] * blend01 + normals[1] * (1 - blend01);
            normal = normal * blend12 + normals[2] * (1 - blend12);
            normal = normal * blend23 + normals[3] * (1 - blend23);        

            roughness = roughnesses[0] * blend01 + roughnesses[1] * (1 - blend01);
            roughness = roughness * blend12 + roughnesses[2] * (1 - blend12);
            roughness = roughness * blend23 + roughnesses[3] * (1 - blend23);          

            ao = aos[0] * blend01 + aos[1] * (1 - blend01);
            ao = ao * blend12 + aos[2] * (1 - blend12);
            ao = ao * blend23 + aos[3] * (1 - blend23);            

            metal = metalness[0] * blend01 + metalness[1] * (1 - blend01);
            metal = metal * blend12 + metalness[2] * (1 - blend12);
            metal = metal * blend23 + metalness[3] * (1 - blend23);            
        }
        else
        {
            albedo = albedos[0] * control.r + albedos[1] * control.g + albedos[2] * control.b + albedos[3] * control.a;
            normal = normals[0] * control.r + normals[1] * control.g + normals[2] * control.b + normals[3] * control.a; // additive?
            roughness = roughnesses[0] * control.r + roughnesses[1] * control.g + roughnesses[2] * control.b + roughnesses[3] * control.a;
            ao = aos[0] * control.r + aos[1] * control.g + aos[2] * control.b + aos[3] * control.a;
            metal = metalness[0] * control.r + metalness[1] * control.g + metalness[2] * control.b + metalness[3] * control.a;
        }
    }

	// 
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
        Texture2D tHeightMap = Bindless::GetTexture2D( Terrain::Get().HeightMapTexture );
        Texture2D tControlMap = Bindless::GetTexture2D( Terrain::Get().ControlMapTexture );

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
        // Not adding up to 1 is invalid, but lets just give everything to the first layer
        float4 control = Terrain::GetControlMap().Sample( g_sBilinearBorder, uv );
        float sum = control.x + control.y + control.z + control.w;

        #if D_AUTO_SPLAT
        if ( sum != 1.0f )
        {
            float invsum = 1.0f - sum;
            float slope_weight = saturate( ( geoNormal.z - 0.99 ) * 100 );
            control.x += ( slope_weight ) * invsum;
            control.y += ( 1.0f - slope_weight ) * invsum;
        }
        #else
        // anything unsplatted, defualt to channel 0
        if ( sum != 1.0f ) { control.x += 1.0f - sum; }
        #endif

        Terrain_Splat4( i.LocalPosition.xy, control,
            albedo, norm, roughness, ao, metalness );
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