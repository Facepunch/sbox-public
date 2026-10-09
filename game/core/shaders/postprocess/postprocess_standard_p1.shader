HEADER
{
    Description = "Standard post processing shader, Pass 1";
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
    #include "postprocess/functions.hlsl"
    #include "procedural.hlsl"

    #include "common/classes/Depth.hlsl"

    Texture2D g_tColorBuffer < Attribute( "ColorBuffer" );  	SrgbRead( true ); >;
    
    float flCameraFOV< Attribute("CameraFOV"); Default(0); >;

    DynamicCombo( D_MOTION_BLUR, 0..1, Sys( PC ) );

    float flMotionBlurScale< Attribute("standard.motionblur.scale"); Default(0.05f); >;
    int sMotionBlurSamples< Attribute("standard.motionblur.samples"); Default(16); >;
   
    float4 FetchSceneColor( float2 vScreenUv )
    {
        return g_tColorBuffer.Sample( g_sBilinearMirror, vScreenUv.xy );
    }

    float2 GetCameraVelocityVector(float2 texCoords)
    {
        // Calculate world space position based on the previous projection
        float3 worldPos = Depth::GetWorldPosition( texCoords * g_vViewportSize );

        // Reproject the world space position to screen space in the previous frame
        float3 prevFramePosSs = ReprojectFromLastFrameSs( worldPos );

        // Calculate the velocity vector based on the current projection
        float2 velocityVector = prevFramePosSs.xy * g_vInvViewportSize;

        return velocityVector;
    }

    // This function applies motion blur to the scene.
    float4 MotionBlurEx(float2 texCoords)
    {
        // Get the velocity vector for the current texture coordinates
        float2 velocityVector = GetCameraVelocityVector(texCoords);

        // Initialize the color accumulator
        float4 color = float4(0.0f, 0.0f, 0.0f, 0.0f);
    
        // Calculate the inverse of the number of samples for motion blur
        float invSamples = 1.0f / (float)sMotionBlurSamples;
    
        // Accumulate the color from each sample along the motion blur path
        float t = invSamples;
        for(float i = 0; i < sMotionBlurSamples; i++)
        {
            float2 uv = lerp(texCoords, velocityVector, t);
            t += invSamples;
            color += FetchSceneColor(uv);
        }
        color *= invSamples; // Normalize the accumulated color by the number of samples
    
        return color;
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 color = 1;

        float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

        #if D_MOTION_BLUR
            // We can't use our predefined motion blur as we need to account for chromatic aberration
            color = MotionBlurEx( vScreenUv );
        #else
            color = FetchSceneColor( vScreenUv );
        #endif

        return color;
    }
}
