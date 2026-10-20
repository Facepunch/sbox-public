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
    // A group is a 4x4x4 block of clusters: one wave64, or two wave32. Each batch of items is loaded once per
    // group and tested against the block's box; only the survivors reach the per-cluster test. Survivors are
    // published as a bitmask and walked lowest bit first, so every cluster's list stays sorted by item index,
    // which the scalarized light loop in the pixel shader depends on.
    #define GROUP_SIZE_X 4
    #define GROUP_SIZE_Y 4
    #define GROUP_SIZE_Z 4
    #define GROUP_SIZE ( GROUP_SIZE_X * GROUP_SIZE_Y * GROUP_SIZE_Z ) // two mask words, see PublishCandidates

    groupshared float4 SharedSphere[GROUP_SIZE]; // bounding sphere in cluster space, w = radius
    groupshared float4 SharedApex[GROUP_SIZE];   // spot apex in cluster space, w = range
    groupshared float4 SharedAxis[GROUP_SIZE];   // spot axis in cluster space, w = outer cone cosine, below -1 for no cone
    groupshared uint SharedFeatures[GROUP_SIZE]; // ClusterFeature bits the light adds to every cluster it reaches
    groupshared uint SharedMask[2][2];           // survivors, double-buffered by batch parity

    //
    // Clusters are boxes in an orthonormal camera basis, x right, y up, z forward depth. Items are moved into the
    // same basis, so the test is a sphere against an axis-aligned box: exact, unlike six frustum planes, which
    // pass spheres near a cluster's corners.
    //
    struct ClusterSpace
    {
        float3 Right;
        float3 Up;
        float3 Forward;
        bool Ortho;

        static ClusterSpace Create()
        {
            ClusterSpace space;
            space.Forward = normalize( g_vCameraDirWs.xyz );
            space.Up = normalize( g_vCameraUpDirWs.xyz - space.Forward * dot( g_vCameraUpDirWs.xyz, space.Forward ) );
            space.Right = cross( space.Forward, space.Up );
            // Same test as Cluster::Query: an ortho projection keeps w = 1
            space.Ortho = g_matViewToProjection[3].w != 0;
            return space;
        }

        float3 ToSpace( float3 vectorWs ) { return float3( dot( vectorWs, Right ), dot( vectorWs, Up ), dot( vectorWs, Forward ) ); }
        float3 PointToSpace( float3 positionWs ) { return ToSpace( positionWs - g_vCameraPositionWs ); }
        float3 PointToWorld( float3 position ) { return g_vCameraPositionWs + Right * position.x + Up * position.y + Forward * position.z; }

        // A point on the ray through this viewport uv, in cluster space. Perspective rays scale with depth.
        float3 RayPoint( float2 uv )
        {
            float2 ndc = float2( uv.x, 1.0f - uv.y ) * 2.0f - 1.0f;
            float4 positionHs = mul( g_matProjectionToWorld, float4( ndc, 1.0f, 1.0f ) );
            return ToSpace( positionHs.xyz / positionHs.w );
        }

        float2 AtDepth( float3 rayPoint, float depth )
        {
            return Ortho ? rayPoint.xy : rayPoint.xy * ( depth / rayPoint.z );
        }

        // The box around viewport uvs [uvMin, uvMax] between two depths
        void Bounds( float2 uvMin, float2 uvMax, float depthNear, float depthFar, out float3 boxMin, out float3 boxMax )
        {
            float3 r0 = RayPoint( uvMin );
            float3 r1 = RayPoint( float2( uvMax.x, uvMin.y ) );
            float3 r2 = RayPoint( float2( uvMin.x, uvMax.y ) );
            float3 r3 = RayPoint( uvMax );

            float2 a = AtDepth( r0, depthNear ), b = AtDepth( r1, depthNear ), c = AtDepth( r2, depthNear ), d = AtDepth( r3, depthNear );
            float2 e = AtDepth( r0, depthFar ), f = AtDepth( r1, depthFar ), g = AtDepth( r2, depthFar ), h = AtDepth( r3, depthFar );

            float2 lo = min( min( min( a, b ), min( c, d ) ), min( min( e, f ), min( g, h ) ) );
            float2 hi = max( max( max( a, b ), max( c, d ) ), max( max( e, f ), max( g, h ) ) );
            boxMin = float3( lo, depthNear );
            boxMax = float3( hi, depthFar );
        }
    };

    struct ClusterBox
    {
        float3 Min;
        float3 Max;
        float4 Sphere;

        static ClusterBox From( ClusterSpace space, uint3 first, uint3 last )
        {
            ClusterBox box;
            float2 uvMin = float2( first.xy ) * ClusterInvCounts.xy;
            float2 uvMax = float2( last.xy + 1 ) * ClusterInvCounts.xy;
            space.Bounds( uvMin, uvMax, Cluster::SliceToDepth( first.z ), Cluster::SliceToDepth( last.z + 1 ), box.Min, box.Max );
            box.Sphere = float4( ( box.Min + box.Max ) * 0.5f, length( box.Max - box.Min ) * 0.5f );
            return box;
        }

        bool TouchesSphere( float4 sphere )
        {
            float3 outside = max( 0.0f, max( Min - sphere.xyz, sphere.xyz - Max ) );
            return dot( outside, outside ) <= sphere.w * sphere.w;
        }

        // A spot's cone against this box's bounding sphere (Wronski, "Cull that cone!", 2017).
        // Only meaningful under 90 degrees; wider cones are left to the bounding sphere.
        bool TouchesCone( float4 apex, float4 axis )
        {
            if ( axis.w <= 0.0f )
                return true;

            float3 v = Sphere.xyz - apex.xyz;
            float alongAxis = dot( v, axis.xyz );
            float acrossAxis = sqrt( max( dot( v, v ) - alongAxis * alongAxis, 0.0f ) );
            float sine = sqrt( max( 1.0f - axis.w * axis.w, 0.0f ) );
            float distanceToCone = axis.w * acrossAxis - alongAxis * sine;

            return distanceToCone <= Sphere.w && alongAxis <= Sphere.w + apex.w && alongAxis >= -Sphere.w;
        }

        bool TouchesLight( float4 sphere, float4 apex, float4 axis )
        {
            return TouchesSphere( sphere ) && TouchesCone( apex, axis );
        }

        // An envmap's box, in its own space. The cluster's sphere moves into that space with the largest stretch
        // the matrix can give it (bounded by its Frobenius norm), so a scaled envmap is never missed.
        bool TouchesEnvMap( ClusterSpace space, uint index )
        {
            float4x3 worldToLocal = EnvMapWorldToLocal( index );
            float feather = max( EnvMapFeathering( index ), 0.0f );
            float3 boxMin = EnvMapBoxMins( index ) - feather;
            float3 boxMax = EnvMapBoxMaxs( index ) + feather;

            float3 center = mul( float4( space.PointToWorld( Sphere.xyz ), 1.0f ), worldToLocal ).xyz;
            float stretch = sqrt( dot( worldToLocal[0], worldToLocal[0] ) + dot( worldToLocal[1], worldToLocal[1] ) + dot( worldToLocal[2], worldToLocal[2] ) );
            float radius = Sphere.w * stretch;

            float3 outside = max( 0.0f, max( boxMin - center, center - boxMax ) );
            return dot( outside, outside ) <= radius * radius;
        }
    };

    //
    // Each item becomes a bounding sphere and, for spots, a cone, in cluster space
    //
    void LightBounds( ClusterSpace space, uint index, out float4 sphere, out float4 apex, out float4 axis )
    {
        BinnedLight light = BinnedLightBufferV2[index];
        float3 position = space.PointToSpace( light.GetPosition() );
        float range = light.Radius;
        float outerCos = light.SpotLightInnerOuterConeCosines.y;

        sphere = float4( position, range );
        apex = float4( position, range );
        axis = float4( space.ToSpace( light.GetDirection() ), -2.0f );

        if ( light.IsSpotLight() && outerCos > 0.0f )
        {
            axis.w = outerCos;

            // The smallest sphere around the cone capped by the light's range
            if ( outerCos >= 0.70710678f )
            {
                float radius = range / ( 2.0f * outerCos );
                sphere = float4( position + axis.xyz * radius, radius );
            }
            else
            {
                float sine = sqrt( 1.0f - outerCos * outerCos );
                sphere = float4( position + axis.xyz * ( range * outerCos ), range * sine );
            }
        }
    }

    // ClusterFeature bits a light adds to every cluster it reaches
    uint LightFeatures( uint index )
    {
        return BinnedLightBufferV2[index].LightModulatorCount > 0 ? ClusterFeature_LightModulators : 0;
    }

    float4 DecalBounds( ClusterSpace space, uint index )
    {
        Decal decal = DecalBuffer[index];
        return float4( space.PointToSpace( decal.GetCenter() ), decal.GetRadius() );
    }

    //
    // Batches: every thread fills one record and says whether it touches the group's box; after the barrier
    // the survivors are read back as a 64-bit mask. A batch's mask is cleared during the batch before it uses it
    // again (parity), so a batch needs two barriers: this one, and EndBatch once the records have been read.
    //
    uint2 PublishCandidates( uint batch, uint groupIndex, bool candidate )
    {
        uint parity = batch & 1;
        if ( groupIndex < 2 )
            SharedMask[parity ^ 1][groupIndex] = 0;
        if ( candidate )
            InterlockedOr( SharedMask[parity][groupIndex >> 5], 1u << ( groupIndex & 31 ) );

        GroupMemoryBarrierWithGroupSync();
        return uint2( SharedMask[parity][0], SharedMask[parity][1] );
    }

    void EndBatch()
    {
        GroupMemoryBarrierWithGroupSync();
    }

    // The lowest set bit of a 64-bit mask, cleared. The mask is the same in every thread, so the loop is uniform.
    uint PopLowest( inout uint2 mask )
    {
        if ( mask.x != 0 )
        {
            uint bit = firstbitlow( mask.x );
            mask.x &= mask.x - 1;
            return bit;
        }

        uint bit = firstbitlow( mask.y );
        mask.y &= mask.y - 1;
        return 32 + bit;
    }

    [numthreads( GROUP_SIZE_X, GROUP_SIZE_Y, GROUP_SIZE_Z )]
    void MainCs( uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupId : SV_GroupID, uint nGroupIndex : SV_GroupIndex )
    {
        // No early return: every thread has to reach the barriers
        const uint3 counts = uint3( ClusterCounts.xyz );
        const bool active = all( vThreadId < counts );
        const uint flatIndex = active ? Cluster::Flatten( vThreadId ) : 0;

        ClusterSpace space = ClusterSpace::Create();

        const uint3 groupFirst = vGroupId * uint3( GROUP_SIZE_X, GROUP_SIZE_Y, GROUP_SIZE_Z );
        const uint3 groupLast = min( groupFirst + uint3( GROUP_SIZE_X - 1, GROUP_SIZE_Y - 1, GROUP_SIZE_Z - 1 ), counts - 1 );
        ClusterBox groupBox = ClusterBox::From( space, groupFirst, groupLast );

        const uint3 clusterCoord = min( vThreadId, counts - 1 );
        ClusterBox clusterBox = ClusterBox::From( space, clusterCoord, clusterCoord );

        if ( nGroupIndex < 2 )
            SharedMask[0][nGroupIndex] = 0;
        GroupMemoryBarrierWithGroupSync();

        uint batch = 0;

        //
        // Lights
        //
        {
            const uint itemCount = NumDynamicLights;
            const uint capacity = Cluster::Capacity( ClusterItemType_Light );
            const uint baseOffset = flatIndex * capacity;
            uint count = 0;
            uint features = 0;

            for ( uint first = 0; first < itemCount; first += GROUP_SIZE, batch++ )
            {
                const uint item = first + nGroupIndex;
                bool candidate = false;

                if ( item < itemCount )
                {
                    float4 sphere, apex, axis;
                    LightBounds( space, item, sphere, apex, axis );
                    SharedSphere[nGroupIndex] = sphere;
                    SharedApex[nGroupIndex] = apex;
                    SharedAxis[nGroupIndex] = axis;
                    SharedFeatures[nGroupIndex] = LightFeatures( item );
                    candidate = groupBox.TouchesLight( sphere, apex, axis );
                }

                uint2 mask = PublishCandidates( batch, nGroupIndex, candidate );

                [loop]
                while ( any( mask != 0 ) )
                {
                    const uint slot = PopLowest( mask );
                    if ( active && count < capacity && clusterBox.TouchesLight( SharedSphere[slot], SharedApex[slot], SharedAxis[slot] ) )
                    {
                        ClusterLightIndices[baseOffset + count] = first + slot;
                        features |= SharedFeatures[slot];
                        count++;
                    }
                }

                EndBatch();
            }

            if ( active )
                ClusterLightCounts[flatIndex] = count | ( features << ClusterFeatureShift );
        }

        //
        // Decals
        //
        {
            const uint itemCount = NumDecals;
            const uint capacity = Cluster::Capacity( ClusterItemType_Decal );
            const uint baseOffset = flatIndex * capacity;
            uint count = 0;

            for ( uint first = 0; first < itemCount; first += GROUP_SIZE, batch++ )
            {
                const uint item = first + nGroupIndex;
                bool candidate = false;

                if ( item < itemCount )
                {
                    float4 sphere = DecalBounds( space, item );
                    SharedSphere[nGroupIndex] = sphere;
                    candidate = groupBox.TouchesSphere( sphere );
                }

                uint2 mask = PublishCandidates( batch, nGroupIndex, candidate );

                [loop]
                while ( any( mask != 0 ) )
                {
                    const uint slot = PopLowest( mask );
                    if ( active && count < capacity && clusterBox.TouchesSphere( SharedSphere[slot] ) )
                    {
                        ClusterDecalIndices[baseOffset + count] = first + slot;
                        count++;
                    }
                }

                EndBatch();
            }

            if ( active )
                ClusterDecalCounts[flatIndex] = count;
        }

        //
        // Envmaps: few, and too big to stage. The index is the same in every thread, so the reads are scalar.
        //
        if ( active )
        {
            const uint itemCount = NumEnvironmentMaps;
            const uint capacity = Cluster::Capacity( ClusterItemType_EnvMap );
            const uint baseOffset = flatIndex * capacity;
            uint count = 0;

            for ( uint i = 0; i < itemCount && count < capacity; i++ )
            {
                if ( groupBox.TouchesEnvMap( space, i ) && clusterBox.TouchesEnvMap( space, i ) )
                {
                    ClusterEnvMapIndices[baseOffset + count] = i;
                    count++;
                }
            }

            ClusterEnvMapCounts[flatIndex] = count;
        }
    }
}
