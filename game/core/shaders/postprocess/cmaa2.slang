// cmaa2: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md). The .shader
// beside it is only Source 2's renderer's.
// HEADER: Description = "CMAA2 color composite";

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );

    #include "postprocess/shared.hlsl"

struct VertexInput
{
    float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
    float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
    float2 uv : TEXCOORD0;
    float4 vPositionSs : SV_Position;
};

#if ( PROGRAM == VFX_PROGRAM_VS )
    [shader( "vertex" )] PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionSs = float4( i.vPositionOs.xyz, 1.0f );
        o.uv = i.vTexCoord;
        return o;
    }

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
    #include "postprocess/common.hlsl"

    SamplerState PointClamp < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
    Texture2D<float4> Cmaa2Color < Attribute( "Cmaa2Color" ); SrgbRead( true ); >;

    RenderState( ColorWriteEnable0, RGBA );
    RenderState( BlendEnable, false );
    RenderState( DepthEnable, false );
    RenderState( DepthWriteEnable, false );

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        return Cmaa2Color.SampleLevel( PointClamp, i.uv, 0.0f );
    }

#endif // PS

