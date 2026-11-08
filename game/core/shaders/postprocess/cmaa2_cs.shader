MODES
{
    Default();
}

CS
{
    #include "postprocess/shared.hlsl"

    DynamicCombo( D_PASS, 0..3, Sys( All ) );
    DynamicCombo( D_SRGB, 0..1, Sys( All ) );

    #define CMAA_MSAA_SAMPLE_COUNT 1
    #define CMAA2_STATIC_QUALITY_PRESET 3
    #define CMAA2_EDGE_DETECTION_LUMA_PATH 1
    #define CMAA2_EXTRA_SHARPNESS 0
    #define CMAA2_USE_HALF_FLOAT_PRECISION 0
    #define CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH 1
    #define CMAA2_EDGE_UNORM 0
    #define CMAA2_UAV_STORE_TYPED 1
    #define CMAA2_UAV_STORE_TYPED_UNORM_FLOAT D_SRGB
    #define CMAA2_UAV_STORE_CONVERT_TO_SRGB D_SRGB
    #define CMAA2_SUPPORT_HDR_COLOR_RANGE ( 1 - D_SRGB )

    Texture2D<float4> Cmaa2Color < Attribute( "Cmaa2Color" ); SrgbRead( true ); >;

    // Keep parser-supported float storage types; the bound format supplies UNORM normalization when needed, while D_SRGB selects the explicit transfer.
    RWTexture2D<float4> Cmaa2Output < Attribute( "Cmaa2Output" ); >;

    RWTexture2D<uint> Cmaa2Edges < Attribute( "Cmaa2Edges" ); >;
    RWTexture2D<uint> Cmaa2BlendHeads < Attribute( "Cmaa2BlendHeads" ); >;
    RWStructuredBuffer<uint> Cmaa2Candidates < Attribute( "Cmaa2Candidates" ); >;
    RWStructuredBuffer<uint> Cmaa2BlendLocations < Attribute( "Cmaa2BlendLocations" ); >;
    RWStructuredBuffer<uint2> Cmaa2BlendItems < Attribute( "Cmaa2BlendItems" ); >;
    RWStructuredBuffer<uint> Cmaa2Control < Attribute( "Cmaa2Control" ); >;

    struct Cmaa2IndirectDispatchArguments
    {
        uint x;
        uint y;
        uint z;
    };

    RWStructuredBuffer<Cmaa2IndirectDispatchArguments> Cmaa2DispatchArgs < Attribute( "Cmaa2DispatchArgs" ); >;
    uint2 Cmaa2Dimensions < Attribute( "Cmaa2Dimensions" ); >;
    uint Cmaa2CandidateCapacity < Attribute( "Cmaa2CandidateCapacity" ); >;
    uint Cmaa2BlendLocationCapacity < Attribute( "Cmaa2BlendLocationCapacity" ); >;
    uint Cmaa2BlendItemCapacity < Attribute( "Cmaa2BlendItemCapacity" ); >;

    #define g_inoutColorReadonly Cmaa2Color
    #define g_inoutColorWriteonly Cmaa2Output
    #define g_workingEdges Cmaa2Edges
    #define g_workingDeferredBlendItemListHeads Cmaa2BlendHeads
    #define g_workingShapeCandidates Cmaa2Candidates
    #define g_workingDeferredBlendLocationList Cmaa2BlendLocations
    #define g_workingDeferredBlendItemList Cmaa2BlendItems

    #include "common/thirdparty/cmaa2.hlsl"

    #if D_PASS == 0
        [numthreads( 16, 16, 1 )]
        void MainCs( uint3 groupID : SV_GroupID, uint3 groupThreadID : SV_GroupThreadID )
        {
            EdgesColor2x2CS( groupID, groupThreadID );
        }
    #elif D_PASS == 1
        [numthreads( 1, 1, 1 )]
        void MainCs( uint3 groupID : SV_GroupID )
        {
            ComputeDispatchArgsCS( groupID );
        }
    #elif D_PASS == 2
        [numthreads( 128, 1, 1 )]
        void MainCs( uint3 dispatchThreadID : SV_DispatchThreadID, uint3 groupThreadID : SV_GroupThreadID )
        {
            ProcessCandidatesCS( dispatchThreadID, groupThreadID );
        }
    #else
        [numthreads( 4, 32, 1 )]
        void MainCs( uint3 dispatchThreadID : SV_DispatchThreadID, uint3 groupThreadID : SV_GroupThreadID )
        {
            DeferredColorApply2x2CS( dispatchThreadID, groupThreadID );
        }
    #endif
}
