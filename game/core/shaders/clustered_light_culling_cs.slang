// clustered_light_culling_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// HEADER: DevShader = true;
// HEADER: Description = "Clustered light culling compute shader.";

#include "sbox/pass.slangh"

Mode( Default );

    #define CLUSTERED_LIGHT_CULLING_CS
    #define MINIMAL_MATERIAL

    #include "sbox/system.slangh"
    #include "sbox/shading.slangh"
    #include "sbox/classes.slangh"
    #include "sbox/classes/decals.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
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
    groupshared float4 SharedEnvMap[5][GROUP_SIZE]; // an envmap's EnvMapBounds (Store, Staged)

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

    // What the culling reads of an envmap, carried from cluster space into its own: where cluster space's origin (the
    // camera) and its three axes land there (so a point there is Origin + Right * x + Up * y + Forward * z, the camera
    // basis and the envmap's matrix composed once per envmap rather than once per cluster), its box there, its feathering,
    // and its matrix's Frobenius norm (the most it can stretch a length). Staged in groupshared a batch at a time.
    struct EnvMapBounds
    {
        float3 Origin;
        float3 Right;
        float3 Up;
        float3 Forward;
        float3 BoxMin;
        float3 BoxMax;
        float Feathering;
        float Stretch;

        static EnvMapBounds Load( ClusterSpace space, uint index )
        {
            float4x3 m = EnvMapWorldToLocal( index );
            EnvMapBounds envMap;
            envMap.Origin = mul( float4( g_vCameraPositionWs, 1.0f ), m ).xyz;
            envMap.Right = mul( float4( space.Right, 0.0f ), m ).xyz;
            envMap.Up = mul( float4( space.Up, 0.0f ), m ).xyz;
            envMap.Forward = mul( float4( space.Forward, 0.0f ), m ).xyz;
            envMap.BoxMin = EnvMapBoxMins( index );
            envMap.BoxMax = EnvMapBoxMaxs( index );
            envMap.Feathering = EnvMapFeathering( index );
            envMap.Stretch = sqrt( dot( m[0], m[0] ) + dot( m[1], m[1] ) + dot( m[2], m[2] ) );
            return envMap;
        }

        // A point in cluster space, in the envmap's
        float3 ToLocal( float3 position ) { return Origin + Right * position.x + Up * position.y + Forward * position.z; }

        void Store( uint slot )
        {
            SharedEnvMap[0][slot] = float4( Right, Feathering );
            SharedEnvMap[1][slot] = float4( Up, Stretch );
            SharedEnvMap[2][slot] = float4( Forward, BoxMax.z );
            SharedEnvMap[3][slot] = float4( Origin, BoxMin.x );
            SharedEnvMap[4][slot] = float4( BoxMin.yz, BoxMax.xy );
        }

        static EnvMapBounds Staged( uint slot )
        {
            float4 a = SharedEnvMap[0][slot], b = SharedEnvMap[1][slot], c = SharedEnvMap[2][slot], d = SharedEnvMap[3][slot], e = SharedEnvMap[4][slot];
            EnvMapBounds envMap;
            envMap.Right = a.xyz;
            envMap.Up = b.xyz;
            envMap.Forward = c.xyz;
            envMap.Origin = d.xyz;
            envMap.BoxMin = float3( d.w, e.xy );
            envMap.BoxMax = float3( e.zw, c.w );
            envMap.Feathering = a.w;
            envMap.Stretch = b.w;
            return envMap;
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
        bool TouchesEnvMap( EnvMapBounds envMap )
        {
            float feather = max( envMap.Feathering, 0.0f );
            float3 boxMin = envMap.BoxMin - feather;
            float3 boxMax = envMap.BoxMax + feather;

            float3 center = envMap.ToLocal( Sphere.xyz );
            float radius = Sphere.w * envMap.Stretch;

            float3 outside = max( 0.0f, max( boxMin - center, center - boxMax ) );
            return dot( outside, outside ) <= radius * radius;
        }

        // The same box against this one carried into the envmap's space whole (its centre, and its half extents through
        // the matrix's absolute values): far tighter than the sphere for the long clusters far away, whose spheres reach
        // envmaps none of their pixels is near. Grown as the pixel shader's cube loop takes an envmap: up to its
        // feathering and half a unit outside its box on each axis, plus a unit for rounding
        bool TouchesEnvMapBox( EnvMapBounds envMap )
        {
            float grow = max( envMap.Feathering, 0.0f ) + 1.5f;
            float3 boxMin = envMap.BoxMin - grow;
            float3 boxMax = envMap.BoxMax + grow;

            float3 halfSize = ( Max - Min ) * 0.5f;
            float3 center = envMap.ToLocal( ( Min + Max ) * 0.5f );
            float3 extent = abs( envMap.Right ) * halfSize.x + abs( envMap.Up ) * halfSize.y + abs( envMap.Forward ) * halfSize.z;

            return all( center + extent >= boxMin ) && all( center - extent <= boxMax );
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
    [shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupId : SV_GroupID, uint nGroupIndex : SV_GroupIndex )
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

        // The cluster's pixels for the envmaps' box test: a pixel takes its quad's top-left pixel's tile (Cluster::ComputeIndex),
        // so two pixels wider each way, and its own depth slice, a percent deeper each way for the logarithm's rounding
        ClusterBox pixelBox;
        {
            float2 pad = 2.0f * ClusterScreenParams.zw;
            float2 uvMin = float2( clusterCoord.xy ) * ClusterInvCounts.xy - pad;
            float2 uvMax = float2( clusterCoord.xy + 1 ) * ClusterInvCounts.xy + pad;
            space.Bounds( uvMin, uvMax, Cluster::SliceToDepth( clusterCoord.z ) * 0.99f, Cluster::SliceToDepth( clusterCoord.z + 1 ) * 1.01f, pixelBox.Min, pixelBox.Max );
            pixelBox.Sphere = 0;
        }

        // The group's, padded the same: it holds every cluster's pixel box, so an envmap it misses no cluster of the group takes
        ClusterBox groupPixelBox;
        {
            float2 pad = 2.0f * ClusterScreenParams.zw;
            float2 uvMin = float2( groupFirst.xy ) * ClusterInvCounts.xy - pad;
            float2 uvMax = float2( groupLast.xy + 1 ) * ClusterInvCounts.xy + pad;
            space.Bounds( uvMin, uvMax, Cluster::SliceToDepth( groupFirst.z ) * 0.99f, Cluster::SliceToDepth( groupLast.z + 1 ) * 1.01f, groupPixelBox.Min, groupPixelBox.Max );
            groupPixelBox.Sphere = 0;
        }

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
            const uint baseOffset = Cluster::IndexBase( ClusterItemType_Light, flatIndex );
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
                        ClusterItemIndices[baseOffset + count] = first + slot;
                        features |= SharedFeatures[slot];
                        count++;
                    }
                }

                EndBatch();
            }

            if ( active )
                ClusterItemCounts[Cluster::CountSlot( ClusterItemType_Light, flatIndex )] = count | ( features << ClusterFeatureShift );
        }

        //
        // Decals
        //
        {
            const uint itemCount = NumDecals;
            const uint capacity = Cluster::Capacity( ClusterItemType_Decal );
            const uint baseOffset = Cluster::IndexBase( ClusterItemType_Decal, flatIndex );
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
                        ClusterItemIndices[baseOffset + count] = first + slot;
                        count++;
                    }
                }

                EndBatch();
            }

            if ( active )
                ClusterItemCounts[Cluster::CountSlot( ClusterItemType_Decal, flatIndex )] = count;
        }

        //
        // Envmaps, as the decals: each batch staged once per group and tested against the group's box, the survivors against
        // each cluster's sphere and then its pixels' box, lowest index first so the list stays sorted
        //
        {
            const uint itemCount = NumEnvironmentMaps;
            const bool boxTest = ClusterCapacitiesVec.w != 0;
            const uint capacity = Cluster::Capacity( ClusterItemType_EnvMap );
            const uint baseOffset = Cluster::IndexBase( ClusterItemType_EnvMap, flatIndex );
            uint count = 0;

            for ( uint first = 0; first < itemCount; first += GROUP_SIZE, batch++ )
            {
                const uint item = first + nGroupIndex;
                bool candidate = false;

                if ( item < itemCount )
                {
                    EnvMapBounds envMap = EnvMapBounds::Load( space, item );
                    envMap.Store( nGroupIndex );
                    candidate = groupBox.TouchesEnvMap( envMap ) && ( !boxTest || groupPixelBox.TouchesEnvMapBox( envMap ) );
                }

                uint2 mask = PublishCandidates( batch, nGroupIndex, candidate );

                [loop]
                while ( any( mask != 0 ) )
                {
                    const uint slot = PopLowest( mask );
                    if ( active && count < capacity )
                    {
                        EnvMapBounds envMap = EnvMapBounds::Staged( slot );
                        if ( clusterBox.TouchesEnvMap( envMap ) && ( !boxTest || pixelBox.TouchesEnvMapBox( envMap ) ) )
                        {
                            ClusterItemIndices[baseOffset + count] = first + slot;
                            count++;
                        }
                    }
                }

                EndBatch();
            }

            if ( active )
                ClusterItemCounts[Cluster::CountSlot( ClusterItemType_EnvMap, flatIndex )] = count;
        }
    }

#endif // CS

