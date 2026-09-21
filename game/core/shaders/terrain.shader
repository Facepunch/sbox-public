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
    float g_flDisplacementFadeDist < Attribute( "DisplacementFadeDist" ); >;

    // Set per draw: shadow passes skip vertex displacement (small relief isn't worth the extra taps there).
    bool g_bTerrainShadowPass < Attribute( "TerrainShadowPass" ); Default( 0 ); >;

    // Whether any hole is painted on the terrain; hole-free terrains skip control-map work in depth passes.
    bool g_bTerrainHasHoles < Attribute( "TerrainHasHoles" ); Default( 1 ); >;
}

struct VertexInput
{
	float3 PositionAndLod : POSITION < Semantic( PosXyz ); >;
	uint InstanceID : SV_InstanceID < Semantic( InstanceTransformUv ); >;
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

        TerrainMeshlet meshlet = g_TerrainMeshlets[i.InstanceID];

        Texture2D tHeightMap = Bindless::GetTexture2D( Terrain::Get().HeightMapTexture );
        float flLodLevel;
        o.LocalPosition = Terrain_ClipmapMeshlet( i.PositionAndLod.xy, meshlet, tHeightMap, Terrain::Get().UnitsPerTexel, flLodLevel );

        o.LocalPosition.z *= Terrain::Get().HeightScale;

        // Vertex displacement, skipped in shadow passes - we want those as cheap as possible
    #if ( D_GRID == 0 )
        if ( g_bVertexDisplacement && !g_bTerrainShadowPass && Terrain::Get().ControlMapTexture != 0 )
        {
            // Fade displacement to zero by the region's edge. Measure from a snapped centre, not the continuous
            // camera, so the amount is fixed per vertex and the surface doesn't "breathe" up/down as you move.
            float2 dispCenter = roundToIncrement( g_vClipCameraLocal, Terrain::Get().UnitsPerTexel * 2.0f );
            float camDist = max( abs( o.LocalPosition.x - dispCenter.x ), abs( o.LocalPosition.y - dispCenter.y ) );
            float t = saturate( camDist / g_flDisplacementFadeDist );
            float displacementFade = 1.0 - t * t;

            if ( displacementFade > 0 )
            {
                float2 texSize = TextureDimensions2D( tHeightMap, 0 );
                float2 uv = o.LocalPosition.xy / ( texSize * Terrain::Get().UnitsPerTexel );
                CompactTerrainMaterial controlMat = CompactTerrainMaterial::DecodeFromFloat( Terrain::GetControlMap().SampleLevel( g_sPointClamp, uv, 0 ).r );

                // Sample base material displacement
                TerrainMaterial baseMat = g_TerrainMaterials[controlMat.BaseTextureId];
                SamplerState materialSampler = Bindless::GetSampler( Terrain::Get().samplerindex );
                float2 baseLayerUV = ( o.LocalPosition.xy / 32.0f ) * baseMat.uvscale;

                if( baseMat.HasFlag( TerrainFlags::NoTile ) )
                    baseLayerUV = Terrain_SampleSeamlessUV( baseLayerUV );

                float4 baseNho = Bindless::GetTexture2D( baseMat.nho_texid ).SampleLevel( materialSampler, baseLayerUV, 0 );
                float baseDisplacement = ( baseNho.b - 0.5f ) * 2.0f * baseMat.displacementscale;

                float blend = controlMat.GetNormalizedBlend();
                float totalDisplacement = baseDisplacement;

                if ( blend > 0.0f || Terrain::Get().HeightBlending )
                {
                    TerrainMaterial overlayMat = g_TerrainMaterials[controlMat.OverlayTextureId];
                    float2 overlayLayerUV = ( o.LocalPosition.xy / 32.0f ) * overlayMat.uvscale;

                    if( overlayMat.HasFlag( TerrainFlags::NoTile ) )
                        overlayLayerUV = Terrain_SampleSeamlessUV( overlayLayerUV );

                    float4 overlayNho = Bindless::GetTexture2D( overlayMat.nho_texid ).SampleLevel( materialSampler, overlayLayerUV, 0 );
                    float overlayDisplacement = ( overlayNho.b - 0.5f ) * 2.0f * overlayMat.displacementscale;

                    // Height-aware blend, matching the surface color/normal blend
                    if ( Terrain::Get().HeightBlending && baseMat.nho_texid > 0 && overlayMat.nho_texid > 0 )
                    {
                        float baseHeight = baseNho.b * baseMat.heightstrength;
                        float overlayHeight = overlayNho.b * overlayMat.heightstrength;
                        blend = Terrain_HeightBlendWeight( blend, baseHeight, overlayHeight, Terrain::Get().HeightBlendSharpness );
                    }

                    totalDisplacement = lerp( baseDisplacement, overlayDisplacement, blend );
                }

                float3 geoNormal = Terrain::SampleNormal( uv );
                o.LocalPosition.xyz += geoNormal * totalDisplacement * displacementFade;
            }
        }
    #endif

