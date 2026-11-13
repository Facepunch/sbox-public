// pp_blitoverlay.shader as a pass module: Basalt.Gpu draws this; the .shader stays for the other routes
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

#if ( PROGRAM == VFX_PROGRAM_VS )
    [shader( "vertex" )] PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        
        o.vPositionPs = float4(i.vPositionOs.xy, 0.0f, 1.0f);
        o.uv = i.vTexCoord;
        return o;
    }

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
    #include "postprocess/common.hlsl"
    #include "postprocess/functions.hlsl"
    #include "procedural.hlsl"

    #include "common/blendmode.hlsl" 

    CreateInputTexture2D( ColorTexture, Srgb,   8, "",  "",  "Material A,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColorBuffer < Channel( RGBA, None( ColorTexture ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

    float blend< Attribute("blend"); >;

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );
        float4 color = g_tColorBuffer.SampleLevel( g_sBilinearMirror, vScreenUv, 0 );

        color.a *= blend;

        return color;
    }

#endif // PS

