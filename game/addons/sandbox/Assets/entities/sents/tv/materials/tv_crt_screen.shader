FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
    #include "common/shared.hlsl"
}

struct VertexInput
{
    #include "common/vertexinput.hlsl"
};

struct PixelInput
{
    #include "common/pixelinput.hlsl"
};

VS
{
    #include "common/vertex.hlsl"

    PixelInput MainVs( VertexInput i )
    {
        PixelInput o = ProcessVertex( i );
        return FinalizeVertex( o );
    }
}

PS
{
    #include "common/pixel.hlsl"

    // The render target texture fed from CameraWeapon
    Texture2D g_tScreenFeed < Attribute( "Color" ); >;

    // Signal state from TVEntity
    float g_flHasSignal < Attribute( "HasSignal" ); Default( 0.0 ); >;
    float g_flScreenOn < Attribute( "ScreenOn" ); Default( 1.0 ); >;
    float g_flTimeSinceSignalChange < Attribute( "TimeSinceSignalChange" ); Default( 99.0 ); >;
    float g_flDistanceFade < Attribute( "DistanceFade" ); Default( 1.0 ); >;

    // CRT effect intensity
    float g_flScanlineIntensity < Attribute( "ScanlineIntensity" ); Default( 0.3 ); >;
    float g_flCurvature < Attribute( "Curvature" ); Default( 0.02 ); >;
    float g_flChromaShift < Attribute( "ChromaShift" ); Default( 0.003 ); >;
    float g_flBrightness < Attribute( "Brightness" ); Default( 1.0 ); >;
    float g_flVignetteStrength < Attribute( "VignetteStrength" ); Default( 0.4 ); >;
    float g_flFlickerSpeed < Attribute( "FlickerSpeed" ); Default( 2.0 ); >;
    float g_flPixelGridScale < Attribute( "PixelGridScale" ); Default( 512.0 ); >;

    // Barrel distortion for CRT curvature
    float2 CurveUV( float2 uv, float curvature )
    {
        uv = uv * 2.0 - 1.0;
        float2 offset = abs( uv.yx ) / float2( 6.0, 4.0 );
        uv = uv + uv * offset * offset * curvature;
        uv = uv * 0.5 + 0.5;
        return uv;
    }

    // Simple hash noise
    float Hash( float2 p )
    {
        float3 p3 = frac( float3( p.xyx ) * 0.1031 );
        p3 += dot( p3, p3.yzx + 33.33 );
        return frac( ( p3.x + p3.y ) * p3.z );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        Material m = Material::From( i );
        float4 baseColor = ShadingModelStandard::Shade( i, m );

        float2 uv = i.vTextureCoords.xy;

        // Screen off — completely dark
        if ( g_flScreenOn < 0.5 )
        {
            return float4( 0, 0, 0, 1 );
        }

        // Apply barrel distortion
        float2 curvedUV = CurveUV( uv, g_flCurvature * 50.0 );

        // Black outside the curved screen area
        if ( curvedUV.x < 0.0 || curvedUV.x > 1.0 || curvedUV.y < 0.0 || curvedUV.y > 1.0 )
        {
            return float4( 0, 0, 0, 1 );
        }

        // Pixelate UVs for low-res CRT look
        float2 grid = float2( g_flPixelGridScale, g_flPixelGridScale * 0.75 );
        float2 pixelUV = round( curvedUV * grid ) / grid;

        // Signal state from C#
        float hasSignal = g_flHasSignal;
        float tChange = g_flTimeSinceSignalChange;

        // Single transition with two phases:
        // Losing signal: squeeze feed to black (0-0.2s), then fade in static (0.2-0.4s)
        // Gaining signal: fade out static to black (0-0.2s), then expand feed (0.2-0.4s)
        float phaseDuration = 0.2;
        float totalDuration = phaseDuration * 2.0;
        float signalMix;
        float squeezeMask = 1.0;

        if ( tChange < totalDuration )
        {
            if ( hasSignal > 0.5 )
            {
                // Gaining signal: fade out static, then expand feed
                if ( tChange < phaseDuration )
                {
                    // Phase 1: fade static to black
                    float t = saturate( tChange / phaseDuration );
                    signalMix = 0.0;
                    squeezeMask = 1.0 - t;
                }
                else
                {
                    // Phase 2: expand feed from black
                    float t = saturate( ( tChange - phaseDuration ) / phaseDuration );
                    signalMix = 1.0;
                    float distFromCenter = abs( uv.y - 0.5 ) * 2.0;
                    float openAmount = lerp( 0.02, 1.0, t );
                    squeezeMask = 1.0 - smoothstep( 0.0, openAmount, distFromCenter );
                }
            }
            else
            {
                // Losing signal: squeeze feed to black, then fade in static
                if ( tChange < phaseDuration )
                {
                    // Phase 1: squeeze feed to black
                    float t = saturate( tChange / phaseDuration );
                    signalMix = 1.0;
                    float distFromCenter = abs( uv.y - 0.5 ) * 2.0;
                    float openAmount = lerp( 0.02, 1.0, 1.0 - t );
                    squeezeMask = 1.0 - smoothstep( 0.0, openAmount, distFromCenter );
                }
                else
                {
                    // Phase 2: fade in static from black
                    float t = saturate( ( tChange - phaseDuration ) / phaseDuration );
                    signalMix = 0.0;
                    squeezeMask = t;
                }
            }
        }
        else
        {
            signalMix = hasSignal > 0.5 ? 1.0 : 0.0;
        }

        // --- No-signal static ---
        // Layered noise for classic analog TV snow
        float n1 = Hash( pixelUV * 800.0 + g_flTime * 30.0 );
        float n2 = Hash( pixelUV * 400.0 - g_flTime * 17.0 + 99.0 );
        float n3 = Hash( float2( pixelUV.y * 200.0, g_flTime * 10.0 ) );

        // Combine for grainy, slightly streaky look
        float staticNoise = n1 * 0.6 + n2 * 0.3 + n3 * 0.1;

        // Slight warm tint like old phosphor monitors
        float3 staticColor = staticNoise * float3( 0.7, 0.7, 0.65 ) * 0.15;

        // Faint horizontal interference bands
        float bands = sin( curvedUV.y * 120.0 + g_flTime * 3.0 ) * 0.5 + 0.5;
        bands = smoothstep( 0.3, 0.7, bands );
        staticColor += bands * 0.02;

        // --- Normal signal path ---
        // Chromatic aberration — shift R and B channels
        float r = g_tScreenFeed.SampleLevel( g_sPointClamp, pixelUV + float2( g_flChromaShift, 0 ), 0 ).r;
        float g = g_tScreenFeed.SampleLevel( g_sPointClamp, pixelUV, 0 ).g;
        float b = g_tScreenFeed.SampleLevel( g_sPointClamp, pixelUV - float2( g_flChromaShift, 0 ), 0 ).b;
        float3 screenColor = float3( r, g, b );

        // Pixel grid mask (RGB sub-pixel pattern)
        float2 subPixel = fmod( curvedUV * grid, 1.0 );
        float gridMask = smoothstep( 0.0, 0.3, subPixel.x ) * smoothstep( 0.0, 0.3, subPixel.y );
        screenColor *= lerp( 0.7, 1.0, gridMask );

        // Scanlines
        float scanline = sin( curvedUV.y * grid.y * 3.14159 ) * 0.5 + 0.5;
        scanline = lerp( 1.0, scanline, g_flScanlineIntensity );
        screenColor *= scanline;

        // Rolling scan bar
        float scrollBar = sin( ( uv.y - g_flTime * g_flFlickerSpeed ) * 2.0 ) * 0.5 + 0.5;
        scrollBar = smoothstep( 0.1, 0.9, scrollBar );
        screenColor += lerp( 0.0, 0.04, scrollBar ) * float3( 1, 0.9, 1 );

        // Blend between static and signal, apply squeeze/fade mask
        float3 finalColor = lerp( staticColor, screenColor, signalMix ) * squeezeMask;

        // Vignette — darken edges
        float2 vignetteUV = uv * 2.0 - 1.0;
        float vignette = 1.0 - dot( vignetteUV, vignetteUV ) * g_flVignetteStrength;
        vignette = saturate( vignette );
        finalColor *= vignette;

        // Brightness and emissive output
        finalColor *= g_flBrightness;

        // Distance-based fade to black
        finalColor *= g_flDistanceFade;

        // Replace base shading with screen output
        return float4( finalColor, 1.0 );
    }
}
