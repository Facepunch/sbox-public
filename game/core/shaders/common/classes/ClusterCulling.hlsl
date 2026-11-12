#ifndef COMMON_CLASSES_CLUSTER_CULLING_HLSL
#define COMMON_CLASSES_CLUSTER_CULLING_HLSL

#include "common.fxc"

// The view's cluster lists, one buffer of counts and one of indices, a section per item type (lights, envmaps, decals)
// over every cluster: as few descriptors as the view set can hold. The culling shader writes them, so binds them
// itself; everything else reads them from the view's descriptor set (common.fxc).
#ifdef CLUSTERED_LIGHT_CULLING_CS
RWStructuredBuffer<uint> ClusterItemCounts  < Attribute( "ClusterItemCounts" ); >;
RWStructuredBuffer<uint> ClusterItemIndices < Attribute( "ClusterItemIndices" ); >;
#else
StructuredBuffer<uint> ClusterItemCounts      EXTERNAL_DESC_SET( t, g_viewSet, 6 );
StructuredBuffer<uint> ClusterItemIndices     EXTERNAL_DESC_SET( t, g_viewSet, 7 );
#endif

// The view's cluster grid: ClusteredLightingConstants' block of its environment
#include "common/ViewEnvironment.hlsl"

enum ClusterItemType
{
    ClusterItemType_Light,
    ClusterItemType_EnvMap,
    ClusterItemType_Decal
};

// Features some light in a cluster needs, so shaders can pick a light loop once instead of testing per light.
// They share the cluster's light count word: the count in the low 16 bits, these above it.
static const uint ClusterFeature_LightModulators = 1;

static const uint ClusterFeatureShift = 16;
static const uint ClusterCountMask = 0xFFFF;

struct ClusterRange
{
    ClusterItemType Type;
    uint Count;
    uint BaseOffset;
    uint Features; // ClusterFeature bits, lights only

    // ClusterForEach state
    uint Taken;     // Items this lane has taken
    uint Pending;   // This lane's next item, ~0 when done
    uint Item;      // The wave's current item
};

struct Cluster
{
    static float SliceToDepth( float slice ) { return exp( ( slice - ClusterZParams.y ) / ClusterZParams.x ); }
    static float DepthToSlice( float depth ) { return ClusterZParams.x * log( depth ) + ClusterZParams.y; }
    static uint Capacity( ClusterItemType type ) { return uint( ClusterCapacitiesVec[uint( type )] ); }

    // Where a cluster's count of a type is in ClusterItemCounts: the type's section, then the cluster
    static uint CountSlot( ClusterItemType type, uint flatIndex ) { return uint( type ) * uint( ClusterCounts.w ) + flatIndex; }

    // Where a cluster's items of a type start in ClusterItemIndices: the sections before the type's, then the
    // cluster's capacity-sized run
    static uint IndexBase( ClusterItemType type, uint flatIndex )
    {
        uint total = uint( ClusterCounts.w );
        uint section = 0;
        if ( type != ClusterItemType_Light ) section += total * Capacity( ClusterItemType_Light );
        if ( type == ClusterItemType_Decal ) section += total * Capacity( ClusterItemType_EnvMap );
        return section + flatIndex * Capacity( type );
    }

    static uint Flatten( uint3 coord )
    {
        uint3 d = max( uint3( ClusterCounts.xyz ), 1 );
        return coord.x + d.x * ( coord.y + d.y * coord.z );
    }

    // The flattened cluster a pixel falls in. A shader that queries more than one item type should take this once
    // and pass it to each query: otherwise the pieces of the coordinate stay live from the first query to the last
    static uint Index( float4 positionSs )
    {
        // Snap positionSs to the top-left pixel of the 2x2 quad to avoid cluster divergence
        // Todo: Z still diverges within the quad and cant derive it without reading from neighbour lanes
        // Using QuadReadLaneAt causes issues on AMD GCN ( RX580 )
        positionSs.xy = floor( positionSs.xy / 2.0 ) * 2.0 + 0.5;

        // Screen position -> cluster coordinate
        float2 uv = CalculateViewportUv( positionSs.xy );
        
        // Perspective: SV_Position.w = 1/viewDepth, so depth = 1/w.
        // Ortho: SV_Position.w is always 1.0, so un-project clip-space Z instead.
        // Uniform branch = free
        float depth;
        if ( g_matViewToProjection[3].w != 0 )
        {
            float4 vViewPos = mul( g_matProjectionToView, float4( 0.0, 0.0, positionSs.z, 1.0 ) );
            depth = -( vViewPos.z / vViewPos.w );
        }
        else
        {
            depth = 1.0 / positionSs.w;
        }

        depth = clamp( depth, ClusterZParams.z, ClusterZParams.w );
        uint3 d = max( uint3( ClusterCounts.xyz ), 1 );
        return Flatten( clamp( uint3( uv * d.xy, DepthToSlice( depth ) ), 0, d - 1 ) );
    }

    static ClusterRange Query( ClusterItemType type, float4 positionSs )
    {
        return Query( type, Index( positionSs ) );
    }

    // The cluster at a flattened index, for callers that already know which
    static ClusterRange Query( ClusterItemType type, uint flatIndex )
    {
        // Build range
        // A light count carries its cluster's features above the count; the others' high bits are zero
        uint packed = ClusterItemCounts[CountSlot( type, flatIndex )];

        ClusterRange range;
        range.Type = type;
        range.Count = min( packed & ClusterCountMask, Capacity( type ) );
        range.BaseOffset = IndexBase( type, flatIndex );
        range.Features = packed >> ClusterFeatureShift;
        range.Taken = 0;
        range.Pending = LoadPending( range );
        range.Item = 0xFFFFFFFF;

        return range;
    }

    // The features any lane in the wave needs. Uniform, so branching on it picks one light loop for the whole wave
    // where per-lane features would run both loops in a wave that straddles clusters.
    static uint WaveFeatures( ClusterRange range )
    {
        return WaveActiveBitOr( range.Features );
    }

    static uint LoadItem( ClusterRange range, uint index )
    {
        return ClusterItemIndices[range.BaseOffset + index];
    }

    // This lane's next item, ~0 once it has taken them all. Branch, not ?:, so a full cluster never loads past its end
    static uint LoadPending( ClusterRange range )
    {
        if ( range.Taken >= range.Count )
            return 0xFFFFFFFF;
        return LoadItem( range, range.Taken );
    }

    // Wave steps to the lowest item any lane still has pending, false once none do
    static bool Next( inout ClusterRange range )
    {
        range.Item = WaveActiveMin( range.Pending );
        return range.Item != 0xFFFFFFFF;
    }

    // Whether this lane holds the current item, prefetching its next. Not ==: amdllpc would make Item per-lane
    static bool Take( inout ClusterRange range )
    {
        if ( range.Pending > range.Item )
            return false;

        range.Taken++;
        range.Pending = LoadPending( range );
        return true;
    }
};

// Body runs once per item this lane holds, walked wave-uniform so item data loads once per wave
#define ClusterForEach( range ) while ( Cluster::Next( range ) ) if ( Cluster::Take( range ) )


#endif
