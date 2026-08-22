#include "common.fxc"
#include "math_general.fxc"
#include "ui/scissor.hlsl"
#include "ui/gamma.hlsl"
#include "common/blendmode.hlsl"

float2 BoxSize < Attribute( "BoxSize" ); >;
float2 BoxPosition < Attribute( "BoxPosition" ); >;

struct PS_OUTPUT
{
    float4 vColor : SV_Target0;
};

// How much of the pixel the clip stack lets through, set by UI_CommonProcessing_Pre
static float g_flUIClipCoverage = 1.0;

void UI_CommonProcessing_Pre( PS_INPUT i )
{
    if ( HasScissoring )
    {
        g_flUIClipCoverage = SoftwareScissorCoverage( i );
    }
}

// Kept until the very end so nothing that takes screen derivatives runs after a discard.
float4 UI_ApplyClip( float4 vColor, bool bPremultiplied = false )
{
    if ( g_flUIClipCoverage <= 0.0 )
        clip( -1 );

    if ( bPremultiplied )
        vColor *= g_flUIClipCoverage;
    else
        vColor.a *= g_flUIClipCoverage;

    return vColor;
}

// Straight-alpha output. Premultiplied paths (D_BLENDMODE 3) return through UI_ApplyClip instead.
// flCoverage is how much of the pixel the shape covers, see UISoftenHdrEdges.
PS_OUTPUT UI_CommonProcessing_Post( PS_INPUT i, PS_OUTPUT o, float flCoverage = 1.0 )
{
    o.vColor = UISoftenHdrEdges( o.vColor, flCoverage * g_flUIClipCoverage );
    o.vColor = UI_ApplyClip( o.vColor );
    o.vColor = UIEncodeOutput( o.vColor );
    return o;
}


#if ( D_NO_ZTEST )
    RenderState( DepthEnable, false );
#else
    RenderState( DepthEnable, true );
#endif
