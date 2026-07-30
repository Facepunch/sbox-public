MODES
{
    Default();
}

CS
{
    #include "postprocess/shared.hlsl"
    #include "common/classes/Depth.hlsl"
    
    //--------------------------------------------------------------------------------------
    SamplerState BilinearClamp      < Filter(BILINEAR); AddressU(CLAMP); AddressV(CLAMP); AddressW(CLAMP); >;
    SamplerState Point              < Filter(POINT);    AddressU(CLAMP); AddressV(CLAMP); AddressW(CLAMP); >;

    #if D_MSAA
    Texture2DMS<float4> Color       < Attribute("Color"); >;
    Texture2DMS<float>  DepthTexture  < Attribute("Depth"); >;
    #else
    Texture2D Color                 < Attribute("Color"); >;
    Texture2D DepthTexture          < Attribute("Depth"); >;
    #endif

    Texture2D VerticalSRV           < Attribute("VerticalSRV"); >;
    Texture2D DiagonalSRV           < Attribute("DiagonalSRV"); >;
    Texture2D FinalSRV              < Attribute("FinalSRV"); >;

    RWTexture2D<float4> Vertical    < Attribute("Vertical"); >;
    RWTexture2D<float4> Diagonal    < Attribute("Diagonal"); >;
    RWTexture2D<float4> Final       < Attribute("Final"); >;

    int Radius                      < Attribute("Radius"); >;
    float FocusPlane                < Attribute("FocusPlane"); >;
    float FocalLength               < Attribute("FocalLength"); >;
    float2 InvDimensions            < Attribute("InvDimensions"); >;

    float StepScale                    < Attribute("StepScale"); >;



    #define PI 3.14159265359
    #define HALF_MAX 65504.0f

    //--------------------------------------------------------------------------------------

    enum BlurPasses
    {
        CircleOfConfusion,
        DiagonalBlur,
        HexagonalBlur
    };

    enum DoFTypes
    {
        Back,
        Front
    };

    DynamicCombo(D_PASS, 0..2, Sys(All));
    DynamicCombo(D_DOF_TYPE, 0..1, Sys(All));
    DynamicCombo(D_MSAA, 0..1, Sys(All));

    //--------------------------------------------------------------------------------------
    // Circle of Confusion Calculation
    //--------------------------------------------------------------------------------------

    // Gets the linear depth from nearest and furthest depth

    float GetDepthLinear(uint2 screenPos, int sampleIndex)
    {
        float depth;
        #if D_MSAA
            depth = DepthTexture.Load( screenPos, sampleIndex ).r;
        #else
            depth = DepthTexture.Load( int3( screenPos, 0 ) ).r;
        #endif

        depth = 1.0 - Depth::Normalize(depth);
        float a = g_flFarPlane / (g_flFarPlane - g_flNearPlane);
        float b = g_flFarPlane * g_flNearPlane / (g_flNearPlane - g_flFarPlane);
        return (b / (depth - a));
    }

    float3 GetColor(uint2 screenPos, int sampleIndex)
    {
        #if D_MSAA
        return Color.Load( screenPos, sampleIndex ).rgb;
        #else
        return Color.Load( int3( screenPos, 0 ) ).rgb;
        #endif
    }

    float ComputeCircleOfConfusion(float sceneDepth)
    {
        float focalLength = FocalLength;
        float focalDistance = FocusPlane;
        float CoC = (focalDistance - sceneDepth) / focalLength;

        //
        // Invert CoC range for back
        //
        if ( D_DOF_TYPE == DoFTypes::Back ) // Back
        {
            CoC = -CoC;
        }

        return saturate(CoC); // saturated to clamp from 0..1, BlurTexture is tailored to this range
    }

    void CircleOfConfusionPass(uint3 DTid : SV_DispatchThreadID)
    {
        float depth = 0;
        float3 color = 0;
        int validSamples = 0;

        uint nSampleCount = 1;
        uint2 dim;
        #if D_MSAA
            DepthTexture.GetDimensions(dim.x, dim.y, nSampleCount);
        #else
            DepthTexture.GetDimensions(dim.x, dim.y);
        #endif

        const uint DownsampleExp = 2;
        
        for ( int x = 0; x < DownsampleExp; x++ ) // Iterate over quad offsets
        for ( int y = 0; y < DownsampleExp; y++ )
        for (int j = 0; j < nSampleCount; j++) // Iterate over MSAA samples if applicable
        {
            uint2 offset = int2(x, y) + ( DownsampleExp / 2 );
            uint2 p = DTid.xy * DownsampleExp + offset;

            float d = GetDepthLinear( p, j);
            float3 c = GetColor( p, j);

            if (D_DOF_TYPE == DoFTypes::Back)
            {
                if (d < FocusPlane)
                    continue;
            }

            depth += d;
            color += c;
            validSamples++;
        }

        if (validSamples > 0)
        {
            depth /= validSamples;
            color /= validSamples;
        }
        color = min( color, HALF_MAX );
        float coc = ComputeCircleOfConfusion(depth);

        Final[DTid.xy] = float4(color, coc);
    }

    //--------------------------------------------------------------------------------------
    // Bokeh Blur Texture
    //--------------------------------------------------------------------------------------

    float4 BlurTexture(Texture2D tex, float2 uv, float2 direction)
    {
        float4 center = tex.SampleLevel(BilinearClamp, uv, 0.0f);
        
        uv += direction * StepScale * 0.5f; // Offset first sample a bit to not self-intersect
        
        float4 finalColor = 0;
        float total = 0.0f;
        
        for ( int i = 0; i < Radius; ++i )
        {
            float2 sampleUV = uv + direction * i * StepScale;
            float4 sampleColor = tex.SampleLevel(BilinearClamp, sampleUV, 0.0f);

            //
            // Make sure we don't get samples that are in front for back blurs and vice versa
            //
            if (D_DOF_TYPE == DoFTypes::Back)
            {
                sampleColor.a = min(sampleColor.a, center.a);
            }
            else
            {
                sampleColor.a = max(sampleColor.a, center.a);
            }

            if ( sampleColor.a * Radius < i)
            {
                // Front blurs continue, back blurs stop
                if (D_DOF_TYPE == DoFTypes::Back)
                    break;
                else if (D_DOF_TYPE == DoFTypes::Front  && sampleColor.a > 0 )
                    continue; // No optimization, should check depth delta of entire tile with depth chain :(
            }

            finalColor += sampleColor;
            total += 1.0;
        }
        finalColor.xyz = min( finalColor.xyz, HALF_MAX );
        
        return finalColor / total;
    }

    //--------------------------------------------------------------------------------------
    // Diagonal Blur Pass
    //--------------------------------------------------------------------------------------

    void DiagonalBlurPass(uint3 DTid : SV_DispatchThreadID)
    {
        float2 uv = float2(DTid.xy) * InvDimensions;
        float coc = Final[DTid.xy].a;

        float2 blurDir = InvDimensions * float2(0, 1);
        float2 blurDir2 = InvDimensions * float2(cos(-PI / 6), sin(-PI / 6));

        float4 vertical = BlurTexture(FinalSRV, uv, blurDir);
        float4 diagonal = BlurTexture(FinalSRV, uv, blurDir2);

        diagonal.xyz += vertical.xyz;

        Vertical[DTid.xy] = vertical;
        Diagonal[DTid.xy] = diagonal;
    }

    //--------------------------------------------------------------------------------------
    // Hexagonal Blur Pass
    //--------------------------------------------------------------------------------------

    void HexagonalBlurPass(uint3 DTid : SV_DispatchThreadID)
    {
        float2 uv = float2(DTid.xy) * InvDimensions;
        float4 color = 0;

        float2 blurDir = InvDimensions * float2(cos(-PI / 6), sin(-PI / 6));
        color += BlurTexture(VerticalSRV, uv, blurDir);

        float2 blurDir2 = InvDimensions * float2(cos(-5 * PI / 6), sin(-5 * PI / 6));
        color += BlurTexture(DiagonalSRV, uv, blurDir2);

        Final[DTid.xy] = color / 3;
    }

    //--------------------------------------------------------------------------------------
    // Main Compute Shader Entry Point
    //--------------------------------------------------------------------------------------

    [numthreads(16, 16, 1)]
    void MainCs(uint3 DTid : SV_DispatchThreadID)
    {
        #if (D_PASS == 0)
            CircleOfConfusionPass(DTid);
        #elif (D_PASS == 1)
            DiagonalBlurPass(DTid);
        #elif (D_PASS == 2)
            HexagonalBlurPass(DTid);
        #endif
    }
}