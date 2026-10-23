//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
	#include "common.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
    #include "math_general.fxc"
    #include "common/classes/Normals.hlsl"

    static const uint TILE_COORD_MASK = 0xFFFFu;
    static const uint TILE_COORD_SHIFT = 16u;

    float RoughnessCutoff < Attribute( "RoughnessCutoff" ); Default( 0.5 ); >;

	// Scale factor between full-res screen and SSR buffers: 1 = full, 2 = half, 4 = quarter
	float ScaleInv 	< Attribute("ScaleInv"); Default(1); >;

    struct IndirectDispatchArguments
    {
        uint x;
        uint y;
        uint z;
    };

    RWTexture2D<uint> 							ClassifiedTilesRW 		< Attribute("ClassifiedTilesRW"); >;
    RWStructuredBuffer<IndirectDispatchArguments> DispatchArgsRW 	< Attribute("IntersectDispatchArgsRW"); >;
    RWTexture2D<float4>                         OutRadiance 		< Attribute("OutRadiance"); >;

    uint PackTileCoord( uint2 tileCoord )
    {
        return (tileCoord.x & TILE_COORD_MASK) | ((tileCoord.y & TILE_COORD_MASK) << TILE_COORD_SHIFT);
    }

    groupshared uint groupHasReflection;

    void Pass_Reflections_ClassifyTiles( int2 vDispatch, int2 vGroupId, uint2 vGroupCoord )
    {
        if ( vGroupId.x == 0 && vGroupId.y == 0 )
        {
            groupHasReflection = 0;
        }

        GroupMemoryBarrierWithGroupSync();

        uint radianceWidth, radianceHeight;
        OutRadiance.GetDimensions( radianceWidth, radianceHeight );
        if ( all( uint2( vDispatch ) < uint2( radianceWidth, radianceHeight ) ) )
        {
            // Clear the trace mip, the intersect only writes classified tiles
            OutRadiance[vDispatch] = 0;

            // Same representative full-res pixel as the intersect pass
            int2 vPixel = min( vDispatch * (int)ScaleInv + (int)ScaleInv / 2, int2( g_vViewportSize ) - 1 );
            float roughness = Roughness::Sample( vPixel );
            float depth = Depth::GetNormalized( vPixel );
            if ( roughness < RoughnessCutoff && depth > 0.0001f )
            {
                InterlockedOr( groupHasReflection, 1u );
            }
        }

        GroupMemoryBarrierWithGroupSync();

        if ( vGroupId.x != 0 || vGroupId.y != 0 || groupHasReflection == 0u )
            return;

        uint writeIndex;
        InterlockedAdd( DispatchArgsRW[0].x, 1u, writeIndex );

        // Bilateral upscale dispatches groupsPerTile groups per classified tile
        uint groupsPerAxis = max( 1u, (uint)round( ScaleInv ) );
        uint groupsPerTile = groupsPerAxis * groupsPerAxis;
        InterlockedAdd( DispatchArgsRW[1].x, groupsPerTile );

        uint width, height;
        ClassifiedTilesRW.GetDimensions( width, height );
        if ( writeIndex >= width * height )
            return;
        ClassifiedTilesRW[uint2( writeIndex % width, writeIndex / width )] = PackTileCoord( vGroupCoord );
    }

    [numthreads(8, 8, 1)]
    void MainCs( uint2 dispatchThreadID : SV_DispatchThreadID,
                 uint2 groupThreadID    : SV_GroupThreadID,
                 uint2 groupID          : SV_GroupID )
    {
        Pass_Reflections_ClassifyTiles( dispatchThreadID, groupThreadID, groupID );
    }
}
