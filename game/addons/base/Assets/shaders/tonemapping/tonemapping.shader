HEADER
{
    Description = "Tonemapping";
    DevShader = true;
}  

MODES
{ 
    Default();
    Forward();
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

    // VS only
    #if ( PROGRAM == VFX_PROGRAM_VS )
        float4 vPositionPs		: SV_Position;
    #endif
};
 
VS
{    
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionPs = float4(i.vPositionOs.xyz, 1.0f);
        o.vPositionPs.z = 1;
        o.vTexCoord = i.vTexCoord;
        return o;
    } 
} 

PS 
{
    #include "postprocess/common.hlsl"

    #define TONEMAPPING_HABLE_FILMIC 1
    #define TONEMAPPING_ACES 2
    #define TONEMAPPING_REINHARDJODIE 3
    #define TONEMAPPING_LINEAR 4
    #define TONEMAPPING_AGX 5

    DynamicCombo( D_TONEMAPPING, 0..5, Sys( PC ) ); 
   
    #define ExposureMethod_RGB        0
    #define ExposureMethod_LUM        1

    DynamicCombo( D_EXPOSUREMETHOD, 0..1, Sys( PC ) );   
     
    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( false ); >;
         
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
     
    float3 ReplaceLuminanceInRGB(in float3 RGB,  float originalLuminance)
    {   
        float3 HSV = rgb2hsv( RGB );
        HSV.z = originalLuminance;
        return hsv2rgb( HSV ); 
    }

    // Only used for Hable
    void RemapForExposureBias( in out float3 RGB )
    { 
    #if ( D_EXPOSUREMETHOD == ExposureMethod_RGB )
        // Do nothing
    #elif ( D_EXPOSUREMETHOD == ExposureMethod_LUM )
        float originalL = CalculateLuminance(RGB);
        RGB = ReplaceLuminanceInRGB( RGB, originalL );
    #endif
    }

    void ToneMapping_ReinhardJodie( in out float3 color )
    {
        float L = CalculateLuminance( color );

        float3 tv = color / ( 1.0f + color );
        color = lerp( color / ( 1.0f + L ), tv, tv ); 
    }

    float3 uncharted2_tonemap_partial(float3 x)
    {
         const float A = 0.15f;
         const float B = 0.50f;
         const float C = 0.10f;
         const float D = 0.20f;
         const float E = 0.02f;
         const float F = 0.30f;
         return ((x*(A*x+C*B)+D*E)/(x*(A*x+B)+D*F))-E/F;
    }

    void ToneMapping_HableFilmic( in out float3 color )
    {
        RemapForExposureBias( color );
        float3 curr = uncharted2_tonemap_partial( color );
        float3 W = 11.2f;
        float3 white_scale = 1.0f / uncharted2_tonemap_partial( W );
        color = curr * white_scale;
    }

    void ToneMapping_Aces( in out float3 color )
    {
        const float3x3 m1  = 
        {
            0.59719, 0.07600, 0.02840,
            0.35458, 0.90834, 0.13383,
            0.04823, 0.01566, 0.83777
        };                

        const float3x3 m2  = 
        {
             1.60475, -0.10208, -0.00327,
             -0.53108,  1.10813, -0.07276,
            -0.07367, -0.00605,  1.07602
        }; 

        // Note:   mul ( rgb, m1 )  == mul ( transpose(m1), rgb )
        float3 v = mul( color, m1 ) ;
        float3 a = v * (v + 0.0245786) - 0.000090537;
        float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
        color = saturate( mul(  (a / b), m2  ));  
    }

    float3 AgxTonescale( float3 v )
    {
        // Similar implementation to Blender
        float3 v2 = v * v;
        float3 v4 = v2 * v2;
        return -0.20687445 * v + 6.80888933 * v2 - 37.60519607 * v2 * v + 93.32681938 * v4 - 95.2780858 * v4 * v + 33.96372259 * v4 * v2;
    }

    void ToneMapping_Agx( in out float3 vColor )
    {
        float3x3 mAgx = float3x3(
            0.856627153315983,      0.137318972929847,      0.11189821299995,
            0.0951212405381588,     0.761241990602591,      0.0767994186031903,
            0.0482516061458583,     0.101439036467562,      0.811302368396859
        );

        float flExposureMin = -12.47393f;
        float flExposureMax = 4.026069f;

        // Prevent negative values
        vColor = max( vColor, 0.0.xxx );

        // Apply
        vColor = mul( mAgx, vColor );

        // Log2 transform
        vColor = max( vColor, 1e-10.xxx );
        vColor = clamp( log2( vColor ), flExposureMin, flExposureMax );
        vColor = ( vColor - flExposureMin ) / ( flExposureMax - flExposureMin );

        vColor = AgxTonescale( vColor );
        
        // custom power that attempts to remove some of AgX's natural
        // tendency to desaturate blues and greens, resulting in
        // a more neutral look
        float3 vPower = float3( 1.2, 1.0, 1.0 );
        float flSat = 1.4; // Raise saturation to match ACES
    
        vColor = pow( vColor, vPower );
    
        float3 flLumaWeight = float3( 0.2126, 0.7152, 0.0722 );
        float flLuma = dot( vColor, flLumaWeight );

        vColor = flLuma + flSat * ( vColor - flLuma );

        // AgX outset matrix combined with Rec2020 to linear sRGB
        const float3x3 mAgxInv = float3x3(
            1.19687900512017,       -0.0528968517574562,    -0.0529716355144438,
            -0.0980208811401368,     1.15190312990417,      -0.0980434501171241,
            -0.0990297440797205,    -0.0989611768448433,    1.15107367264116
        );

        vColor = pow( vColor, 2.2.xxx );
        vColor = mul( mAgxInv, vColor );
        vColor = max( vColor, 0.0.xxx );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {   
        float4 color = g_tColorBuffer.Sample( g_sPointClamp, i.vTexCoord );

        float3 rgb = max(0.0.xxx, color.rgb) * g_flToneMapScalarLinear;

        #if ( D_TONEMAPPING == TONEMAPPING_LINEAR ) 
            // it's already linear
        #elif ( D_TONEMAPPING == TONEMAPPING_REINHARDJODIE ) 
              ToneMapping_ReinhardJodie( rgb );
        #elif ( D_TONEMAPPING == TONEMAPPING_HABLE_FILMIC )
              ToneMapping_HableFilmic( rgb );
        #elif ( D_TONEMAPPING == TONEMAPPING_ACES )
              ToneMapping_Aces( rgb );
        #elif ( D_TONEMAPPING == TONEMAPPING_AGX )
              ToneMapping_Agx( rgb );
        #endif

        return float4( rgb, color.a ); 
    }
}
