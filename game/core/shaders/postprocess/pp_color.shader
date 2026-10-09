HEADER
{
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

	// PS only
	#if ( ( PROGRAM == VFX_PROGRAM_PS ) )
		float4 vPositionSs		: SV_Position;
	#endif
};

VS
{
    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        
        o.vPositionPs = float4(i.vPositionOs.xy, 0.0f, 1.0f);
        o.vTexCoord = i.vTexCoord;
        return o;
    }
}

PS
{
    #include "postprocess/common.hlsl"
    #include "postprocess/functions.hlsl"
    #include "procedural.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

    //
    // Color adjustments
    //
    float flBlend< Attribute("blend"); Default(1.0f); >;
    float flHueRotate< Attribute("hue_rotate" ); Default(0.0f); >;
    float flBrightness< Attribute("brightness"); Default(1.0f); >;
    float flContrast< Attribute("contrast"); Default(1.0f); >;
    float flSaturationAmount< Attribute("saturate"); Default(1.0f); >;

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;
        float2 vScreenUv = i.vPositionSs.xy / g_vRenderTargetSize;

        color = g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv.xy );
        float4 oc = color;

        color.rgb = ( (color.rgb - 0.5f) * flContrast + 0.5f );
        float3 vHsv = RgbToHsv( color.rgb );

        vHsv.r = (vHsv.r + (flHueRotate / 360.0f)) % 1.0f;
        vHsv.b *= flBrightness;
        vHsv.g *= flSaturationAmount;

        color.rgb = HsvToRgb( vHsv );

        return lerp( oc, color, flBlend );
    }
}
