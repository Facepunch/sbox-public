HEADER
{
    DevShader = true;
    Description = "Bloom downsample/upsample compute";
}

MODES
{
    Default();
}

COMMON
{
    #include "postprocess/shared.hlsl"

    // 0 = Prefilter (threshold + karis average), 1 = Downsample, 2 = Upsample (accumulate)
    DynamicCombo( D_STAGE, 0..2, Sys( ALL ) );
}

CS
{
    SamplerState BilinearClamp < Filter(BILINEAR); AddressU(CLAMP); AddressV(CLAMP); >;

    Texture2D InputTexture < Attribute("InputTexture"); >;
    Texture2D QuarterResEffectsBloomInputTexture < Attribute( "QuarterResEffectsBloomInputTexture" ); >;

    RWTexture2D<float4> BloomOut < Attribute("BloomOut"); >;

    float Threshold < Attribute("Threshold"); Default(1.0f); >;

    float3 Fetch( float2 uv, float2 texel, float2 offset )
    {
        return InputTexture.SampleLevel( BilinearClamp, uv + offset * texel, 0 ).rgb;
    }

    float Luma( float3 c )
    {
        return dot( c, float3( 0.2126, 0.7152, 0.0722 ) );
    }

    // Soft-knee threshold in exposure-relative space
    float3 SoftThreshold( float3 color )
    {
        float threshold = max( Threshold, 0.0 );
        float exposure = g_flToneMapScalarLinear > 0.0 ? g_flToneMapScalarLinear : 1.0;
        float luma = Luma( color ) * exposure;
        float knee = max( threshold * 0.5, 1e-4 );
        float soft = clamp( luma - threshold + knee, 0.0, 2.0 * knee );
        soft = soft * soft / ( 4.0 * knee );
        return color * ( max( soft, luma - threshold ) / max( luma, 1e-4 ) );
    }

    // 13-tap downsample (Jimenez, SIGGRAPH 2014) - offsets in input texels
    //  a . b . c
    //  . j . k .
    //  d . e . f
    //  . l . m .
    //  g . h . i
    void Fetch13( float2 uv, float2 texel,
        out float3 a, out float3 b, out float3 c, out float3 d, out float3 e, out float3 f,
        out float3 g, out float3 h, out float3 i, out float3 j, out float3 k, out float3 l, out float3 m )
    {
        a = Fetch( uv, texel, float2( -2, -2 ) );
        b = Fetch( uv, texel, float2(  0, -2 ) );
        c = Fetch( uv, texel, float2(  2, -2 ) );
        d = Fetch( uv, texel, float2( -2,  0 ) );
        e = Fetch( uv, texel, float2(  0,  0 ) );
        f = Fetch( uv, texel, float2(  2,  0 ) );
        g = Fetch( uv, texel, float2( -2,  2 ) );
        h = Fetch( uv, texel, float2(  0,  2 ) );
        i = Fetch( uv, texel, float2(  2,  2 ) );
        j = Fetch( uv, texel, float2( -1, -1 ) );
        k = Fetch( uv, texel, float2(  1, -1 ) );
        l = Fetch( uv, texel, float2( -1,  1 ) );
        m = Fetch( uv, texel, float2(  1,  1 ) );
    }

    float3 Downsample13( float2 uv, float2 texel )
    {
        float3 a, b, c, d, e, f, g, h, i, j, k, l, m;
        Fetch13( uv, texel, a, b, c, d, e, f, g, h, i, j, k, l, m );

        float3 color = ( j + k + l + m ) * 0.125;
        color += ( a + c + g + i ) * 0.03125;
        color += ( b + d + f + h ) * 0.0625;
        color += e * 0.125;
        return color;
    }

    // Same 13 taps, but each overlapping quad is weighted by 1/(1+luma)
    // to suppress fireflies on the first (full res -> half res) downsample
    float3 Prefilter13( float2 uv, float2 texel )
    {
        float3 a, b, c, d, e, f, g, h, i, j, k, l, m;
        Fetch13( uv, texel, a, b, c, d, e, f, g, h, i, j, k, l, m );

        float3 q0 = max( ( j + k + l + m ) * 0.25, 0 );
        float3 q1 = max( ( a + b + d + e ) * 0.25, 0 );
        float3 q2 = max( ( b + c + e + f ) * 0.25, 0 );
        float3 q3 = max( ( d + e + g + h ) * 0.25, 0 );
        float3 q4 = max( ( e + f + h + i ) * 0.25, 0 );

        float w0 = 0.5   / ( 1.0 + Luma( q0 ) );
        float w1 = 0.125 / ( 1.0 + Luma( q1 ) );
        float w2 = 0.125 / ( 1.0 + Luma( q2 ) );
        float w3 = 0.125 / ( 1.0 + Luma( q3 ) );
        float w4 = 0.125 / ( 1.0 + Luma( q4 ) );

        return ( q0 * w0 + q1 * w1 + q2 * w2 + q3 * w3 + q4 * w4 ) / ( w0 + w1 + w2 + w3 + w4 );
    }

    // 3x3 tent filter - offsets in input (lower mip) texels
    float3 UpsampleTent( float2 uv, float2 texel )
    {
        float3 sum;
        sum  = Fetch( uv, texel, float2( -1, -1 ) );
        sum += Fetch( uv, texel, float2(  0, -1 ) ) * 2.0;
        sum += Fetch( uv, texel, float2(  1, -1 ) );
        sum += Fetch( uv, texel, float2( -1,  0 ) ) * 2.0;
        sum += Fetch( uv, texel, float2(  0,  0 ) ) * 4.0;
        sum += Fetch( uv, texel, float2(  1,  0 ) ) * 2.0;
        sum += Fetch( uv, texel, float2( -1,  1 ) );
        sum += Fetch( uv, texel, float2(  0,  1 ) ) * 2.0;
        sum += Fetch( uv, texel, float2(  1,  1 ) );
        return sum / 16.0;
    }

    [numthreads(8, 8, 1)]
    void MainCs( uint3 id : SV_DispatchThreadID )
    {
        uint2 dims;
        BloomOut.GetDimensions( dims.x, dims.y );
        if ( any( id.xy >= dims ) )
            return;

        float2 uv = ( id.xy + 0.5 ) / float2( dims );

        uint2 inDims;
        InputTexture.GetDimensions( inDims.x, inDims.y );
        float2 texel = 1.0 / float2( inDims );

        #if D_STAGE == 0
            // Threshold at (near) full resolution so small bright details survive
            float3 color = SoftThreshold( Prefilter13( uv, texel ) );

            // Bloom-only effects (already meant to glow, no threshold)
            color += QuarterResEffectsBloomInputTexture.SampleLevel( BilinearClamp, uv, 0 ).rgb;

            BloomOut[id.xy] = float4( max( color, 0 ), 1 );
        #elif D_STAGE == 1
            BloomOut[id.xy] = float4( Downsample13( uv, texel ), 1 );
        #else
            BloomOut[id.xy] = float4( BloomOut[id.xy].rgb + UpsampleTent( uv, texel ), 1 );
        #endif
    }
}