        o.WorldPosition = mul( Terrain::Get().Transform, float4( o.LocalPosition, 1.0 ) ).xyz;
        o.PixelPosition = Position3WsToPs( o.WorldPosition.xyz );
        o.LodLevel = (uint)flLodLevel;

		return o;
	}
}

//=========================================================================================================================

PS
{
    DynamicCombo( D_GRID, 0..1, Sys( ALL ) );

    #include "common/pixel.hlsl"
    #include "common/material.hlsl"
    #include "common/shadingmodel.hlsl"

	//
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
        float2 uv = Terrain::LocalToUV( i.LocalPosition.xy );

        // Clip any of the clipmap that exceeds the heightmap bounds
        if ( uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0 )
        {
            clip( -1 );
            return float4( 0, 0, 0, 0 );
        }

    #if ( S_MODE_DEPTH )
        // Hole-free terrain: depth passes only need the bounds clip above
        if ( !g_bTerrainHasHoles )
            return 1;
    #endif

    #if ( D_GRID == 0 )
        // Compact format: simple base/overlay blending
        bool bHasControlMap = Terrain::Get().ControlMapTexture != 0;
        uint4 controlBits = 0;
        float4 quadWeights = 0;

        if ( bHasControlMap )
        {
            controlBits = Terrain::GatherControlQuad( uv, quadWeights );

            // Check for holes - blend hole values
            float holeBlend = 0.0;
            if ( CompactTerrainMaterial::Decode( controlBits.x ).IsHole ) holeBlend += quadWeights.x;
            if ( CompactTerrainMaterial::Decode( controlBits.y ).IsHole ) holeBlend += quadWeights.y;
            if ( CompactTerrainMaterial::Decode( controlBits.z ).IsHole ) holeBlend += quadWeights.z;
            if ( CompactTerrainMaterial::Decode( controlBits.w ).IsHole ) holeBlend += quadWeights.w;

            // Clip if predominantly a hole
            if ( holeBlend > 0.5 )
            {
                clip( -1 );
                return float4( 0, 0, 0, 0 );
            }
        }
    #endif

    #if ( S_MODE_DEPTH )
        // Depth passes only need the clips above - keep everything Material-shaped below this line
        return 1;
    #endif

        Material p = Material::Init();
        p.TextureCoords = uv;

    #if D_GRID
        Terrain_ProcGrid( i.LocalPosition.xy, p.Albedo, p.Roughness );
    #else
        if ( bHasControlMap )
        {
            p = Terrain::Sample( i.LocalPosition.xy, ddx( i.LocalPosition.xy ), ddy( i.LocalPosition.xy ), controlBits, quadWeights );
        }
    #endif

        Terrain::ApplyGeometricNormals( p, uv );

        p.WorldPosition = i.WorldPosition;
        p.WorldPositionWithOffset = i.WorldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
        p.ScreenPosition = i.ScreenPosition;

        if ( g_nDebugView != 0 )
        {
            // return Terrain_Debug( i.LodLevel, p.TextureCoords );
        }

	    return ShadingModelStandard::Shade( p );
	}
}
