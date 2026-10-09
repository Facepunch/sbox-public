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
    float strength <Attribute("strength"); Default(0.0f);>;
    float size <Attribute("size"); Default(1.0f);>;

    float4 Fetch( float2 vScreenUv )
    {
        return g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv.xy );
    }

    float4 Sharpen( float2 uv, float k )
    {
        float2 texel = size / g_vRenderTargetSize;

        float4 c   = Fetch(uv);
        float4 n   = Fetch(uv + float2(0, -texel.y));
        float4 s   = Fetch(uv + float2(0,  texel.y));
        float4 e   = Fetch(uv + float2( texel.x, 0));
        float4 w   = Fetch(uv + float2(-texel.x, 0));
        float4 ne  = Fetch(uv + texel);
        float4 nw  = Fetch(uv + float2(-texel.x,  texel.y));
        float4 se  = Fetch(uv + float2( texel.x, -texel.y));
        float4 sw  = Fetch(uv - texel);

        // 3x3 tent weights: center*4, axial*2, diagonal*1 -> normalize by 16
        float4 blur = (c*4 + (n+s+e+w)*2 + (ne+nw+se+sw)) * (1.0/16.0);

        // simple luminance edge mask to cut halos at flat areas
        float lc = dot(c.rgb,  float3(0.299,0.587,0.114));
        float lb = dot(blur.rgb,float3(0.299,0.587,0.114));
        float edge = saturate(abs(lc - lb) * 4.0);          // tune 4.0 as needed

        float kEff = k * edge;
        float4 outc = c + kEff * (c - blur);

        return saturate( outc );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;
        float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

        color = Sharpen( vScreenUv, strength * 25 );
        return color;
    }
}
