HEADER
{
    DevShader = true;
    Description = "Clustered light culling compute shader.";
}

MODES
{
    Default();
}

FEATURES
{
}

COMMON
{
    #define CLUSTERED_LIGHT_CULLING_CS
    #define MINIMAL_MATERIAL

    #include "system.fxc"
    #include "vr_common.fxc"
    #include "common/classes/_classes.hlsl"
    #include "common/classes/Decals.hlsl"
}

CS
{
    bool IsLightVisible( uint index, Frustum frustum )
    {
        BinnedLight light = BinnedLightBuffer[index];

        return light.IsSpotLight()
            ? frustum.ConeInside( light.GetPosition(), light.GetDirection(), light.GetDirectionUp(), light.GetRadius(), light.SpotLightInnerOuterConeCosines.y )
            : frustum.SphereInside( light.GetPosition(), light.GetRadius() );
    }

    bool IsEnvMapVisible( uint index, Frustum frustum )
    {
        float feather = max( EnvMapFeathering( index ), 0.0f );
        float3 mins = EnvMapBoxMins( index ) - feather;
        float3 maxs = EnvMapBoxMaxs( index ) + feather;

        return frustum.AABBInside( mins, maxs, EnvMapWorldToLocal( index ) );
    }

    bool IsDecalVisible( uint index, Frustum frustum )
    {
        Decal decal = DecalBuffer[index];
        return frustum.SphereInside( decal.GetCenter(), decal.GetRadius() );
    }

    [numthreads( 8, 8, 8 )]
    void MainCs( uint3 vThreadId : SV_DispatchThreadID )
    {
        if ( any( vThreadId >= uint3( ClusterCounts.xyz ) ) )
            return;

        uint flatIndex = Cluster::Flatten( vThreadId );

        float2 depthRange = float2(
            Cluster::SliceToDepth( vThreadId.z ),
            Cluster::SliceToDepth( vThreadId.z + 1 )
        );

        Frustum frustum = Frustum::FromClusterTile( vThreadId.xy, depthRange, ClusterInvCounts.xy );

        [unroll]
        for ( uint type = 0; type < ClusterItemType_Count; type++ )
        {
            uint itemCount, capacity, baseOffset;

            switch ( type )
            {
                case ClusterItemType_Light:  itemCount = NumDynamicLights;   break;
                case ClusterItemType_EnvMap: itemCount = NumEnvironmentMaps; break;
                case ClusterItemType_Decal:  itemCount = NumDecals;          break;
                default:                    itemCount = 0;                   return;
            }

            capacity = Cluster::Capacity( (ClusterItemType)type );
            baseOffset = Cluster::BaseOffset( (ClusterItemType)type, flatIndex );

            uint count = 0;
            for ( uint i = 0; i < itemCount && count < capacity; i++ )
            {
                bool visible = false;
                switch ( type )
                {
                    case ClusterItemType_Light:  visible = IsLightVisible( i, frustum );  break;
                    case ClusterItemType_EnvMap: visible = IsEnvMapVisible( i, frustum ); break;
                    case ClusterItemType_Decal:  visible = IsDecalVisible( i, frustum );  break;
                }

                if ( visible )
                {
                    switch ( type )
                    {
                        case ClusterItemType_Light:  ClusterLightIndices[baseOffset + count]  = i; break;
                        case ClusterItemType_EnvMap: ClusterEnvMapIndices[baseOffset + count] = i; break;
                        case ClusterItemType_Decal:  ClusterDecalIndices[baseOffset + count]  = i; break;
                    }
                    count++;
                }
            }

            switch ( type )
            {
                case ClusterItemType_Light:  ClusterLightCounts[flatIndex]  = count; break;
                case ClusterItemType_EnvMap: ClusterEnvMapCounts[flatIndex] = count; break;
                case ClusterItemType_Decal:  ClusterDecalCounts[flatIndex]  = count; break;
            }
        }
    }
}
