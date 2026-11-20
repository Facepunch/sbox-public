//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
    Description = "Glass Shader";
    Version = 3;
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
FEATURES
{
    #include "common/features.hlsl"
    Feature( F_RENDER_SIGHTS_ONLY, 0..1, "Glass");
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
MODES
{
    Forward();                                               // Indicates this shader will be used for main rendering
    ToolsShadingComplexity("tools_shading_complexity.shader"); // Shows how expensive drawing is in debug view
    Depth( S_MODE_DEPTH );
}

//=========================================================================================================================
COMMON
{
    #include "common/shared.hlsl"
}

//=========================================================================================================================

struct VertexInput
{
    #include "common/vertexinput.hlsl"
};

//=========================================================================================================================

struct PixelInput
{
    #include "common/pixelinput.hlsl"
};

//=========================================================================================================================

VS
{
    #include "common/vertex.hlsl"
    //
    // Main
    //
    PixelInput MainVs(VertexInput i)
    {
        PixelInput o = ProcessVertex(i);
        // Add your vertex manipulation functions here
        return FinalizeVertex(o);
    }
}

//=========================================================================================================================

PS
{
    StaticCombo( S_RENDER_SIGHTS_ONLY, F_RENDER_SIGHTS_ONLY, Sys(ALL) );
    DynamicCombo( D_MULTIVIEW_INSTANCING, 0..1, Sys(PC) );

    // Transparency
    #if (S_RENDER_SIGHTS_ONLY)
        RenderState(BlendEnable, true);
        RenderState(SrcBlend, SRC_ALPHA);
        RenderState(DstBlend, INV_SRC_ALPHA);
    #endif

    // Attributes ------------------------------------------------------------------------------------------

    // Hate this
    #define DEPTH_STATE_ALREADY_SET 1
    #define BLEND_MODE_ALREADY_SET 1
    #define S_TRANSLUCENT 1

    #include "common/utils/Material.CommonInputs.hlsl"
    #include "common/pixel.hlsl"
    #include "common/classes/Depth.hlsl"

    BoolAttribute(bWantsFBCopyTexture, !F_RENDER_SIGHTS_ONLY );
    
    Texture2D g_tFrameBufferCopyTexture < Attribute("FrameBufferCopyTexture");   SrgbRead( false ); >;    

    //
    // Blur and Refraction Settings
    //
    float g_flBlurAmount < Default(0.0f); Range(0.0f, 1.0f); UiGroup("Glass,10/10"); > ;
    float g_flRefractionStrength < Default(1.005); Range(1.0, 1.1); UiGroup("Glass,10/20"); > ;
    float g_flIridescence < Default(600.0); Range(0.0f, 1000.0); UiGroup("Glass,10/30"); > ;
    float g_flIridescenceScale < Default(1.0); Range(0.0f, 10.0); UiGroup("Glass,10/30"); > ;

    float3 g_vSightLightColor <  UiType( Color ); Default3(1.0, 1.0, 1.0); UiGroup("Glass,10/40"); > ;
    float g_flSightDistanceScale < Default(1.0); Range(0.0, 20.0); UiGroup("Glass,10/50"); > ;

    //
    // Overlay layer
    //
    CreateInputTexture2D(RedDot, Srgb, 8, "", "_color", "Sight Dot,10/10", Default3(0.0, 0.0, 0.0));
    CreateInputTexture2D(RedDot2, Srgb, 8, "", "_color", "Sight Dot,10/10", Default3(0.0, 0.0, 0.0));
    CreateInputTexture2D(RedDot3, Srgb, 8, "", "_color", "Sight Dot,10/10", Default3(0.0, 0.0, 0.0));
    CreateTexture2DWithoutSampler(g_tRedDot) < Channel(R, Box(RedDot), Linear); Channel(G, Box(RedDot2), Linear); Channel(B, Box(RedDot3), Linear); OutputFormat(BC7); SrgbRead(false); > ;
    SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

    float3 GetIridescence(float3 vCameraDirWs, float3 vNormalWs, float k) 
    {
        const float3 c1 = float3(3.54585104, 2.93225262, 2.41593945);
        const float3 x1 = float3(0.69549072, 0.49228336, 0.27699880);
        const float3 y1 = float3(0.02312639, 0.15225084, 0.52607955);

        const float3 c2 = float3(3.90307140, 3.21182957, 3.96587128);
        const float3 x2 = float3(0.11748627, 0.86755042, 0.66077860);
        const float3 y2 = float3(0.84897130, 0.88445281, 0.73949448);

        float3 color = 0;
        float NDotV = dot(vNormalWs, vCameraDirWs);

        [unroll]
        for (int n = 1; n <= 8; n++) {
            float wavelength = abs( NDotV ) * k / float(n);
            float x = saturate( ( wavelength - 400.0f ) / 300.0f );

            float3 col1 = saturate(1 - (c1 * (x - x1)) * (c1 * (x - x1)) - y1);
            float3 col2 = saturate(1 - (c2 * (x - x2)) * (c2 * (x - x2)) - y2);

            color += col1 + col2;
        }

        return color;
    }

    //
    // Main
    //
    float4 MainPs(PixelInput i)  : SV_Target0
    {
        Material m = Material::From(i);

        // Shadows
        #if S_MODE_DEPTH
        {
            float flOpacity = CalcBRDFReflectionFactor(dot(-i.vNormalWs.xyz, g_vCameraDirWs.xyz), m.Roughness, 0.04).x;

            flOpacity = pow(flOpacity, 1.0f / 2.0f);
            flOpacity = lerp(flOpacity, 0.75f, sqrt(m.Roughness));       // Glossiness
            flOpacity = lerp(flOpacity, 1.0 - dot(-i.vNormalWs.xyz, g_vCameraDirWs.xyz), ( g_flRefractionStrength - 1.0f ) * 5.0f );       // Refraction
            flOpacity = lerp(1.0f, flOpacity + 0.04f, length(m.Albedo)); // Albedo absorption

            OpaqueFadeDepth(flOpacity, i.vPositionSs.xy);

            return 1;
        }
        #endif

        m.Metalness = 0; // Glass is always non-metallic

        float3 vViewRayWs = normalize(i.vPositionWithOffsetWs.xyz);
        float flNDotV = saturate(dot(-m.Normal, vViewRayWs));
        float3 vEnvBRDF = CalcBRDFReflectionFactor(flNDotV, m.Roughness, 0.04);

        float4 vDotColor;

        {
            float4 vRefractionColor = 0;

            float flDepthPs = 1.0f - Depth::GetNormalized( i.vPositionSs.xy );
            float3 vRefractionWs = RecoverWorldPosFromProjectedDepthAndRay(flDepthPs, normalize(i.vPositionWithOffsetWs.xyz)) - g_vCameraPositionWs;
            float flDistanceVs = distance(i.vPositionWithOffsetWs.xyz, vRefractionWs);

            float3 vRefractRayWs = refract(vViewRayWs, m.Normal, 1.0 / g_flRefractionStrength);
            float3 vRefractWorldPosWs = i.vPositionWithOffsetWs.xyz + vRefractRayWs * flDistanceVs;

            float4 vPositionPs = Position4WsToPs( float4(vRefractWorldPosWs, 0));

            float2 vPositionSs = vPositionPs.xy / vPositionPs.w;
            vPositionSs = vPositionSs * 0.5 + 0.5;

            vPositionSs.y = 1.0 - vPositionSs.y;

            float flAmount;
            float3 vAlbedoTint = m.Albedo;
            vAlbedoTint += GetIridescence( vViewRayWs, m.Normal, g_flIridescence ) * g_flIridescenceScale * m.Albedo;

            //
            // Color and blur
            //
            {
                flAmount = g_flBlurAmount * m.Roughness * (1.0 - (1.0 / flDistanceVs));

                // Isotropic blur based on grazing angle
                flAmount /= flNDotV;

                const int nNumMips = 7;

                float2 vUV = float2(vPositionSs) * g_vFrameBufferCopyInvSizeAndUvScale.zw;

                vRefractionColor = g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearMirror, vUV, sqrt(flAmount) * nNumMips );
            }

            // Blend
            {
                m.Emission = lerp(vRefractionColor.xyz, 0.0f, vEnvBRDF);
                m.Emission *= vAlbedoTint;
                m.Albedo = 0;            
            }

            // Sight dot
            {
                float3 vRedDotColor = g_vSightLightColor * 20.0f;

                float3 vCameraToPosition = i.vPositionWithOffsetWs;
                float3 vCameraDir = normalize( vCameraToPosition );

                float flProjectDirOntoNormal = 1.0f / dot( vCameraDir, m.Normal );
                float flProjectA =  -g_flSightDistanceScale * flProjectDirOntoNormal ;

                float flProjectDirOntoTangentU = dot( vCameraDir, i.vTangentUWs ) ;
                float flProjectDirOntoTangentV = dot( vCameraDir, i.vTangentVWs ) ;

                float2 vRedDotUV = saturate( i.vTextureCoords.xy + ( flProjectA * 0.5 ) * float2( flProjectDirOntoTangentU, flProjectDirOntoTangentV ) );
                float2 vRedDotUV2 = saturate( i.vTextureCoords.xy + ( flProjectA * 1.1 ) * float2( flProjectDirOntoTangentU, flProjectDirOntoTangentV ) );
                float2 vRedDotUV3 = saturate( i.vTextureCoords.xy + ( flProjectA * 1.2 ) * float2( flProjectDirOntoTangentU, flProjectDirOntoTangentV ) );
                
                float flRedDot = Tex2DLevelS(g_tRedDot, g_sSampler0, vRedDotUV, sqrt(flAmount) * 7 ).r;
                flRedDot += Tex2DLevelS(g_tRedDot, g_sSampler0, vRedDotUV2, sqrt(flAmount) * 7 ).g;
                flRedDot += Tex2DLevelS(g_tRedDot, g_sSampler0, vRedDotUV3, sqrt(flAmount) * 7 ).b;

                vDotColor = float4( flRedDot * vRedDotColor * vAlbedoTint, flRedDot );
                m.Emission = lerp(m.Emission, vDotColor.rgb, vDotColor.a );
            }

            #if S_MODE_TOOLS_VIS
                m.Albedo = m.Emission;
                m.Emission = 0;
            #endif
        }

        #if S_RENDER_SIGHTS_ONLY
            return vDotColor;
        #endif

        return ShadingModelStandard::Shade(i, m);
    }
}