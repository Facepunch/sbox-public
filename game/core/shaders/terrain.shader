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

    // Hole-free terrains skip the control-map gather in the vertex shader.
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
        // Positive keeps the vertex. The rasterizer cuts the triangle; the pixel shader does not discard.
        float ClipDistance : SV_ClipDistance0;
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

        // Outside the heightmap, and painted holes. Both are a signed distance so the
        // rasterizer cuts the triangle instead of the pixel shader discarding.
        float2 uv = Terrain::LocalToUV( o.LocalPosition.xy );
        float flKeep = min( min( uv.x, 1.0 - uv.x ), min( uv.y, 1.0 - uv.y ) );

    #if ( D_GRID == 0 )
        if ( g_bTerrainHasHoles && Terrain::Get().ControlMapTexture != 0 && flKeep >= 0.0 )
        {
            float4 holeWeights;
            uint4 holeBits = Terrain::GatherControlQuad( uv, holeWeights );
            float holeBlend = 0.0;
            if ( CompactTerrainMaterial::Decode( holeBits.x ).IsHole ) holeBlend += holeWeights.x;
            if ( CompactTerrainMaterial::Decode( holeBits.y ).IsHole ) holeBlend += holeWeights.y;
            if ( CompactTerrainMaterial::Decode( holeBits.z ).IsHole ) holeBlend += holeWeights.z;
            if ( CompactTerrainMaterial::Decode( holeBits.w ).IsHole ) holeBlend += holeWeights.w;
            flKeep = min( flKeep, 0.5 - holeBlend );
        }
    #endif

        o.WorldPosition = mul( Terrain::Get().Transform, float4( o.LocalPosition, 1.0 ) ).xyz;
        o.PixelPosition = Position3WsToPs( o.WorldPosition.xyz );
        o.ClipDistance = flKeep;
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

    #if ( D_GRID == 0 )
        bool bHasControlMap = Terrain::Get().ControlMapTexture != 0;
        uint4 controlBits = 0;
        float4 quadWeights = 0;

        if ( bHasControlMap )
            controlBits = Terrain::GatherControlQuad( uv, quadWeights );
    #endif

        Material p = Material::Init();
        p.TextureCoords = uv;

    #if D_GRID
        Terrain_ProcGrid( i.LocalPosition.xy, p.Albedo, p.Roughness );
    #else
        if ( bHasControlMap )
            p = Terrain::Sample( i.LocalPosition.xy, ddx( i.LocalPosition.xy ), ddy( i.LocalPosition.xy ), controlBits, quadWeights );
    #endif

        Terrain::ApplyGeometricNormals( p, uv );

        p.WorldPosition = i.WorldPosition;
        p.WorldPositionWithOffset = i.WorldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
        p.ScreenPosition = i.ScreenPosition;

        // The prepass only stores normal and roughness. Lighting the pixel and discarding the color
        // was most of this pass.
    #if ( S_MODE_DEPTH )
        Decals::Apply( p.WorldPosition, p );
        return DepthNormals::Output( p.Normal, p.Roughness, p.Opacity );
    #endif

	    return ShadingModelStandard::Shade( p );
	}
}
