#ifndef COMMON_CLASSES_CLUSTER_CULLING_HLSL
#define COMMON_CLASSES_CLUSTER_CULLING_HLSL

#include "common.fxc"

#ifdef CLUSTERED_LIGHT_CULLING_CS
    #define ClusteredBuffer RWStructuredBuffer
#else
    #define ClusteredBuffer StructuredBuffer
#endif

ClusteredBuffer<uint> ClusterLightCounts   < Attribute( "ClusterLightCounts" ); >;
ClusteredBuffer<uint> ClusterEnvMapCounts  < Attribute( "ClusterEnvMapCounts" ); >;
ClusteredBuffer<uint> ClusterDecalCounts   < Attribute( "ClusterDecalCounts" ); >;
ClusteredBuffer<uint> ClusterLightIndices  < Attribute( "ClusterLightIndices" ); >;
ClusteredBuffer<uint> ClusterEnvMapIndices < Attribute( "ClusterEnvMapIndices" ); >;
ClusteredBuffer<uint> ClusterDecalIndices  < Attribute( "ClusterDecalIndices" ); >;

cbuffer ClusteredLightingConstants
{
    float4 ClusterCounts;        // xyz = tile counts, w = total
    float4 ClusterInvCounts;     // xyz = 1/counts, w = unused
    float4 ClusterZParams;       // x = log scale, y = log bias, z = near, w = far
    float4 ClusterScreenParams;  // xy = size, zw = 1/size
    float4 ClusterCapacitiesVec; // xyz = capacity per type
};

enum ClusterItemType
{
    ClusterItemType_Light,
    ClusterItemType_EnvMap,
    ClusterItemType_Decal,
    ClusterItemType_Count
};

struct ClusterRange
{
    uint Type;
    uint Count;
    uint BaseOffset;
};

class Cluster
{
    static float SliceToDepth( float slice ) { return exp( ( slice - ClusterZParams.y ) / ClusterZParams.x ); }
    static float DepthToSlice( float depth ) { return ClusterZParams.x * log( depth ) + ClusterZParams.y; }
    static uint Capacity( ClusterItemType type ) { return uint( ClusterCapacitiesVec[type] ); }
    static uint BaseOffset( ClusterItemType type, uint flatIndex ) { return flatIndex * Capacity( type ); }

    static uint Flatten( uint3 coord )
    {
        uint3 d = max( uint3( ClusterCounts.xyz ), 1 );
        return coord.x + d.x * ( coord.y + d.y * coord.z );
    }

    static ClusterRange Query( ClusterItemType type, float4 positionSs )
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
        uint3 coord = clamp( uint3( uv * d.xy, DepthToSlice( depth ) ), 0, d - 1 );
        uint flatIndex = coord.x + d.x * ( coord.y + d.y * coord.z );

        // Build range
        uint capacity = uint( ClusterCapacitiesVec[type] );
        uint count = 0;
        switch ( type )
        {
            case ClusterItemType_Light:  count = ClusterLightCounts[flatIndex];  break;
            case ClusterItemType_EnvMap: count = ClusterEnvMapCounts[flatIndex]; break;
            case ClusterItemType_Decal:  count = ClusterDecalCounts[flatIndex];  break;
            default: break;
        }

        ClusterRange range;
        range.Type = type;
        range.Count = min( count, capacity );
        range.BaseOffset = flatIndex * capacity;

        return range;
    }

    static uint LoadItem( ClusterRange range, uint index )
    {
        uint offset = range.BaseOffset + index;
        switch ( range.Type )
        {
            case ClusterItemType_Light:  return ClusterLightIndices[offset];
            case ClusterItemType_EnvMap: return ClusterEnvMapIndices[offset];
            case ClusterItemType_Decal:  return ClusterDecalIndices[offset];
            default: return 0;
        }
    }
};

#endif
