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
    float2 uv : TEXCOORD0;

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
        o.uv = i.vTexCoord;
        return o;
    }
}

PS
{
    #include "postprocess/common.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;

    float scale <Attribute("scale"); Default(1.0f);>;

    float4 Fetch( float2 vScreenUv )
    {
        return g_tColorBuffer.SampleLevel( g_sTrilinearClamp, vScreenUv.xy, scale );
    }

    float2 PixelateUV(float2 uv, float scale01)
    {
        // Map scale to pixel count across X. Higher scale -> fewer pixels.
        // Tweak gamma to shape response.
        const float gamma = 0.25;           // lower = more aggressive
        const float minPixAcross = 8.0;     // smallest across X
        float W = g_vRenderTargetSize.x;
        float H = g_vRenderTargetSize.y;
        float t = pow(saturate(scale01), gamma);
        float pixAcrossX = lerp(W, minPixAcross, t);

        // Square pixels: integer Y count from aspect
        float pixAcrossY = round(pixAcrossX * H / W);

        float2 count   = float2(pixAcrossX, pixAcrossY);
        float2 invCnt  = rcp(count);
        float2 snapped = (floor(uv * count) + 0.5) * invCnt; // center of the block
        return snapped;
    }
    

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;
        float2 uv = CalculateViewportUv( i.vPositionSs.xy );

        float enabled = step(1e-5, scale);
        float2 pUV = PixelateUV(uv, scale);
        uv = lerp(uv, pUV, enabled);

        return Fetch( uv );
    }
}
