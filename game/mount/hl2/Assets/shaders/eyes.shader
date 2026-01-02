HEADER
{
    Description = "Source Engine Eyes Shader";
    Version = 1;
    DevShader = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_HALFLAMBERT, 0..1, "Half Lambert" );
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
    float2 vIrisTexCoord : TEXCOORD14;
};

VS
{
    #include "common/vertex.hlsl"

    // Iris projection vectors
    float4 g_vIrisU < Default4( 1.0, 0.0, 0.0, 0.5 ); >;
    float4 g_vIrisV < Default4( 0.0, 0.0, 1.0, 0.5 ); >;

    PixelInput MainVs( VS_INPUT i )
    {
        PixelInput o = ProcessVertex( i );

        o.vIrisTexCoord.x = dot( g_vIrisU.xyz, i.vPositionOs.xyz ) + g_vIrisU.w;
        o.vIrisTexCoord.y = dot( g_vIrisV.xyz, i.vPositionOs.xyz ) + g_vIrisV.w;

        return FinalizeVertex( o );
    }
}

PS
{
    #include "common/pixel.hlsl"

    StaticCombo( S_HALFLAMBERT, F_HALFLAMBERT, Sys( ALL ) );

    // $basetexture - eyeball/sclera texture
    CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); Channel( A, Box( TextureColor ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
    TextureAttribute( RepresentativeTexture, g_tColor );

    // $iris - iris/pupil texture
    CreateInputTexture2D( TextureIris, Srgb, 8, "", "_iris", "Eyes,10/10", Default3( 0.5, 0.3, 0.2 ) );
    Texture2D g_tIris < Channel( RGB, Box( TextureIris ), Srgb ); Channel( A, Box( TextureIris ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vUV = i.vTextureCoords.xy;
        float4 vBaseSample = g_tColor.Sample( g_sAniso, vUV );
        float4 vIrisSample = g_tIris.Sample( g_sAniso, i.vIrisTexCoord );
        
        #if S_MODE_DEPTH
            return float4( 0, 0, 0, vBaseSample.a );
        #endif

        float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        float3 vNormalWs = normalize( i.vNormalWs );

        // Blend iris over base using iris alpha
        float3 vResult = lerp( vBaseSample.rgb, vIrisSample.rgb, vIrisSample.a );

        // Lighting
        float3 vLighting = float3( 0, 0, 0 );

        uint nLightCount = Light::Count( i.vPositionSs.xy );
        for ( uint idx = 0; idx < nLightCount; idx++ )
        {
            Light light = Light::From( i.vPositionSs.xy, vPositionWs, idx );
            float3 vLightDir = light.Direction;
            float3 vLightColor = light.Color * light.Attenuation * light.Visibility;
            
            float flNdotL = dot( vNormalWs, vLightDir );
            
            #if S_HALFLAMBERT
                flNdotL = flNdotL * 0.5 + 0.5;
                flNdotL = flNdotL * flNdotL;
            #else
                flNdotL = saturate( flNdotL );
            #endif
            
            vLighting += vLightColor * flNdotL;
        }
        
        float3 vAmbient = AmbientLight::From( vPositionWs, i.vPositionSs.xy, vNormalWs );
        vLighting += vAmbient;

        vResult *= vLighting;

        float4 vColor = float4( vResult, vBaseSample.a );
        vColor = DoAtmospherics( vPositionWs, i.vPositionSs.xy, vColor );

        return vColor;
    }
}
