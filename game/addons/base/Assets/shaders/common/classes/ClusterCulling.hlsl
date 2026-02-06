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

    static ClusterRange Query( ClusterItemType type, float3 positionWs )
    {
        // World -> cluster coordinate
        float4 clip = Position3WsToPs( positionWs );
        float2 uv = saturate( clip.xy / max( clip.w, 1e-4f ) * 0.5f + 0.5f );
        uv.y = 1.0f - uv.y;

        float depth = clamp( abs( Position3WsToVs( positionWs ).z ), ClusterZParams.z, ClusterZParams.w );
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
