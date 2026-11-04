//
//  Purpose: Colour grading methods` for post process  'ColorGrading.cs'
//  Version: 1.0
//  Author:  MDV
//
//  Notes:   LUT  is for 16 slices only.`
//

HEADER
{
    Description = "Color Grading";
    DevShader = true;
}  

MODES
{ 
    Default();
    Forward();
}
 
FEATURES
{ 
}
 
COMMON
{
    #include "postprocess/shared.hlsl"

    float flDOFFocusPlane< Attribute("standard.dof.focusplane"); Default(1.0f); >;
     
    Texture2D LookupTexture < Attribute( "LookupTexture" ); >;  
}

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

    // Extra data:
                 
    float3 vData : TEXCOORD1;     // 'vData.x'   unused
                                  // 'vData.y'   holds 0.5 / (LUT lookup texel width)   when using LUT
                                  // 'vData.z'   holds 0.5 / (LUT lookup texel height)   when using LUT
};
 
VS
{    
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionPs = float4(i.vPositionOs.xyz, 1.0f);
        o.vPositionPs.z = 1;
        o.vTexCoord = i.vTexCoord;
  
        #if ( D_CGRAD_PASS == GRADING_LUT )
            float texWidth;
            float texHeight;
            LookupTexture.GetDimensions( texWidth, texHeight ); 
            o.vData.yz = 1.0 / (2.0 * float2( texWidth, texHeight) );
        #endif

        return o;
    } 
} 

