// postprocess_standard_p1: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// HEADER: Description = "Standard post processing shader, Pass 1";
// HEADER: DevShader = true;

#include "sbox/pass.slangh"

Mode( Default );
Mode( Forward );

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

    #include "common/classes/Depth.hlsl"
    #include "common/classes/Motion.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" );  	SrgbRead( true ); >;
    
    float flCameraFOV< Attribute("CameraFOV"); Default(0); >;

    DynamicCombo( D_MOTION_BLUR, 0..1, Sys( PC ) );

    float flMotionBlurScale< Attribute("standard.motionblur.scale"); Default(0.05f); >;
    int sMotionBlurSamples< Attribute("standard.motionblur.samples"); Default(16); >;
   
    float4 FetchSceneColor( float2 vScreenUv )
    {
        return g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv.xy );
    }

    // Averages the scene along the pixel's motion this frame, centred on the pixel
    float4 MotionBlurEx(float2 texCoords, float2 vPositionSs)
    {
        float2 vMotion = texCoords - CalculateViewportUv( Motion::Get( vPositionSs ).xy );

        float4 color = 0;
        float invSamples = 1.0f / (float)sMotionBlurSamples;
        for ( int i = 0; i < sMotionBlurSamples; i++ )
            color += FetchSceneColor( texCoords + vMotion * ( ( i + 0.5f ) * invSamples - 0.5f ) );

        return color * invSamples;
    }

    [shader( "pixel" )] float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;

        float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

        #if D_MOTION_BLUR
            // We can't use our predefined motion blur as we need to account for chromatic aberration
            color = MotionBlurEx( vScreenUv, i.vPositionSs.xy );
        #else
            color = FetchSceneColor( vScreenUv );
        #endif

        return color;
    }

#endif // PS

