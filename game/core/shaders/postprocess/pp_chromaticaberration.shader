HEADER
{
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
    #include "postprocess/functions.hlsl"
    #include "procedural.hlsl"

    #include "common/classes/Depth.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
    float3 caAmount< Attribute("amount"); Default3(0.004f, 0.006f, 0.0f); >;
    float caScale< Attribute("scale"); Default(0.0f);>;
   
    float4 ChromaticAberration( float2 vTexCoords )
    {
        float2 offsetScale = (vTexCoords - 0.5) * caScale * 10.0f;

        float4 r = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - (offsetScale * caAmount.r ));
        float4 g = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - (offsetScale * caAmount.g ));
        float4 b = g_tColorBuffer.Sample( g_sBilinearMirror, vTexCoords - (offsetScale * caAmount.b ));

        return float4( r.r, g.g, b.b, ( r.a + g.a + b.a ) / 3.0f );
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;
        float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

        color = ChromaticAberration( vScreenUv );

        return color;
    }
}
