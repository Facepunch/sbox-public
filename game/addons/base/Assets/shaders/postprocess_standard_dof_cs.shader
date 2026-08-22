MODES
{
    Default();
}

CS
{
    #include "postprocess/shared.hlsl"
    #include "common/classes/Depth.hlsl"

    //--------------------------------------------------------------------------------------
    // Depth of field - hexagonal bokeh blur at half resolution.
    //
    // Pass 0 (CircleOfConfusion): Downsamples color and computes the circle of confusion
    //      for the back and front layers, plus the max CoC of each 16x16 tile.
    // Pass 1 (Classify): Dilates the tile CoCs by their blur reach and builds compacted
    //      lists of tiles that need blurring + indirect dispatch arguments, so the blur
    //      passes only run where blur can land, with a budget from the tile's max CoC.
    // Pass 2 (DiagonalBlur): First hexagonal blur stage (vertical + diagonal). Indirect.
    // Pass 3 (HexagonalBlur): Second hexagonal blur stage (two diagonals). Indirect.
    //--------------------------------------------------------------------------------------

    DynamicCombo(D_PASS, 0..3, Sys(All));
    DynamicCombo(D_MSAA, 0..1, Sys(All));

    //--------------------------------------------------------------------------------------

    SamplerState BilinearClamp      < Filter(BILINEAR); AddressU(CLAMP); AddressV(CLAMP); AddressW(CLAMP); >;

    #if D_MSAA
    Texture2DMS<float4> Color       < Attribute("Color"); >;
    Texture2DMS<float>  DepthTexture  < Attribute("Depth"); >;
    #else
    Texture2D Color                 < Attribute("Color"); >;
    Texture2D DepthTexture          < Attribute("Depth"); >;
    #endif

    // Half resolution color with CoC in alpha, one per layer
    // UAVs are aliased across passes to stay within image binding limits:
    //   CoC:       OutColor0 = back color+CoC, OutColor1 = front color+CoC, OutTile = TileMax
    //   Classify:  OutTile = TileDilated
    //   Diagonal:  OutColor0 = Vertical, OutColor1 = Diagonal
    //   Hexagonal: OutColor0 = Final
    RWTexture2D<float4> OutColor0   < Attribute("OutColor0"); >;
    RWTexture2D<float4> OutColor1   < Attribute("OutColor1"); >;
    RWTexture2D<float2> OutTile     < Attribute("OutTile"); >;

    // Per-tile max CoC (x = back, y = front)
    Texture2D<float2>   TileMaxSRV      < Attribute("TileMaxSRV"); >;
    Texture2D<float2>   TileDilatedSRV  < Attribute("TileDilatedSRV"); >;

    struct IndirectDispatchArguments
    {
        uint x;
        uint y;
        uint z;
    };

    RWStructuredBuffer<uint> TileListRW                         < Attribute("TileListRW"); >;
    StructuredBuffer<uint>   TileList                           < Attribute("TileList"); >;
    RWStructuredBuffer<IndirectDispatchArguments> DispatchArgsRW < Attribute("DispatchArgsRW"); >;

    Texture2D VerticalSRV           < Attribute("VerticalSRV"); >;
    Texture2D DiagonalSRV           < Attribute("DiagonalSRV"); >;
    Texture2D FinalSRV              < Attribute("FinalSRV"); >;

    int Radius                      < Attribute("Radius"); >;
    float StepScale                 < Attribute("StepScale"); Default(1.0f); >;
    float FocusPlane                < Attribute("FocusPlane"); >;
    float FocusRange                < Attribute("FocusRange"); Default(10000.0f); >;
    float2 InvDimensions            < Attribute("InvDimensions"); >;
    float2 Dimensions               < Attribute("Dimensions"); >;

    bool EnableBack                 < Attribute("EnableBack"); Default(1); >;
    bool EnableFront                < Attribute("EnableFront"); Default(0); >;
    bool IsFront                    < Attribute("IsFront"); Default(0); >;
    int ListOffset                  < Attribute("ListOffset"); Default(0); >;
    int TileCapacity                < Attribute("TileCapacity"); >;

    #define PI 3.14159265359
    #define HALF_MAX 65504.0f
    #define TILE_SIZE 16

    //--------------------------------------------------------------------------------------

    // Max blur reach in half-res pixels
    float MaxBlurPixels() { return Radius * StepScale; }

    // Tiles whose blur is under half a pixel aren't worth dispatching
    float CocThreshold() { return 0.5f / max( MaxBlurPixels(), 1.0f ); }

    uint PackTile( uint2 tile ) { return (tile.x & 0xFFFFu) | (tile.y << 16u); }
    uint2 UnpackTile( uint packed ) { return uint2( packed & 0xFFFFu, packed >> 16u ); }

    //--------------------------------------------------------------------------------------
    // Pass 0 - Circle of confusion + downsample + per-tile max
    //--------------------------------------------------------------------------------------

    float GetDepthLinear( uint2 screenPos, int sampleIndex )
    {
        float depth;
        #if D_MSAA
            depth = DepthTexture.Load( screenPos, sampleIndex ).r;
        #else
            depth = DepthTexture.Load( int3( screenPos, 0 ) ).r;
        #endif


        // Viewmodels have a different depth range than the world, so we remap them to the same range for DoF.
        const float DEPTH_RANGE_GAME_OVERLAYS = 1.0 - 0.01f;
        if( depth > DEPTH_RANGE_GAME_OVERLAYS )
            depth = 1.0 - RemapValClamped( depth, DEPTH_RANGE_GAME_OVERLAYS, 1.0, 0.0, 1.0 );
        else
            depth = 1.0 - Depth::Normalize( depth );


        float a = g_flFarPlane / (g_flFarPlane - g_flNearPlane);
        float b = g_flFarPlane * g_flNearPlane / (g_flNearPlane - g_flFarPlane);
        return b / (depth - a);
    }

    float3 GetColor( uint2 screenPos, int sampleIndex )
    {
        #if D_MSAA
        return Color.Load( screenPos, sampleIndex ).rgb;
        #else
        return Color.Load( int3( screenPos, 0 ) ).rgb;
        #endif
    }

    //
    // Thin lens circle of confusion, normalized to 0..1 of the max blur radius.
    // Zeroed under the dispatch threshold so undispatched tiles stay fully transparent in the composite.
    //
    float BackCoc( float depth )  { float c = saturate( ( depth - FocusPlane ) / max( FocusRange, 1e-2f ) ); return c > CocThreshold() ? c : 0.0f; }
    float FrontCoc( float depth ) { float c = saturate( ( FocusPlane - depth ) / max( FocusRange, 1e-2f ) ); return c > CocThreshold() ? c : 0.0f; }

    groupshared uint g_nTileMaxBack;
    groupshared uint g_nTileMaxFront;

    void CircleOfConfusionPass( uint2 vDispatch, uint2 vGroup, uint nGroupIndex )
    {
        if ( nGroupIndex == 0 )
        {
            g_nTileMaxBack = 0;
            g_nTileMaxFront = 0;
        }

        GroupMemoryBarrierWithGroupSync();

        if ( all( vDispatch < uint2( Dimensions ) ) )
        {
            uint nSampleCount = 1;
            uint2 dim;
            #if D_MSAA
                DepthTexture.GetDimensions( dim.x, dim.y, nSampleCount );
            #else
                DepthTexture.GetDimensions( dim.x, dim.y );
            #endif

            const uint Downsample = 2;

            float3 colorAll = 0; float depthAll = 0; int samplesAll = 0;
            float3 colorBack = 0; float depthBack = 0; int samplesBack = 0;

            for ( uint x = 0; x < Downsample; x++ )
            for ( uint y = 0; y < Downsample; y++ )
            for ( uint j = 0; j < nSampleCount; j++ )
            {
                uint2 p = min( vDispatch * Downsample + uint2( x, y ), dim - 1 );

                float d = GetDepthLinear( p, j );
                float3 c = GetColor( p, j );

                colorAll += c; depthAll += d; samplesAll++;

                // The back layer only averages samples behind the focus plane so
                // foreground color doesn't bleed into the background blur
                if ( d >= FocusPlane )
                {
                    colorBack += c; depthBack += d; samplesBack++;
                }
            }

            float backCoc = 0;
            float frontCoc = 0;

            if ( EnableBack && samplesBack > 0 )
            {
                depthBack /= samplesBack;
                colorBack = min( colorBack / samplesBack, HALF_MAX );
                backCoc = BackCoc( depthBack );
                OutColor0[vDispatch] = float4( colorBack, backCoc );
            }
            else if ( EnableBack )
            {
                // No samples behind the focus plane - this texel is fully foreground.
                // Store its colour with zero CoC rather than black, so the bicubic upsample
                // in the composite (and the blur's centre sample) don't bleed a dark line
                // along the foreground edge.
                OutColor0[vDispatch] = float4( min( colorAll / samplesAll, HALF_MAX ), -1 );
            }

            if ( EnableFront )
            {
                depthAll /= samplesAll;
                colorAll = min( colorAll / samplesAll, HALF_MAX );
                frontCoc = FrontCoc( depthAll );
                OutColor1[vDispatch] = float4( colorAll, frontCoc );
            }

            // Positive floats compare correctly as uints
            InterlockedMax( g_nTileMaxBack, asuint( backCoc ) );
            InterlockedMax( g_nTileMaxFront, asuint( frontCoc ) );
        }

        GroupMemoryBarrierWithGroupSync();

        if ( nGroupIndex == 0 )
        {
            OutTile[vGroup] = float2( asfloat( g_nTileMaxBack ), asfloat( g_nTileMaxFront ) );
        }
    }

    //--------------------------------------------------------------------------------------
    // Pass 1 - Tile classification
    //
    // A tile needs blurring if any tile's CoC can reach it ( budget = dilated max CoC ).
    // We classify with twice that reach so every tile the blur passes can *sample from*
    // is also processed and never contains stale data.
    //--------------------------------------------------------------------------------------

    void ClassifyPass( uint2 vTile )
    {
        uint2 vTileGrid = uint2( ceil( Dimensions / TILE_SIZE ) );

        if ( any( vTile >= vTileGrid ) )
            return;

        float flMaxBlur = MaxBlurPixels();
        int nRange = min( 16, (int)ceil( ( 2.0f * flMaxBlur ) / TILE_SIZE ) + 1 );

        float2 vBudget = 0;     // What can reach this tile
        float2 vPadded = 0;     // What can reach what reaches this tile

        for ( int y = -nRange; y <= nRange; y++ )
        for ( int x = -nRange; x <= nRange; x++ )
        {
            int2 vNeighbor = int2( vTile ) + int2( x, y );
            if ( any( vNeighbor < 0 ) || any( vNeighbor >= int2( vTileGrid ) ) )
                continue;

            float2 vCoc = TileMaxSRV.Load( int3( vNeighbor, 0 ) ).rg;

            // Conservative tile center distance minus tile diagonal
            float flDist = max( 0.0f, length( float2( x, y ) ) - 1.5f ) * TILE_SIZE;
            float2 vReach = float2( vCoc.x * flMaxBlur, flMaxBlur );

            vBudget = max( vBudget, vCoc * ( step( flDist, vReach ) ) );
            vPadded = max( vPadded, vCoc * ( step( flDist, 2.0f * vReach ) ) );
        }

        OutTile[vTile] = vBudget;

        float flThreshold = CocThreshold();
        uint nPacked = PackTile( vTile );

        if ( vPadded.x > flThreshold )
        {
            uint nIndex;
            InterlockedAdd( DispatchArgsRW[0].x, 1u, nIndex );
            if ( nIndex < (uint)TileCapacity )
                TileListRW[nIndex] = nPacked;
        }

        if ( vPadded.y > flThreshold )
        {
            uint nIndex;
            InterlockedAdd( DispatchArgsRW[1].x, 1u, nIndex );
            if ( nIndex < (uint)TileCapacity )
                TileListRW[TileCapacity + nIndex] = nPacked;
        }
    }

    //--------------------------------------------------------------------------------------
    // Hexagonal bokeh blur
    //--------------------------------------------------------------------------------------

    float4 BlurTexture( Texture2D tex, float2 uv, float2 direction, int nSamples )
    {
        float4 center = tex.SampleLevel( BilinearClamp, uv, 0.0f );

        // The back layer can never gather more than the center's own CoC
        if ( !IsFront )
            nSamples = min( nSamples, (int)ceil( center.a * Radius ) );

        if( !IsFront)
            uv += direction * StepScale * 0.5f; // Offset first sample a bit to not self-intersect

        float4 finalColor = 0;
        float total = 0;

        for ( int i = 0; i < nSamples; ++i )
        {
            float2 sampleUV = uv + direction * i * StepScale;
            float4 sampleColor = tex.SampleLevel( BilinearClamp, sampleUV, 0.0f );

            // Dont do hard cutoff of front blur, back blur shouldnt go beyond CoC radius either
            if ( sampleColor.a * Radius < i && !IsFront )
                continue;

            finalColor += sampleColor;
            total += 1.0f;
        }

        if ( total <= 0.0f )
            return center;

        finalColor.xyz = min( finalColor.xyz, HALF_MAX );
        return finalColor / total;
    }

    // Maps an indirect group to its tile's pixel + per-tile sample budget
    bool GetBlurTarget( uint2 vGroup, uint2 vGroupThread, out uint2 vPixel, out int nSamples )
    {
        uint2 vTile = UnpackTile( TileList[ListOffset + vGroup.x] );
        vPixel = vTile * TILE_SIZE + vGroupThread;

        float2 vBudget = TileDilatedSRV.Load( int3( vTile, 0 ) ).rg;
        float flCoc = IsFront ? vBudget.y : vBudget.x;

        nSamples = min( Radius, (int)ceil( flCoc * Radius ) );

        return all( vPixel < uint2( Dimensions ) );
    }

    void DiagonalBlurPass( uint2 vGroup, uint2 vGroupThread )
    {
        uint2 vPixel; int nSamples;
        if ( !GetBlurTarget( vGroup, vGroupThread, vPixel, nSamples ) )
            return;

        float2 uv = ( float2( vPixel ) + 0.5f ) * InvDimensions;

        float2 blurDir = InvDimensions * float2( 0, 1 );
        float2 blurDir2 = InvDimensions * float2( cos( -PI / 6 ), sin( -PI / 6 ) );

        float4 vertical = BlurTexture( FinalSRV, uv, blurDir, nSamples );
        float4 diagonal = BlurTexture( FinalSRV, uv, blurDir2, nSamples );

        diagonal.xyz += vertical.xyz;

        OutColor0[vPixel] = vertical;
        OutColor1[vPixel] = diagonal;
    }

    void HexagonalBlurPass( uint2 vGroup, uint2 vGroupThread )
    {
        uint2 vPixel; int nSamples;
        if ( !GetBlurTarget( vGroup, vGroupThread, vPixel, nSamples ) )
            return;

        float2 uv = ( float2( vPixel ) + 0.5f ) * InvDimensions;

        float2 blurDir = InvDimensions * float2( cos( -PI / 6 ), sin( -PI / 6 ) );
        float4 color = BlurTexture( VerticalSRV, uv, blurDir, nSamples );

        float2 blurDir2 = InvDimensions * float2( cos( -5 * PI / 6 ), sin( -5 * PI / 6 ) );
        color += BlurTexture( DiagonalSRV, uv, blurDir2, nSamples );

        OutColor0[vPixel] = color / 3;
    }

    //--------------------------------------------------------------------------------------

    #if ( D_PASS == 1 )
    [numthreads(8, 8, 1)]
    #else
    [numthreads(TILE_SIZE, TILE_SIZE, 1)]
    #endif
    void MainCs( uint3 vDispatch : SV_DispatchThreadID, uint3 vGroupThread : SV_GroupThreadID, uint3 vGroup : SV_GroupID, uint nGroupIndex : SV_GroupIndex )
    {
        #if ( D_PASS == 0 )
            CircleOfConfusionPass( vDispatch.xy, vGroup.xy, nGroupIndex );
        #elif ( D_PASS == 1 )
            ClassifyPass( vDispatch.xy );
        #elif ( D_PASS == 2 )
            DiagonalBlurPass( vGroup.xy, vGroupThread.xy );
        #elif ( D_PASS == 3 )
            HexagonalBlurPass( vGroup.xy, vGroupThread.xy );
        #endif
    }
}
