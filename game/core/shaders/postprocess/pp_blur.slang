// pp_blur.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// HEADER: DevShader = true;

// MODES: Default();
// MODES: Forward();

#include "sbox/pass.slangh"

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
        
        o.vPositionPs = float4(i.vPositionOs.xy, 0.0f, 1.0f);
        o.vTexCoord = i.vTexCoord;
        return o;
    }

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
    #include "postprocess/common.hlsl"
    #include "postprocess/functions.hlsl"
    #include "procedural.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
    
    float flBlurSize< Attribute("size"); Default(1.0f); >;

    float4 FetchSceneColorBlur( float2 vScreenUv, float blurSize )
    {
        const int   Quality = 4; // Magic number to make the blur look the same as the previous impl
        return g_tColorBuffer.SampleLevel( g_sTrilinearMirror, vScreenUv.xy, blurSize * Quality );
    }

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vScreenUv = ( i.vPositionSs.xy - g_vViewportOffset ) * g_vInvViewportSize;

        return FetchSceneColorBlur( vScreenUv, flBlurSize * 2 );
    }

#endif // PS