PS 
{
    #include "postprocess/common.hlsl"
        
    #define	GRADING_TEMPERATURE 1
    #define	GRADING_LUT 2

    DynamicCombo( D_CGRAD_PASS, 0..2, Sys( PC ) );

    #define COLORSPACE_NONE 0
    #define COLORSPACE_RGB  1
    #define COLORSPACE_HSV  2

    DynamicCombo( D_COLORSPACE, 0..2, Sys( PC ) ); 
        
    float ColorTempK            < Attribute("ColorTempK"); Default(6500.0f); >;
    float BlendFactor           < Attribute("BlendFactor"); Default(0.0f); >;

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
     
    // RGB curves 

    int      CurveFramesR < Attribute("CurveFramesR"); Default(0); >;
    float4   CurveFrameR0 < Attribute("CurveFrameR0");  >;
    float4   CurveFrameR1 < Attribute("CurveFrameR1");  >;  
    float4   CurveFrameR2 < Attribute("CurveFrameR2");  >;
    float4   CurveFrameR3 < Attribute("CurveFrameR3");  >;
    
    int      CurveFramesG < Attribute("CurveFramesG"); Default(0); >;
    float4   CurveFrameG0 < Attribute("CurveFrameG0");  >;
    float4   CurveFrameG1 < Attribute("CurveFrameG1");  >;  
    float4   CurveFrameG2 < Attribute("CurveFrameG2");  >;
    float4   CurveFrameG3 < Attribute("CurveFrameG3");  >;
    
    int      CurveFramesB < Attribute("CurveFramesB"); Default(0); >;
    float4   CurveFrameB0 < Attribute("CurveFrameB0");  >;
    float4   CurveFrameB1 < Attribute("CurveFrameB1");  >;  
    float4   CurveFrameB2 < Attribute("CurveFrameB2");  >;
    float4   CurveFrameB3 < Attribute("CurveFrameB3");  >;
    
    //  CurveDivisors store the reciprocals of the 'x' deltas  (time in Curve.cs)  between successive point pairs.  to prevent needing divisions in the PS#
    
    float4   CurveDivisorsR < Attribute("CurveDivisorsR");  >;
    float4   CurveDivisorsG < Attribute("CurveDivisorsG");  >;
    float4   CurveDivisorsB < Attribute("CurveDivisorsB");  >;
  
    // HSV curves 
    
    int      CurveFramesH < Attribute("CurveFramesH"); Default(0); >;
    float4   CurveFrameH0 < Attribute("CurveFrameH0");  >;
    float4   CurveFrameH1 < Attribute("CurveFrameH1");  >;  
    float4   CurveFrameH2 < Attribute("CurveFrameH2");  >;
    float4   CurveFrameH3 < Attribute("CurveFrameH3");  >;
    
    int      CurveFramesS < Attribute("CurveFramesS"); Default(0); >;
    float4   CurveFrameS0 < Attribute("CurveFrameS0");  >;
    float4   CurveFrameS1 < Attribute("CurveFrameS1");  >;  
    float4   CurveFrameS2 < Attribute("CurveFrameS2");  >;
    float4   CurveFrameS3 < Attribute("CurveFrameS3");  >;
    
    int      CurveFramesV < Attribute("CurveFramesV"); Default(0); >;
    float4   CurveFrameV0 < Attribute("CurveFrameV0");  >;
    float4   CurveFrameV1 < Attribute("CurveFrameV1");  >;  
    float4   CurveFrameV2 < Attribute("CurveFrameV2");  >;
    float4   CurveFrameV3 < Attribute("CurveFrameV3");  >;
    
    //  CurveDivisors store the reciprocals of the 'x' deltas  (time in Curve.cs)  between successive point pairs.  to prevent needing divisions in the PS#
     
    float4   CurveDivisorsH < Attribute("CurveDivisorsH");  >;
    float4   CurveDivisorsS < Attribute("CurveDivisorsS");  >;
    float4   CurveDivisorsV < Attribute("CurveDivisorsV");  >; 
            
    float3  TemperatureToRGB(float temperature)
    { 
        const float MinT = 1000;
        const float MaxT = 40000;
        
        float3 retColor; 
        float temperatureInKelvins = clamp(temperature, MinT, MaxT) * 0.01f;  
 
        if (temperatureInKelvins <= 66.0)
        {
            retColor.r = 1.0;
            retColor.g = saturate(0.39008157876901960784 * log(temperatureInKelvins) - 0.63184144378862745098);
        }
        else
        {
            float t = temperatureInKelvins - 60.0;
            retColor.r = saturate(1.29293618606274509804 * pow(t, -0.1332047592));
            retColor.g = saturate(1.12989086089529411765 * pow(t, -0.0755148492));
        }
    
        if (temperatureInKelvins >= 66.0)
            retColor.b = 1.0;
        else if(temperatureInKelvins <= 19.0)
                retColor.b = 0.0;
            else
                retColor.b = saturate(0.54320678911019607843 * log(temperatureInKelvins - 10.0) - 1.19625408914);

        return retColor; 
    } 
         
    float CalculateLuminance(float3 color)
    { 
        return dot(color, float3(0.2126f, 0.7152f, 0.0722f)); 
    } 

    // Colour space conversion  RGB to HSV
    // All components are in the range [0�1], including hue.

    float3 rgb2hsv(float3 c)
    {
        float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
        float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
        float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r)); 
        float d = q.x - min(q.w, q.y);
        float e = 1.0e-10;
        return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / max(q.x, e), q.x);
    }  

    float3 hsv2rgb(float3 c)
    {
        float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
        float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y); 
    }

    SamplerState g_sLutSampler < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;

    void GradingLUT( PixelInput i, in out float3 color )
    {         
        float halfColX = i.vData.y;    // 0.5 / texWidth
        float halfColY = i.vData.z;    // 0.5 / texHeight

        float xOffset = halfColX + color.r * 0.05859375;   // 15/256
        float yOffset = halfColY + color.g * 0.9375;       // 15/16

        float b = saturate(color.b) * 15.0;
        float cell0 = floor(b);
        float cell1 = min(cell0 + 1.0, 15.0);
        float f = b - cell0;

        float2 uv0 = float2(cell0 * 0.0625 + xOffset, yOffset);
        float2 uv1 = float2(cell1 * 0.0625 + xOffset, yOffset);

        float3 c0 = LookupTexture.SampleLevel(g_sLutSampler, uv0, 0).rgb;
        float3 c1 = LookupTexture.SampleLevel(g_sLutSampler, uv1, 0).rgb;

        color = lerp(c0, c1, f);
    } 
    
    void GradingTemperature( in out float3 color )
    { 
        float3 colorTempRGB = TemperatureToRGB( ColorTempK );
        color = color * colorTempRGB;
    }

    //
    //  GetYonCurve   -   maths for curve interpolation MUST be same as in  Curve.cs
    //

    float GetYonCurve(float x, float CurveFrames, float4 CurveFrame0, float4 CurveFrame1,  float4 CurveFrame2, float4 CurveFrame3, float4 CurveDivisors)
    {  
        if ( CurveFrames == 0 ) return 0;           // Should never happen as this means no control points on curve! - ie. no curve
           
        // Put the curve parameters into an array.   
        //     This is temporary.   If we only ever have 4 points we could pass a 4x4 matrix in and index each row.
        //                          OR could we pass an array in ? - do we support this ?
             
        float4 frames [4];
        frames[0] = CurveFrame0;
        frames[1] = CurveFrame1;
        frames[2] = CurveFrame2;
        frames[3] = CurveFrame3;

        float recips[3];
        recips[0] = CurveDivisors.x;
        recips[1] = CurveDivisors.y;
        recips[2] = CurveDivisors.z;

        int lastFrameIndex = CurveFrames-1;
        if(  CurveFrame0.x > x )              // our point is before the first curve point
        {
            return  frames[0].y; 
        }
        if( frames[lastFrameIndex].x <= x )              // our point is beyond the last curve point
        {
             return  frames[lastFrameIndex].y; 
        } 
         
        // Since only max. of 4 points,  binchop would be overkill

        float y = 0.f;
        for(int i = 1; i <=  lastFrameIndex; ++i)      // Note:  lastFrameIndex = Frames-1,   so  i+1  is in range
        {
            if( frames[i].x >=  x )
            {
                //  x  sits between  Frame[i-1] and Frame[i]
  
               float t = (x - frames[i-1].x )  * recips[i-1]; //  /  max(frames[i].x - frames[i-1].x, 0.00001f );      //  prevent div by zero when  frames[i-1].x == frames[i].x.  

               /*  frames[] fields correspond to 'Curves.cs frames'
                        x =  Time;			 
                        y =  Value;
                        z =  In;
                        w =  Out;
               */
                  
               float it =  frames[i].z * -1.0f;
               float ot =  frames[i-1].w;
                
               float2 dxdy = frames[i].xy - frames[i-1].xy;   //c1.Time - c0.Time,   c1.Value - c0.Value; 
               y =   frames[i-1].y    +   t * (t * (t * ((it + ot) * dxdy.x - 2.0f * dxdy.y) + (-it - 2.0f * ot) * dxdy.x + 3.0f * dxdy.y) + ot * dxdy.x);
                 
               break;
            } 
        } 
        return y; 
    }

    float3 ChannelBiasingRGB(float3 inPixel) //, PixelInput i)
    { 
        float3 result; 

        // For a channel, say 'r' we need to find which interval to use and interpolate between  
        // then remap the rgb channel
    
        result.r =   GetYonCurve( inPixel.r, CurveFramesR, CurveFrameR0, CurveFrameR1, CurveFrameR2, CurveFrameR3, CurveDivisorsR );
        result.g =   GetYonCurve( inPixel.g, CurveFramesG, CurveFrameG0, CurveFrameG1, CurveFrameG2, CurveFrameG3, CurveDivisorsG );
        result.b =   GetYonCurve( inPixel.b, CurveFramesB, CurveFrameB0, CurveFrameB1, CurveFrameB2, CurveFrameB3, CurveDivisorsB ); 
        return result;
    }

    float3 ChannelBiasingHSV(float3 inPixel) //, PixelInput i)
    {         
        float3 HSV = rgb2hsv( inPixel );    // Note:  all elements  in [0,1] interval -  so Hue is not in the regular 0 to 360 degree

        HSV.x = GetYonCurve( HSV.x, CurveFramesH, CurveFrameH0, CurveFrameH1, CurveFrameH2, CurveFrameH3, CurveDivisorsH );
        HSV.y = GetYonCurve( HSV.y, CurveFramesS, CurveFrameS0, CurveFrameS1, CurveFrameS2, CurveFrameS3, CurveDivisorsS );
        HSV.z = GetYonCurve( HSV.z, CurveFramesV, CurveFrameV0, CurveFrameV1, CurveFrameV2, CurveFrameV3, CurveDivisorsV ); 
        
        return hsv2rgb(HSV);
    }

    // --------------------------------------------------------------------------------------------------------------------------------------------------------

    float4 MainPs( PixelInput i ) : SV_Target0
    {   
        float4 originalColor = g_tColorBuffer.Sample( g_sBilinearMirror, i.vTexCoord );
        float3 color = originalColor.rgb;

        //
        // Color grading
        //

        #if ( D_CGRAD_PASS == GRADING_TEMPERATURE )
            GradingTemperature( color );
        #elif ( D_CGRAD_PASS == GRADING_LUT )
            GradingLUT( i, color );
        #endif

        //
        // Now any 'per channel' adjustment
        //

        #if ( D_COLORSPACE == COLORSPACE_RGB )
             color = ChannelBiasingRGB( color );
        #elif ( D_COLORSPACE == COLORSPACE_HSV )
             color = ChannelBiasingHSV( color );
        #endif 

        color = lerp( originalColor.rgb, color, BlendFactor );

        return float4( color, originalColor.a ); 
    }
}
