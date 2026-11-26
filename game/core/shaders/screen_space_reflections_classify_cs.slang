// screen_space_reflections_classify_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// frame (common/classes/ForwardNormals.hlsl), as Borealis draws no depth-normals prepass.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON
#include "sbox/view.slangh"

// Whether the opaque pass's normals target (common/classes/ForwardNormals.hlsl) is multisampled, as the view
DynamicCombo( D_MSAA_NORMALS, 0..1, Sys( PC ) );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
    #include "sbox/math.slangh"
    #include "common/classes/ForwardNormals.hlsl"

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
    // The upscale's low-res guide and the full-res mip it fills (the trace's own mip at full res)
    RWTexture2D<float4>                         OutGuide 			< Attribute("OutGuide"); >;
    RWTexture2D<float4>                         OutFull 			< Attribute("OutFull"); >;

    uint PackTileCoord( uint2 tileCoord )
    {
        return (tileCoord.x & TILE_COORD_MASK) | ((tileCoord.y & TILE_COORD_MASK) << TILE_COORD_SHIFT);
    }

    groupshared uint groupHasReflection;

    // The intersect and upscale write only classified tiles; this writes the zeros everything else reads (the guide and
    // the trace's mip, and the full-res mip past the upscale's reach), so nothing is cleared first
    void Pass_Reflections_ClassifyTiles( int2 vDispatch, int2 vGroupThread, uint2 vGroupCoord )
    {
        if ( vGroupThread.x == 0 && vGroupThread.y == 0 )
        {
            groupHasReflection = 0;
        }

        GroupMemoryBarrierWithGroupSync();

        uint radianceWidth, radianceHeight;
        OutRadiance.GetDimensions( radianceWidth, radianceHeight );
        bool inside = all( uint2( vDispatch ) < uint2( radianceWidth, radianceHeight ) );
        bool reflects = false;
        if ( inside )
        {
            // Same representative full-res pixel as the intersect pass; the sky needs no roughness
            int2 vPixel = min( vDispatch * (int)ScaleInv + (int)ScaleInv / 2, int2( g_vViewportSize ) - 1 );
            reflects = Depth::GetNormalized( vPixel ) > 0.0001f && Roughness::Sample( vPixel ) < RoughnessCutoff;
        }
        if ( WaveActiveAnyTrue( reflects ) && WaveIsFirstLane() )
            groupHasReflection = 1u;

        GroupMemoryBarrierWithGroupSync();

        bool classified = groupHasReflection != 0u;
        if ( inside && !classified )
        {
            OutRadiance[vDispatch] = 0;
            OutGuide[vDispatch] = 0;
        }

        // The full-res texels this one stands for, unless the upscale writes them; the last row and column also take any
        // the low-res size dropped, which no tile reaches
        uint fullWidth, fullHeight;
        OutFull.GetDimensions( fullWidth, fullHeight );
        int scale = (int)ScaleInv;
        if ( inside && scale > 1 )
        {
            int2 lo = vDispatch * scale;
            int2 hi = lo + scale;
            int2 last = int2( fullWidth, fullHeight );
            hi = select( vDispatch == int2( uint2( radianceWidth, radianceHeight ) ) - 1, last, min( hi, last ) );
            for ( int y = lo.y; y < hi.y; y++ )
                for ( int x = lo.x; x < hi.x; x++ )
                    if ( !classified || x >= int( radianceWidth ) * scale || y >= int( radianceHeight ) * scale )
                        OutFull[int2( x, y )] = 0;
        }

        if ( vGroupThread.x != 0 || vGroupThread.y != 0 || !classified )
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
    [shader( "compute" )] void MainCs( uint2 dispatchThreadID : SV_DispatchThreadID,
                 uint2 groupThreadID    : SV_GroupThreadID,
                 uint2 groupID          : SV_GroupID )
    {
        Pass_Reflections_ClassifyTiles( dispatchThreadID, groupThreadID, groupID );
    }

#endif // CS

