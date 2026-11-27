// pp_uber: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// The standard post effects that read only their own pixel, or a fixed few around it, in one pass: one read of the frame
// and one write, whichever of them are on (docs/reference/borealis-uber-post.md). PostProcessLayers folds a run of these
// effects' layers into one draw; each effect is a bit of UberEffects, a feature bit in the pass's constants that every
// pixel of a draw branches on the same way, so an effect that's off costs a skipped branch, with no combos; the effects'
// parameters are the same one constant buffer.
//
// In a run's order, as the effects' own layers drew them, each over the last one's result:
//   the source: sharpen (3x3), chromatic aberration (three taps) or pixelate (a block's mip) when one starts the run, else
//   this pixel; then film grain, colour adjustments and colour grading (temperature or LUT, then per-channel curves), each
//   over a colour clamped to 0..1, as the 8-bit target clamped it between passes.
// Before the tonemap (BeforePostProcess, HDR, nothing clamped): bloom's composite as the source, then the vignette.
// The tonemap (its own stage): the frame's pixel, exposed and mapped.
// Drawn by the output copy (UBER_OUTPUT, SceneBridge.CopyFrame): the tonemap and the first run after it in one draw, from the
// HDR frame into the output, point-sampled and mirrored as copytexture reads it; a neighbour reader's taps are each tonemapped
// and clamped, as the output it read held them. UBER_ENCODE writes as the copy into a linear-bound 8-bit target: encoded, opaque.
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
    #define UBER_BLOOM          64
    #define UBER_VIGNETTE       128
    #define UBER_TONEMAP        256
    #define UBER_OUTPUT         512
    #define UBER_ENCODE         1024

    // Not specialization constants: Borealis.Gpu keys a standalone module's pipelines by its dynamic combos only
    int UberEffects < Attribute( "UberEffects" ); Default( 0 ); >;

    // Colour grading's method (0 none, 1 temperature, 2 LUT) and per-channel colour space (0 none, 1 RGB, 2 HSV): method + 4 * space
    int UberGrading < Attribute( "UberGrading" ); Default( 0 ); >;

    bool Has( int effect ) { return ( UberEffects & effect ) != 0; }

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

    float4 Fetch( float2 vUv )
    {
        return g_tColorBuffer.SampleLevel( g_sBilinearMirror, vUv, 0 );
    }

    // A neighbour reader's tap. Drawn by the output copy, the frame is HDR: each tap is what the output held there, the tonemap's
    // result (when it's drawn here) clamped as the 8-bit output stored it
    float4 Tap( float2 vUv )
    {
        float4 c = g_tColorBuffer.Sample( g_sBilinearMirror, vUv );
        if ( Has( UBER_OUTPUT ) )
        {
            if ( Has( UBER_TONEMAP ) ) c = Tonemap( c );
            c = saturate( c );
        }
        return c;
    }

    //
    // Sharpen: a 3x3 tent blur's difference, masked to edges
    //
    float SharpenStrength < Attribute( "SharpenStrength" ); Default( 0.0f ); >;
    float SharpenSize < Attribute( "SharpenSize" ); Default( 1.0f ); >;

    float4 Sharpen( float2 uv, float k )
    {
        float2 texel = SharpenSize / g_vRenderTargetSize;

        float4 c   = Tap( uv );
        float4 n   = Tap( uv + float2( 0, -texel.y ) );
        float4 s   = Tap( uv + float2( 0,  texel.y ) );
        float4 e   = Tap( uv + float2(  texel.x, 0 ) );
        float4 w   = Tap( uv + float2( -texel.x, 0 ) );
        float4 ne  = Tap( uv + texel );
        float4 nw  = Tap( uv + float2( -texel.x,  texel.y ) );
        float4 se  = Tap( uv + float2(  texel.x, -texel.y ) );
        float4 sw  = Tap( uv - texel );

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

        float4 r = Tap( vTexCoords - ( offsetScale * CaAmount.r ) );
        float4 g = Tap( vTexCoords - ( offsetScale * CaAmount.g ) );
        float4 b = Tap( vTexCoords - ( offsetScale * CaAmount.b ) );

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

    //
    // Bloom's composite: the bloom chain's result (BloomTexture, made by bloom's own command list before this pass) over
    // the frame, in HDR (BeforePostProcess)
    //
    Texture2D BloomTexture < Attribute( "BloomTexture" ); >;
    int CompositeMode < Attribute( "CompositeMode" ); Default( 0 ); >;
    float BloomScale < Attribute( "BloomScale" ); Default( 0.0f ); >;
    float3 Tint < Attribute( "Tint" ); Default3( 1.0f, 1.0f, 1.0f ); >;

    float3 ScreenHDR( float3 base, float3 blend )
    {
        base = max( base, 0.0f );
        blend = max( blend, 0.0f );
        float3 screenTerm = 1.0f - ( 1.0f - saturate( base ) ) * ( 1.0f - saturate( blend ) );
        float3 excessBase = max( base - 1.0f, 0.0f );
        float3 excessBlend = max( blend - 1.0f, 0.0f );
        return screenTerm + excessBase + excessBlend;
    }

    float4 BloomComposite( float2 vScreenUv )
    {
        float4 baseColor = g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv.xy );
        float4 bloom = BloomTexture.Sample( g_sBilinearMirror, vScreenUv.xy );
        bloom.rgb *= Tint * BloomScale;

        float3 finalColor = 0;
        if ( CompositeMode == 0 )
        {
            finalColor = baseColor.rgb + bloom.rgb; // Additive
        }
        else if ( CompositeMode == 1 )
        {
            finalColor = ScreenHDR( baseColor.rgb, bloom.rgb ); // Screen
        }
        else // 2: Lerp by bloom luminance
        {
            float bloomLuminance = Luminance( bloom.rgb );
            finalColor = lerp( baseColor.rgb, bloom.rgb, saturate( bloomLuminance ) );
        }

        // Approximate bloom alpha with brightness so it bleeds fine on translucent backgrounds
        float alpha = max( baseColor.a, Luminance( finalColor.rgb ) );

        return float4( finalColor, alpha );
    }

    //
    // Vignette: the frame multiplied toward the vignette's colour at the edges, in HDR (BeforePostProcess). Alone it draws
    // with pp_vignette, blending over the frame; after bloom's composite it's part of that pass, blended here the same way
    //
    float4 VignetteColor < Attribute( "VignetteColor" ); Default4( 0.0f, 0.0f, 0.0f, 1.0f ); >;
    float VignetteIntensity < Attribute( "VignetteIntensity" ); Default( 1.0f ); >;
    float VignetteFeather < Attribute( "VignetteSmoothness" ); Default( 1.0f ); >;
    float VignetteRoundness < Attribute( "VignetteRoundness" ); Default( 1.0f ); >;
    float2 VignetteCenter < Attribute( "VignetteCenter" ); Default2( 0.5f, 0.5f ); >;

    float ComputeVignetteAmount( float2 uv )
    {
        float2 offset = abs( uv - VignetteCenter );

        // Apply roundness/aspect correction
        float2 aspect = float2( g_vRenderTargetSize.x / g_vRenderTargetSize.y, 1.0 );
        offset *= lerp( float2( 1.0, 1.0 ), aspect, VignetteRoundness );

        // Scale radius by intensity
        float dist = length( offset ) * ( 0.5 + VignetteIntensity );

        // Falloff using smooth feathering
        return smoothstep( 0.0, 1.0, pow( saturate( dist ), lerp( 1.0, 6.0, 1.0 - VignetteFeather ) ) );
    }

    float4 Vignette( float4 dst, float2 uv )
    {
        float4 src = lerp( 1.0f, VignetteColor, ComputeVignetteAmount( uv ) * VignetteColor.a );

        // pp_vignette's blending: colour src * dst (ZERO, SRC_COLOR), alpha src.a * src.a + dst.a (the default alpha factors)
        return float4( dst.rgb * src.rgb, src.a * src.a + dst.a );
    }

    //
    // Tonemap: the auto exposure's scale, then the curve (the Tonemapping stage, HDR in, display range out)
    //
    #define TONEMAPPING_HABLE_FILMIC 1
    #define TONEMAPPING_ACES 2
    #define TONEMAPPING_REINHARDJODIE 3
    #define TONEMAPPING_LINEAR 4
    #define TONEMAPPING_AGX 5

    float TonemapLuminance( float3 color )
    {
        return dot( color, float3( 0.2126f, 0.7152f, 0.0722f ) );
    }

    // Hable only: the curve on the colour (RGB) or on its luminance, put back with the colour's hue and saturation
    float3 RemapForExposureBias( float3 RGB, int exposureMethod )
    {
        if ( exposureMethod == 1 )
        {
            float3 HSV = GradingRgbToHsv( RGB );
            HSV.z = TonemapLuminance( RGB );
            RGB = GradingHsvToRgb( HSV );
        }
        return RGB;
    }

    float3 ToneMapping_ReinhardJodie( float3 color )
    {
        float L = TonemapLuminance( color );
        float3 tv = color / ( 1.0f + color );
        return lerp( color / ( 1.0f + L ), tv, tv );
    }

    float3 uncharted2_tonemap_partial( float3 x )
    {
        const float A = 0.15f;
        const float B = 0.50f;
        const float C = 0.10f;
        const float D = 0.20f;
        const float E = 0.02f;
        const float F = 0.30f;
        return ( ( x * ( A * x + C * B ) + D * E ) / ( x * ( A * x + B ) + D * F ) ) - E / F;
    }

    float3 ToneMapping_HableFilmic( float3 color, int exposureMethod )
    {
        color = RemapForExposureBias( color, exposureMethod );
        float3 curr = uncharted2_tonemap_partial( color );
        float3 W = 11.2f;
        float3 white_scale = 1.0f / uncharted2_tonemap_partial( W );
        return curr * white_scale;
    }

    float3 ToneMapping_Aces( float3 color )
    {
        const float3x3 m1 =
        {
            0.59719, 0.07600, 0.02840,
            0.35458, 0.90834, 0.13383,
            0.04823, 0.01566, 0.83777
        };

        const float3x3 m2 =
        {
             1.60475, -0.10208, -0.00327,
            -0.53108,  1.10813, -0.07276,
            -0.07367, -0.00605,  1.07602
        };

        // mul( rgb, m1 ) == mul( transpose( m1 ), rgb )
        float3 v = mul( color, m1 );
        float3 a = v * ( v + 0.0245786 ) - 0.000090537;
        float3 b = v * ( 0.983729 * v + 0.4329510 ) + 0.238081;
        return saturate( mul( ( a / b ), m2 ) );
    }

    float3 AgxTonescale( float3 v )
    {
        // Similar implementation to Blender
        float3 v2 = v * v;
        float3 v4 = v2 * v2;
        return -0.20687445 * v + 6.80888933 * v2 - 37.60519607 * v2 * v + 93.32681938 * v4 - 95.2780858 * v4 * v + 33.96372259 * v4 * v2;
    }

    float3 ToneMapping_Agx( float3 vColor )
    {
        float3x3 mAgx = float3x3(
            0.856627153315983,      0.137318972929847,      0.11189821299995,
            0.0951212405381588,     0.761241990602591,      0.0767994186031903,
            0.0482516061458583,     0.101439036467562,      0.811302368396859
        );

        // Prevent negative values
        vColor = max( vColor, 0.0.xxx );
        vColor = mul( vColor, mAgx );

        // Log2 transform
        float flExposureMin = -12.47393f;
        float flExposureMax = 4.026069f;

        vColor = max( vColor, 1e-10.xxx );
        vColor = clamp( log2( vColor ), flExposureMin, flExposureMax );
        vColor = ( vColor - flExposureMin ) / ( flExposureMax - flExposureMin );

        vColor = AgxTonescale( vColor );

        // AgX Punchy look
        float3 vPower = float3( 1.35.xxx );
        float flSat = 1.4;

        vColor = pow( vColor, vPower );

        float flLuma = TonemapLuminance( vColor );
        vColor = flLuma + flSat * ( vColor - flLuma );

        // AgX outset matrix combined with Rec2020 to linear sRGB
        const float3x3 mAgxInv = float3x3(
            1.19687900512017,       -0.0528968517574562,    -0.0529716355144438,
            -0.0980208811401368,     1.15190312990417,      -0.0980434501171241,
            -0.0990297440797205,    -0.0989611768448433,    1.15107367264116
        );

        vColor = mul( vColor, mAgxInv );
        vColor = pow( vColor, 2.2.xxx );
        return max( vColor, 0.0.xxx );
    }

    // UberTonemap: the mode (Tonemapping.TonemappingMode, 0 none) + 8 * the exposure method (0 RGB, 1 luminance)
    int UberTonemap < Attribute( "UberTonemap" ); Default( 0 ); >;

    float4 Tonemap( float4 color )
    {
        int mode = UberTonemap & 7;
        float3 rgb = max( 0.0.xxx, color.rgb ) * g_flToneMapScalarLinear;

        if ( mode == TONEMAPPING_REINHARDJODIE ) rgb = ToneMapping_ReinhardJodie( rgb );
        else if ( mode == TONEMAPPING_HABLE_FILMIC ) rgb = ToneMapping_HableFilmic( rgb, UberTonemap >> 3 );
        else if ( mode == TONEMAPPING_ACES ) rgb = ToneMapping_Aces( rgb );
        else if ( mode == TONEMAPPING_AGX ) rgb = ToneMapping_Agx( rgb );

        return float4( rgb, color.a );
    }

    // Drawn by the output copy: copytexture's mirroring
    bool FlipX < Attribute( "FlipX" ); >;
    bool FlipY < Attribute( "FlipY" ); >;

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 uv = CalculateViewportUv( i.vPositionSs.xy );

        // The source: an effect that reads other pixels starts its run, so it reads the frame itself
        float4 color;
        bool tapsTonemapped = false;
        if ( Has( UBER_SHARPEN ) ) { color = Sharpen( uv, SharpenStrength * 25 ); tapsTonemapped = Has( UBER_OUTPUT ); }
        else if ( Has( UBER_CHROMATIC ) ) { color = ChromaticAberration( uv ); tapsTonemapped = Has( UBER_OUTPUT ); }
        else if ( Has( UBER_PIXELATE ) ) color = Pixelate( uv );
        else if ( Has( UBER_BLOOM ) ) color = BloomComposite( ( i.vPositionSs.xy + g_vViewportOffset ) / g_vViewportSize );
        else if ( Has( UBER_OUTPUT ) )
        {
            // As copytexture reads the frame
            float2 src = i.vTexCoord;
            if ( FlipX ) src.x = 1 - src.x;
            if ( FlipY ) src.y = 1 - src.y;
            color = g_tColorBuffer.Sample( g_sPointWrap, src );
        }
        else color = Fetch( uv );

        if ( Has( UBER_TONEMAP ) && !tapsTonemapped ) color = Tonemap( color );

        // Then each per-pixel effect over the last one's result, clamped as an 8-bit target between passes clamped it
        if ( Has( UBER_FILMGRAIN ) ) color = saturate( float4( ApplyFilmGrain( saturate( color.rgb ), uv ), color.a ) );
        if ( Has( UBER_COLOR ) ) color = saturate( ColorAdjust( saturate( color ) ) );
        if ( Has( UBER_GRADING ) ) color = ColorGrade( saturate( color ) );

        // HDR, before the tonemap
        if ( Has( UBER_VIGNETTE ) ) color = Vignette( color, i.vPositionSs.xy / g_vRenderTargetSize );

        // As the copy writes a linear-bound 8-bit target: sRGB-encoded, opaque
        if ( Has( UBER_ENCODE ) ) color = float4( SrgbLinearToGamma( color.rgb ), 1.0 );

        return color;
    }

#endif // PS
