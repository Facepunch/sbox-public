HEADER
{
    Description = "Bloom post-process shader";
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

    Texture2D ColorBuffer < Attribute( "ColorBuffer" ); >;
    Texture2D BloomTexture < Attribute( "BloomTexture" ); >;
    int CompositeMode< Attribute("CompositeMode"); Default(0); >;
    float BloomScale < Attribute("BloomScale"); Default(0.0f); >;
    float3 Tint < Attribute("Tint"); Default3(1.0f, 1.0f, 1.0f); >;

    float3 ScreenHDR(float3 base, float3 blend)
    {
        base = max(base, 0.0f);
        blend = max(blend, 0.0f);
        float3 screenTerm = 1.0f - (1.0f - saturate(base)) * (1.0f - saturate(blend));
        float3 excessBase = max(base - 1.0f, 0.0f);
        float3 excessBlend = max(blend - 1.0f, 0.0f);
        return screenTerm + excessBase + excessBlend;
    }

    float4 MainPs(PixelInput input) : SV_Target0
    {
        float2 vScreenUv = (input.vPositionSs.xy + g_vViewportOffset) / g_vViewportSize;

        float4 baseColor = ColorBuffer.Sample(g_sBilinearMirror, vScreenUv.xy);
        float4 bloom = BloomTexture.Sample(g_sBilinearMirror, vScreenUv.xy);
        bloom.rgb *= Tint * BloomScale;

        float3 finalColor = 0;
        if (CompositeMode == 0)
        {
            finalColor = baseColor.rgb + bloom.rgb; // Additive
        }
        else if (CompositeMode == 1)
        {
            finalColor = ScreenHDR(baseColor.rgb, bloom.rgb); // Screen
        }
        else // 2: Lerp by bloom luminance
        {
            float bloomLuminance = Luminance(bloom.rgb);
            finalColor = lerp(baseColor.rgb, bloom.rgb, saturate(bloomLuminance));
        }
        
        // Approximate bloom alpha with brightness so it bleeds fine on translucent backgrounds
        float alpha = max(baseColor.a, Luminance( finalColor.rgb ) );

        return float4(finalColor, alpha);
    }

}
