HEADER
{
    Description = "Light Probe Volume Debugging";
    DevShader = true;
}

FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    Forward();
    ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
    // Probe markers are absent from the scene depth used to calculate SSAO.
    #define AMBIENTOCCLUSION_HLSL
    struct ScreenSpaceAmbientOcclusion
    {
        static float Sample( float4 ScreenPosition )
        {
            return 1.0f;
        }
    };

    #include "common/shared.hlsl"

    float flSampleSize < Attribute( "SampleSize" ); Default( 4 ); >;
    bool bProbeSpheres < Attribute( "ProbeSpheres" ); Default( 1 ); >;
}

struct VertexInput
{
    #include "common/vertexinput.hlsl"
    float3 vProbePositionWs : TEXCOORD12 < Semantic( Uvwx ); >;
};

struct PixelInput
{
    #include "common/pixelinput.hlsl"
    nointerpolation float3 vProbePositionWs : TEXCOORD13;
};

VS
{
    #include "common/vertex.hlsl"

    PixelInput MainVs( VertexInput i )
    {
        PixelInput o = ProcessVertex( i );
        o.vProbePositionWs = i.vProbePositionWs;
        if ( bProbeSpheres )
        {
            // A camera-facing tangent plane encloses the sphere's projected silhouette.
            float3 vCameraToProbe = i.vProbePositionWs - g_vCameraPositionWs;
            float flDistance = length( vCameraToProbe );
            float flExtent = flSampleSize * flDistance / sqrt( max( flDistance * flDistance - flSampleSize * flSampleSize, 0.0001 ) );
            float3 vForward = vCameraToProbe / max( flDistance, 0.0001 );
            float3 vUp = abs( vForward.z ) < 0.99 ? float3( 0, 0, 1 ) : float3( 0, 1, 0 );
            float3 vRight = normalize( cross( vForward, vUp ) );
            vUp = cross( vRight, vForward );
            o.vPositionWs = i.vProbePositionWs + ( i.vTexCoord.x * vRight - i.vTexCoord.y * vUp ) * flExtent;
            o.vPositionPs = Position3WsToPs( o.vPositionWs );
        }
        return FinalizeVertex( o );
    }
}

PS
{
    #include "common/pixel.hlsl"

    RenderState( CullMode, NONE );
    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );
    RenderState( AlphaToCoverageEnable, false );
    RenderState( DepthWriteEnable, false );

    int nDDGIVolume < Attribute( "DDGIVolume" ); Default( -1 ); >;
    float flRoughness < Attribute( "Roughness" ); Default( 1 ); >;
    float flMetalness < Attribute( "Metalness" ); Default( 0 ); >;
    float3 vAlbedo < Attribute( "Albedo" ); Default3( 1, 1, 1 ); >;
    bool bLightingPreview < Attribute( "LightingPreview" ); Default( 0 ); >;

    struct ProbeOutput
    {
        float4 vColor : SV_Target0;
        float flDepth : SV_Depth;
    };

    ProbeOutput MainPs( PixelInput i )
    {
        ProbeOutput o;
        o.flDepth = i.vPositionSs.z;
        float flCoverage = 1.0;
        if ( bProbeSpheres )
        {
            float3 vRayDirection = normalize( i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz - g_vCameraPositionWs );
            float3 vCameraToProbe = i.vProbePositionWs - g_vCameraPositionWs;
            float flProjection = dot( vCameraToProbe, vRayDirection );
            // Avoid subtracting large squared distances for small, distant probes.
            float3 vClosestApproach = vRayDirection * flProjection - vCameraToProbe;
            float flDiscriminant = flSampleSize * flSampleSize - dot( vClosestApproach, vClosestApproach );
            flCoverage = smoothstep( 0.0, max( fwidth( flDiscriminant ), 0.000001 ), flDiscriminant );
            clip( flDiscriminant );
            float flHalfChord = sqrt( max( flDiscriminant, 0 ) );
            float flDistance = flProjection - flHalfChord;
            clip( flDistance );

            float3 vSurfaceOffset = vClosestApproach - vRayDirection * flHalfChord;
            float3 vHitPosition = i.vProbePositionWs + vSurfaceOffset;
            i.vPositionWithOffsetWs = vHitPosition - g_vHighPrecisionLightingOffsetWs.xyz;
            i.vNormalWs = normalize( vSurfaceOffset );
            float4 vHitPositionPs = Position3WsToPs( vHitPosition );
            o.flDepth = vHitPositionPs.z / vHitPositionPs.w;
            clip( o.flDepth );
            clip( 1 - o.flDepth );
            o.flDepth = lerp( g_flViewportMinZ, g_flViewportMaxZ, o.flDepth );
        }

        if ( !bLightingPreview )
        {
            float3 vNormalWs = normalize( i.vNormalWs );
            float3 vIrradiance = 0;
            if ( UsesBakedLightingFromProbe )
                vIrradiance = SampleLightProbeVolume( i.vProbePositionWs, vNormalWs );
            else if ( nDDGIVolume >= 0 && nDDGIVolume < (int)DDGIVolumeCount )
            {
                // Relocation stays under half a cell, so rounding recovers the probe's grid index.
                DDGIVolume volume = DDGIVolumes[nDDGIVolume];
                int3 vProbeIndex = round( ( volume.ToProbeSpace( i.vProbePositionWs ) - volume.BBoxMin ) * volume.ReciprocalSpacing );
                vIrradiance = DDGI::SampleProbeIrradiance( volume, Bindless::GetTexture3D( volume.IrradianceTextureIndex ), vProbeIndex, -vNormalWs );
                vIrradiance *= volume.RuntimeIntensity * max( volume.RuntimeTint, 0.0f );
            }
            o.vColor = float4( vIrradiance, flCoverage );
            return o;
        }

        i.vPositionWithOffsetWs = i.vProbePositionWs - g_vHighPrecisionLightingOffsetWs.xyz;
        Material m = Material::Init( i );
        m.Roughness = flRoughness;
        m.Metalness = flMetalness;
        m.Albedo = vAlbedo;
        o.vColor = ShadingModelStandard::Shade( m );
        o.vColor.a = flCoverage;
        return o;
    }
}