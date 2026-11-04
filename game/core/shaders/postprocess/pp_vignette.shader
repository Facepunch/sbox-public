HEADER
{
    Description = "Vignette shader";
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

    RenderState( BlendEnable, true );
    RenderState( SrcBlend, ZERO );
    RenderState( DstBlend, SRC_COLOR );
    RenderState( BlendOp, ADD );

    float4 VignetteColor     < Attribute("color");     Default4(0.0f, 0.0f, 0.0f, 1.0f); >;
    float  VignetteIntensity < Attribute("intensity"); Default(1.0f); >;
    float  VignetteFeather   < Attribute("smoothness");   Default(1.0f); >;
    float  VignetteRoundness < Attribute("roundness"); Default(1.0f); >;
    float2 VignetteCenter    < Attribute("center");    Default2(0.5f, 0.5f); >;

    float ComputeVignetteAmount( float2 uv )
    {
        float2 offset = abs(uv - VignetteCenter);

        // Apply roundness/aspect correction
        float2 aspect = float2(g_vRenderTargetSize.x / g_vRenderTargetSize.y, 1.0);
        offset *= lerp(float2(1.0, 1.0), aspect, VignetteRoundness);

        // Scale radius by intensity
        float dist = length(offset) * (0.5 + VignetteIntensity);

        // Falloff using smooth feathering
        float vignette = smoothstep(0.0, 1.0, pow(saturate(dist), lerp(1.0, 6.0, 1.0 - VignetteFeather)));

        return vignette;
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 uv = i.vPositionSs.xy / g_vRenderTargetSize;
        float vignette = ComputeVignetteAmount(uv);

        return lerp(1.0f, VignetteColor, vignette * VignetteColor.a);
    }
}
