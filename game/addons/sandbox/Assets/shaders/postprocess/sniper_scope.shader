HEADER
{
    Description = "";
    DevShader = true;
}

MODES
{
    Default();
    VrForward();
}

FEATURES
{
}

COMMON
{
    #include "postprocess/shared.hlsl"
}

struct VertexInput
{
    float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
    float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
    float2 vTexCoord : TEXCOORD0;

    #if ( PROGRAM == VFX_PROGRAM_VS )
        float4 vPositionPs : SV_Position;
    #endif

    #if ( ( PROGRAM == VFX_PROGRAM_PS ) )
        float4 vPositionSs : SV_Position;
    #endif

    float2 vData : TEXCOORD1;
};

VS
{
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionPs = float4( i.vPositionOs.xyz, 1.0f );
        return o;
    }
}

PS
{
    #include "postprocess/common.hlsl"

    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );

    float BlurAmount < Attribute( "BlurAmount" ); Default( 1.0f ); >;
    float ScopeReveal < Attribute( "ScopeReveal" ); Default( 1.0f ); >;
    float2 Offset < Attribute( "Offset" ); >;

    float sdCircle( float2 p, float r )
    {
        return length( p ) - r;
    }

    float sdBox( in float2 p, in float2 b )
    {
        float2 d = abs( p ) - b;
        return length( max( d, 0.0 ) ) + min( max( d.x, d.y ), 0.0 );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 pos = i.vPositionSs.xy;

        float2 center = g_vViewportSize * 0.5f;
        center += Offset * g_vViewportSize.xy;

        float radius = g_vViewportSize.y * 0.45f * ScopeReveal;

        float d = sdCircle( pos - center, radius );

        // Crosshair lines
        d = max( d, 1.0 - sdBox( pos - center, float2( -1.0f, g_vViewportSize.y ) ) );
        d = max( d, 1.0 - sdBox( pos - center, float2( g_vViewportSize.x, -1.0f ) ) );

        d *= BlurAmount;
        d += 1.0f;

        d = saturate( d * BlurAmount );
        return float4( 0, 0, 0, d );
    }
}
