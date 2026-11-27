// pp_uber: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The standard post effects that read only their own pixel, or a fixed few around it, in one pass: one read of the frame
// and one write, whichever of them are on (docs/reference/borealis-uber-post.md). PostProcessLayers folds a run of these
// effects' layers into one draw; each effect is a bit of UberEffects, a specialization constant, so a pipeline holds only
// the effects it draws, and their parameters are one constant buffer.
//
// In a run's order, as the effects' own layers drew them, each over the last one's result:
//   the source: sharpen (3x3), chromatic aberration (three taps) or pixelate (a block's mip) when one starts the run, else
//   this pixel; then film grain, colour adjustments and colour grading (temperature or LUT, then per-channel curves), each
//   over a colour clamped to 0..1, as the 8-bit target clamped it between passes.
// HEADER: Description = "Uber post";
// HEADER: DevShader = true;

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
    float2 vTexCoord : TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs		: SV_Position;
	#endif

	// PS only
	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs		: SV_Position;
	#endif
};

#if ( PROGRAM == VFX_PROGRAM_VS )
    [shader( "vertex" )] PixelInput MainVs( VertexInput i )
    {
        PixelInput o;

        o.vPositionPs = float4( i.vPositionOs.xy, 0.0f, 1.0f );
        o.vTexCoord = i.vTexCoord;
        return o;
    }

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
    #include "postprocess/common.hlsl"
    #include "procedural.hlsl"

    // The effects drawn (UberPostEffect in PostProcessLayers.Uber.cs)
    #define UBER_SHARPEN        1
    #define UBER_FILMGRAIN      2
    #define UBER_CHROMATIC      4
    #define UBER_COLOR          8
    #define UBER_GRADING        16
    #define UBER_PIXELATE       32

    int UberEffects < Attribute( "UberEffects" ); Default( 0 ); SpecializedConstant(); >;

    // Colour grading's method (0 none, 1 temperature, 2 LUT) and per-channel colour space (0 none, 1 RGB, 2 HSV): method + 4 * space
    int UberGrading < Attribute( "UberGrading" ); Default( 0 ); SpecializedConstant(); >;

    bool Has( int effect ) { return ( UberEffects & effect ) != 0; }

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

    float4 Fetch( float2 vUv )
    {
        return g_tColorBuffer.SampleLevel( g_sBilinearMirror, vUv, 0 );
    }

    //
    // Sharpen: a 3x3 tent blur's difference, masked to edges
    //
    float SharpenStrength < Attribute( "SharpenStrength" ); Default( 0.0f ); >;
    float SharpenSize < Attribute( "SharpenSize" ); Default( 1.0f ); >;

    float4 Sharpen( float2 uv, float k )
    {
        float2 texel = SharpenSize / g_vRenderTargetSize;

        float4 c   = g_tColorBuffer.Sample( g_sBilinearMirror, uv );
        float4 n   = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2( 0, -texel.y ) );
        float4 s   = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2( 0,  texel.y ) );
        float4 e   = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2(  texel.x, 0 ) );
        float4 w   = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2( -texel.x, 0 ) );
        float4 ne  = g_tColorBuffer.Sample( g_sBilinearMirror, uv + texel );
        float4 nw  = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2( -texel.x,  texel.y ) );
        float4 se  = g_tColorBuffer.Sample( g_sBilinearMirror, uv + float2(  texel.x, -texel.y ) );
        float4 sw  = g_tColorBuffer.Sample( g_sBilinearMirror, uv - texel );

        // 3x3 tent weights: center*4, axial*2, diagonal*1 -> normalize by 16
        float4 blur = ( c * 4 + ( n + s + e + w ) * 2 + ( ne + nw + se + sw ) ) * ( 1.0 / 16.0 );

        // simple luminance edge mask to cut halos at flat areas
        float lc = dot( c.rgb, float3( 0.299, 0.587, 0.114 ) );
        float lb = dot( blur.rgb, float3( 0.299, 0.587, 0.114 ) );
        float edge = saturate( abs( lc - lb ) * 4.0 );

        float kEff = k * edge;
        float4 outc = c + kEff * ( c - blur );

        return saturate( outc );
    }

    //
    // Chromatic aberration: each channel from its own offset toward the centre
    //
    float3 CaAmount < Attribute( "CaAmount" ); Default3( 0.004f, 0.006f, 0.0f ); >;
    float CaScale < Attribute( "CaScale" ); Default( 0.0f ); >;

    float4 ChromaticAberration( float2 vTexCoords )
    {
        float2 offsetScale = ( vTexCoords - 0.5 ) * CaScale * 10.0f;

        float4 r = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - ( offsetScale * CaAmount.r ) );
        float4 g = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - ( offsetScale * CaAmount.g ) );
        float4 b = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - ( offsetScale * CaAmount.b ) );

        return float4( r.r, g.g, b.b, ( r.a + g.a + b.a ) / 3.0f );
    }

    //
    // Pixelate: the block's centre, from the frame's mips
    //
    float PixelateScale < Attribute( "PixelateScale" ); Default( 1.0f ); >;

    float2 PixelateUV( float2 uv, float scale01 )
    {
        // Map scale to pixel count across X. Higher scale -> fewer pixels.
        const float gamma = 0.25;           // lower = more aggressive
        const float minPixAcross = 8.0;     // smallest across X
        float W = g_vRenderTargetSize.x;
        float H = g_vRenderTargetSize.y;
        float t = pow( saturate( scale01 ), gamma );
        float pixAcrossX = lerp( W, minPixAcross, t );

        // Square pixels: integer Y count from aspect
        float pixAcrossY = round( pixAcrossX * H / W );

        float2 count   = float2( pixAcrossX, pixAcrossY );
        float2 invCnt  = rcp( count );
        return ( floor( uv * count ) + 0.5 ) * invCnt; // center of the block
    }

    float4 Pixelate( float2 uv )
    {
        float enabled = step( 1e-5, PixelateScale );
        uv = lerp( uv, PixelateUV( uv, PixelateScale ), enabled );
        return g_tColorBuffer.SampleLevel( g_sTrilinearClamp, uv, PixelateScale );
    }

    //
    // Film grain: blue noise, weighted toward the darks
    //
    float GrainIntensity < Attribute( "GrainIntensity" ); Default( 0.0f ); >;
    float GrainResponse < Attribute( "GrainResponse" ); Default( 0.0f ); >;

    Texture2D g_tBlueNoise < Attribute( "BlueNoise" ); >;

    float3 FilmGrain( float3 vColor, float3 flSampledGrain, float intensity, float flResponse )
    {
        // Remap grain to a -1 -> 1 range
        flSampledGrain.rgb = flSampledGrain.b;

        // Grab our luminence and rescale based on response
        float lum = 1 - ( GetLuminance( vColor.rgb ) * 5.0f );
        intensity *= saturate( lerp( 1.0f, saturate( lum ), flResponse ) );

        float3 fullGrain = saturate( ( flSampledGrain - 0.1 ) * 2 ) - 0.7;

        vColor = lerp( vColor, GetLuminance( vColor.rgb ) * 0.1, intensity );

        return lerp( vColor, fullGrain, intensity );
    }

    float3 ApplyFilmGrain( float3 color, float2 vScreenUv )
    {
        float2 scale = float2( g_vRenderTargetSize.x, g_vRenderTargetSize.y ) / 128;

        float2 vGrainUvs = TileAndOffsetUv( vScreenUv, scale, float2( frac( g_flTime * 4.042f ), frac( g_flTime * 34.054f ) ) );
        float3 flGrain = g_tBlueNoise.SampleLevel( g_sBilinearWrap, vGrainUvs, 0 ).rgb;
        flGrain += g_tBlueNoise.SampleLevel( g_sBilinearWrap, -vGrainUvs * 5, 0 ).brg;
        flGrain *= 0.5;

        return FilmGrain( color, flGrain, GrainIntensity, GrainResponse );
    }

    //
    // Colour adjustments: contrast, then hue, brightness and saturation in HSV, blended
    //
    float ColorBlend < Attribute( "ColorBlend" ); Default( 1.0f ); >;
    float ColorHueRotate < Attribute( "ColorHueRotate" ); Default( 0.0f ); >;
    float ColorBrightness < Attribute( "ColorBrightness" ); Default( 1.0f ); >;
    float ColorContrast < Attribute( "ColorContrast" ); Default( 1.0f ); >;
    float ColorSaturation < Attribute( "ColorSaturation" ); Default( 1.0f ); >;

    float4 ColorAdjust( float4 color )
    {
        float4 oc = color;

        color.rgb = ( ( color.rgb - 0.5f ) * ColorContrast + 0.5f );
        float3 vHsv = RgbToHsv( color.rgb );

        vHsv.r = ( vHsv.r + ( ColorHueRotate / 360.0f ) ) % 1.0f;
        vHsv.b *= ColorBrightness;
        vHsv.g *= ColorSaturation;

        color.rgb = HsvToRgb( vHsv );

        return lerp( oc, color, ColorBlend );
    }

    //
    // Colour grading: temperature or a 16-slice LUT, then per-channel curves (curves of up to four frames), blended
    //
    #define GRADING_TEMPERATURE 1
    #define GRADING_LUT 2
    #define COLORSPACE_RGB 1
    #define COLORSPACE_HSV 2

    float ColorTempK < Attribute( "ColorTempK" ); Default( 6500.0f ); >;
    float BlendFactor < Attribute( "BlendFactor" ); Default( 0.0f ); >;
    Texture2D LookupTexture < Attribute( "LookupTexture" ); >;
    SamplerState g_sLutSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

    int      CurveFramesR < Attribute( "CurveFramesR" ); Default( 0 ); >;
    float4   CurveFrameR0 < Attribute( "CurveFrameR0" ); >;
    float4   CurveFrameR1 < Attribute( "CurveFrameR1" ); >;
    float4   CurveFrameR2 < Attribute( "CurveFrameR2" ); >;
    float4   CurveFrameR3 < Attribute( "CurveFrameR3" ); >;
    int      CurveFramesG < Attribute( "CurveFramesG" ); Default( 0 ); >;
    float4   CurveFrameG0 < Attribute( "CurveFrameG0" ); >;
    float4   CurveFrameG1 < Attribute( "CurveFrameG1" ); >;
    float4   CurveFrameG2 < Attribute( "CurveFrameG2" ); >;
    float4   CurveFrameG3 < Attribute( "CurveFrameG3" ); >;
    int      CurveFramesB < Attribute( "CurveFramesB" ); Default( 0 ); >;
    float4   CurveFrameB0 < Attribute( "CurveFrameB0" ); >;
    float4   CurveFrameB1 < Attribute( "CurveFrameB1" ); >;
    float4   CurveFrameB2 < Attribute( "CurveFrameB2" ); >;
    float4   CurveFrameB3 < Attribute( "CurveFrameB3" ); >;
    float4   CurveDivisorsR < Attribute( "CurveDivisorsR" ); >;
    float4   CurveDivisorsG < Attribute( "CurveDivisorsG" ); >;
    float4   CurveDivisorsB < Attribute( "CurveDivisorsB" ); >;

    int      CurveFramesH < Attribute( "CurveFramesH" ); Default( 0 ); >;
    float4   CurveFrameH0 < Attribute( "CurveFrameH0" ); >;
    float4   CurveFrameH1 < Attribute( "CurveFrameH1" ); >;
    float4   CurveFrameH2 < Attribute( "CurveFrameH2" ); >;
    float4   CurveFrameH3 < Attribute( "CurveFrameH3" ); >;
    int      CurveFramesS < Attribute( "CurveFramesS" ); Default( 0 ); >;
    float4   CurveFrameS0 < Attribute( "CurveFrameS0" ); >;
    float4   CurveFrameS1 < Attribute( "CurveFrameS1" ); >;
    float4   CurveFrameS2 < Attribute( "CurveFrameS2" ); >;
    float4   CurveFrameS3 < Attribute( "CurveFrameS3" ); >;
    int      CurveFramesV < Attribute( "CurveFramesV" ); Default( 0 ); >;
    float4   CurveFrameV0 < Attribute( "CurveFrameV0" ); >;
    float4   CurveFrameV1 < Attribute( "CurveFrameV1" ); >;
    float4   CurveFrameV2 < Attribute( "CurveFrameV2" ); >;
    float4   CurveFrameV3 < Attribute( "CurveFrameV3" ); >;
    float4   CurveDivisorsH < Attribute( "CurveDivisorsH" ); >;
    float4   CurveDivisorsS < Attribute( "CurveDivisorsS" ); >;
    float4   CurveDivisorsV < Attribute( "CurveDivisorsV" ); >;

    float3 TemperatureToRGB( float temperature )
    {
        const float MinT = 1000;
        const float MaxT = 40000;

        float3 retColor;
        float temperatureInKelvins = clamp( temperature, MinT, MaxT ) * 0.01f;

        if ( temperatureInKelvins <= 66.0 )
        {
            retColor.r = 1.0;
            retColor.g = saturate( 0.39008157876901960784 * log( temperatureInKelvins ) - 0.63184144378862745098 );
        }
        else
        {
            float t = temperatureInKelvins - 60.0;
            retColor.r = saturate( 1.29293618606274509804 * pow( t, -0.1332047592 ) );
            retColor.g = saturate( 1.12989086089529411765 * pow( t, -0.0755148492 ) );
        }

        if ( temperatureInKelvins >= 66.0 )
            retColor.b = 1.0;
        else if ( temperatureInKelvins <= 19.0 )
            retColor.b = 0.0;
        else
            retColor.b = saturate( 0.54320678911019607843 * log( temperatureInKelvins - 10.0 ) - 1.19625408914 );

        return retColor;
    }

    // RGB to HSV and back, all components 0..1, hue included (colour grading's own, not RgbToHsv's)
    float3 GradingRgbToHsv( float3 c )
    {
        float4 K = float4( 0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0 );
        float4 p = lerp( float4( c.bg, K.wz ), float4( c.gb, K.xy ), step( c.b, c.g ) );
        float4 q = lerp( float4( p.xyw, c.r ), float4( c.r, p.yzx ), step( p.x, c.r ) );
        float d = q.x - min( q.w, q.y );
        float e = 1.0e-10;
        return float3( abs( q.z + ( q.w - q.y ) / ( 6.0 * d + e ) ), d / max( q.x, e ), q.x );
    }

    float3 GradingHsvToRgb( float3 c )
    {
        float4 K = float4( 1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0 );
        float3 p = abs( frac( c.xxx + K.xyz ) * 6.0 - K.www );
        return c.z * lerp( K.xxx, saturate( p - K.xxx ), c.y );
    }

    float3 GradingLUT( float3 color )
    {
        float texWidth, texHeight;
        LookupTexture.GetDimensions( texWidth, texHeight );
        float halfColX = 1.0 / ( 2.0 * texWidth );
        float halfColY = 1.0 / ( 2.0 * texHeight );

        float xOffset = halfColX + color.r * 0.05859375;   // 15/256
        float yOffset = halfColY + color.g * 0.9375;       // 15/16

        float b = saturate( color.b ) * 15.0;
        float cell0 = floor( b );
        float cell1 = min( cell0 + 1.0, 15.0 );
        float f = b - cell0;

        float2 uv0 = float2( cell0 * 0.0625 + xOffset, yOffset );
        float2 uv1 = float2( cell1 * 0.0625 + xOffset, yOffset );

        float3 c0 = LookupTexture.SampleLevel( g_sLutSampler, uv0, 0 ).rgb;
        float3 c1 = LookupTexture.SampleLevel( g_sLutSampler, uv1, 0 ).rgb;

        return lerp( c0, c1, f );
    }

    // The curve's value at x, as Curve.cs evaluates it
    float GetYonCurve( float x, float CurveFrames, float4 CurveFrame0, float4 CurveFrame1, float4 CurveFrame2, float4 CurveFrame3, float4 CurveDivisors )
    {
        if ( CurveFrames == 0 ) return 0;

        float4 frames[4];
        frames[0] = CurveFrame0;
        frames[1] = CurveFrame1;
        frames[2] = CurveFrame2;
        frames[3] = CurveFrame3;

        float recips[3];
        recips[0] = CurveDivisors.x;
        recips[1] = CurveDivisors.y;
        recips[2] = CurveDivisors.z;

        int lastFrameIndex = CurveFrames - 1;
        if ( CurveFrame0.x > x ) return frames[0].y;
        if ( frames[lastFrameIndex].x <= x ) return frames[lastFrameIndex].y;

        float y = 0.f;
        for ( int i = 1; i <= lastFrameIndex; ++i )
        {
            if ( frames[i].x >= x )
            {
                float t = ( x - frames[i - 1].x ) * recips[i - 1];

                // x Time, y Value, z In, w Out
                float it = frames[i].z * -1.0f;
                float ot = frames[i - 1].w;

                float2 dxdy = frames[i].xy - frames[i - 1].xy;
                y = frames[i - 1].y + t * ( t * ( t * ( ( it + ot ) * dxdy.x - 2.0f * dxdy.y ) + ( -it - 2.0f * ot ) * dxdy.x + 3.0f * dxdy.y ) + ot * dxdy.x );
                break;
            }
        }
        return y;
    }

    float4 ColorGrade( float4 originalColor )
    {
        float3 color = originalColor.rgb;
        int method = UberGrading & 3;
        int space = UberGrading >> 2;

        if ( method == GRADING_TEMPERATURE ) color = color * TemperatureToRGB( ColorTempK );
        else if ( method == GRADING_LUT ) color = GradingLUT( color );

        if ( space == COLORSPACE_RGB )
        {
            color.r = GetYonCurve( color.r, CurveFramesR, CurveFrameR0, CurveFrameR1, CurveFrameR2, CurveFrameR3, CurveDivisorsR );
            color.g = GetYonCurve( color.g, CurveFramesG, CurveFrameG0, CurveFrameG1, CurveFrameG2, CurveFrameG3, CurveDivisorsG );
            color.b = GetYonCurve( color.b, CurveFramesB, CurveFrameB0, CurveFrameB1, CurveFrameB2, CurveFrameB3, CurveDivisorsB );
        }
        else if ( space == COLORSPACE_HSV )
        {
            float3 hsv = GradingRgbToHsv( color );
            hsv.x = GetYonCurve( hsv.x, CurveFramesH, CurveFrameH0, CurveFrameH1, CurveFrameH2, CurveFrameH3, CurveDivisorsH );
            hsv.y = GetYonCurve( hsv.y, CurveFramesS, CurveFrameS0, CurveFrameS1, CurveFrameS2, CurveFrameS3, CurveDivisorsS );
            hsv.z = GetYonCurve( hsv.z, CurveFramesV, CurveFrameV0, CurveFrameV1, CurveFrameV2, CurveFrameV3, CurveDivisorsV );
            color = GradingHsvToRgb( hsv );
        }

        return float4( lerp( originalColor.rgb, color, BlendFactor ), originalColor.a );
    }

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 uv = CalculateViewportUv( i.vPositionSs.xy );

        // The source: an effect that reads other pixels starts its run, so it reads the frame itself
        float4 color;
        if ( Has( UBER_SHARPEN ) ) color = Sharpen( uv, SharpenStrength * 25 );
        else if ( Has( UBER_CHROMATIC ) ) color = ChromaticAberration( uv );
        else if ( Has( UBER_PIXELATE ) ) color = Pixelate( uv );
        else color = Fetch( uv );

        // Then each per-pixel effect over the last one's result, clamped as an 8-bit target between passes clamped it
        if ( Has( UBER_FILMGRAIN ) ) color = saturate( float4( ApplyFilmGrain( saturate( color.rgb ), uv ), color.a ) );
        if ( Has( UBER_COLOR ) ) color = saturate( ColorAdjust( saturate( color ) ) );
        if ( Has( UBER_GRADING ) ) color = ColorGrade( saturate( color ) );

        return color;
    }

#endif // PS
