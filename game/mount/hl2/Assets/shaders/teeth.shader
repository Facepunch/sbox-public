HEADER
{
    Description = "Source Engine Teeth Shader";
    Version = 1;
    DevShader = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_BUMPMAP, 0..1, "Normal Mapping" );
}

MODES
{
    Forward();
    Depth( S_MODE_DEPTH );
    ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
    #include "common/shared.hlsl"
}

struct VertexInput
{
    #include "common/vertexinput.hlsl"
};

struct PixelInput
{
    #include "common/pixelinput.hlsl"
    float flDarkening : TEXCOORD14;
};

VS
{
    #include "common/vertex.hlsl"

    // $forward - forward direction vector for teeth lighting
    float3 g_vForward < Default3( 1.0, 0.0, 0.0 ); UiGroup( "Teeth,10/10" ); >;
    // $illumfactor - amount to darken or brighten the teeth (default 1)
    float g_flIllumFactor < Default( 1.0 ); Range( 0.0, 2.0 ); UiGroup( "Teeth,10/20" ); >;

    PixelInput MainVs( VS_INPUT i )
    {
        PixelInput o = ProcessVertex( i );

        float3 vNormalOs = normalize( i.vNormalOs.xyz );
        float flForwardDot = saturate( dot( vNormalOs, normalize( g_vForward ) ) );
        o.flDarkening = g_flIllumFactor * flForwardDot;
        
        return FinalizeVertex( o );
    }
}

PS
{
    StaticCombo( S_BUMPMAP, F_BUMPMAP, Sys( ALL ) );

    #include "common/pixel.hlsl"

    // $basetexture
    CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); Channel( A, Box( TextureColor ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
    TextureAttribute( RepresentativeTexture, g_tColor );

    #if S_BUMPMAP
        // $bumpmap
        CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal Map,10/10", Default3( 0.5, 0.5, 1.0 ) );
        Texture2D g_tNormal < Channel( RGB, Box( TextureNormal ), Linear ); Channel( A, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
    #endif

    // $phongexponent - specular exponent (default 100)
    float g_flPhongExponent < Default( 100.0 ); Range( 1.0, 150.0 ); UiGroup( "Teeth,10/30" ); >;

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vUV = i.vTextureCoords.xy;
        
        float4 vBaseSample = g_tColor.Sample( g_sAniso, vUV );
        float3 vAlbedo = vBaseSample.rgb;
        float flAlpha = vBaseSample.a;

        float3 vNormalWs = normalize( i.vNormalWs );
        float flSpecMask = 1.0;
        
        #if S_BUMPMAP
            float4 vNormalSample = g_tNormal.Sample( g_sAniso, vUV );
            float3 vNormalTs = DecodeNormal( vNormalSample.rgb );
            vNormalTs = normalize( vNormalTs );
            vNormalWs = TransformNormal( vNormalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
            flSpecMask = vNormalSample.a;
        #endif

        #if S_MODE_DEPTH
            return float4( 0, 0, 0, flAlpha );
        #endif

        float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        float3 vViewWs = normalize( g_vCameraPositionWs - vPositionWs );

        float3 vDiffuse = float3( 0, 0, 0 );
        float3 vSpecular = float3( 0, 0, 0 );
        
        uint nLightCount = Light::Count( i.vPositionSs.xy );
        for ( uint idx = 0; idx < nLightCount; idx++ )
        {
            Light light = Light::From( i.vPositionSs.xy, vPositionWs, idx );
            float3 vLightDir = light.Direction;
            float3 vLightColor = light.Color * light.Attenuation * light.Visibility;
            float flNdotL = saturate( dot( vNormalWs, vLightDir ) );
            
            vDiffuse += vLightColor * flNdotL;
            
            #if S_BUMPMAP
                float3 vReflect = reflect( -vViewWs, vNormalWs );
                float flRdotL = saturate( dot( vReflect, vLightDir ) );
                float3 vSpec = pow( flRdotL, g_flPhongExponent ) * flNdotL;
                vSpecular += vSpec * vLightColor;
            #endif
        }

        float3 vAmbient = AmbientLight::From( vPositionWs, i.vPositionSs.xy, vNormalWs );
        vDiffuse += vAmbient;

        float flDarkening = i.flDarkening;

        float3 vResult = vAlbedo * vDiffuse;
        
        #if S_BUMPMAP
            vResult += vSpecular * flSpecMask;
        #endif

        vResult *= flDarkening;

        float4 vColor = float4( vResult, 1.0 );
        vColor = DoAtmospherics( vPositionWs, i.vPositionSs.xy, vColor );
        
        return vColor;
    }
}
