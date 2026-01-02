HEADER
{
    Description = "Source Engine LightmappedGeneric Shader";
    Version = 1;
    DevShader = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_TRANSLUCENT, 0..1, "Rendering" );
    Feature( F_ALPHA_TEST, 0..1, "Rendering" );
    Feature( F_BUMPMAP, 0..1, "Normal Mapping" );
    Feature( F_SELFILLUM, 0..1, "Self Illumination" );
    Feature( F_DETAIL, 0..1, "Detail Texture" );
    Feature( F_ENVMAP, 0..1, "Environment Map" );
    Feature( F_BLEND, 0..1, "Texture Blending" );
    Feature( F_SEAMLESS, 0..1, "Seamless Mapping" );
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
};

VS
{
    #include "common/vertex.hlsl"

    PixelInput MainVs( VS_INPUT i )
    {
        PixelInput o = ProcessVertex( i );
        return FinalizeVertex( o );
    }
}

PS
{
    StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
    StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
    StaticCombo( S_ADDITIVE_BLEND, F_ADDITIVE_BLEND, Sys( ALL ) );
    StaticCombo( S_BUMPMAP, F_BUMPMAP, Sys( ALL ) );
    StaticCombo( S_SELFILLUM, F_SELFILLUM, Sys( ALL ) );
    StaticCombo( S_DETAIL, F_DETAIL, Sys( ALL ) );
    StaticCombo( S_ENVMAP, F_ENVMAP, Sys( ALL ) );
    StaticCombo( S_BLEND, F_BLEND, Sys( ALL ) );
    StaticCombo( S_SEAMLESS, F_SEAMLESS, Sys( ALL ) );

    #include "common/pixel.hlsl"

    float SourceFresnel4( float3 vNormal, float3 vEyeDir )
    {
        // Traditional fresnel using 4th power (square twice)
        float fresnel = saturate( 1.0 - dot( vNormal, vEyeDir ) );
        fresnel = fresnel * fresnel;
        return fresnel * fresnel;
    }

    // $basetexture
    CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); Channel( A, Box( TextureColor ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
    TextureAttribute( RepresentativeTexture, g_tColor );

    // $color - color tint (default [1 1 1])
    float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material,10/30" ); >;

    // $alpha - opacity (default 1)
    float g_flAlpha < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Material,10/40" ); >;

    #if S_SEAMLESS
        // $seamless_scale - seamless texture mapping scale (default 0)
        float g_flSeamlessScale < Default( 1.0 ); Range( 0.001, 10.0 ); UiGroup( "Material,10/60" ); >;
    #endif

    #if S_BUMPMAP
        // $bumpmap
        CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal Map,10/10", Default3( 0.5, 0.5, 1.0 ) );
        Texture2D g_tNormal < Channel( RGB, Box( TextureNormal ), Linear ); Channel( A, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
    #endif

    #if S_BLEND
        // $basetexture2
        CreateInputTexture2D( TextureColor2, Srgb, 8, "", "_color2", "Blending,10/10", Default3( 1.0, 1.0, 1.0 ) );
        Texture2D g_tColor2 < Channel( RGB, Box( TextureColor2 ), Srgb ); Channel( A, Box( TextureColor2 ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
        
        #if S_BUMPMAP
            // $bumpmap2
            CreateInputTexture2D( TextureNormal2, Linear, 8, "NormalizeNormals", "_normal2", "Blending,10/20", Default3( 0.5, 0.5, 1.0 ) );
            Texture2D g_tNormal2 < Channel( RGB, Box( TextureNormal2 ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
        #endif
        
        // $blendmodulatetexture - R channel = blend point, G channel = blend range
        CreateInputTexture2D( TextureBlendModulate, Linear, 8, "", "_blendmod", "Blending,10/30", Default3( 0.5, 0.0, 0.0 ) );
        Texture2D g_tBlendModulate < Channel( RG, Box( TextureBlendModulate ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
    #endif

    #if S_DETAIL
        // $detail
        CreateInputTexture2D( TextureDetail, Srgb, 8, "", "_detail", "Detail,10/10", Default3( 0.5, 0.5, 0.5 ) );
        Texture2D g_tDetail < Channel( RGB, Box( TextureDetail ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
        
        // $detailscale - detail texture UV scale (default 4)
        float g_flDetailScale < Default( 4.0 ); Range( 0.1, 32.0 ); UiGroup( "Detail,10/20" ); >;
        // $detailblendfactor - detail blend amount (default 1)
        float g_flDetailBlendFactor < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Detail,10/30" ); >;
        // $detailblendmode - blend mode 0-9 (default 0 = mod2x)
        int g_nDetailBlendMode < Default( 0 ); Range( 0, 9 ); UiGroup( "Detail,10/40" ); >;
        // $detailtint - detail color tint (default [1 1 1])
        float3 g_vDetailTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Detail,10/50" ); >;
    #endif

    #if S_ENVMAP
        // $envmapmask
        CreateInputTexture2D( TextureEnvMapMask, Linear, 8, "", "_envmapmask", "Environment Map,10/10", Default( 1.0 ) );
        // $envmap
        CreateInputTextureCube( TextureEnvMap, Srgb, 8, "", "_envmap", "Environment Map,10/15", Default3( 0.0, 0.0, 0.0 ) );
        Texture2D g_tEnvMapMask < Channel( R, Box( TextureEnvMapMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
        TextureCube g_tEnvMap < Channel( RGBA, Box( TextureEnvMap ), Srgb ); OutputFormat( BC6H ); SrgbRead( true ); >;
        
        // $envmaptint - envmap color tint (default [1 1 1])
        float3 g_vEnvMapTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Environment Map,10/20" ); >;
        // $envmapcontrast - 0=normal, 1=color*color (default 0)
        float g_flEnvMapContrast < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/30" ); >;
        // $envmapsaturation - 0=greyscale, 1=normal (default 1)
        float g_flEnvMapSaturation < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/40" ); >;
        // $fresnelreflection - 1=mirror, 0=water (default 1)
        float g_flFresnelReflection < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/45" ); >;
        // $basealphaenvmapmask - use base alpha as envmap mask (default 0)
        int g_nBaseAlphaEnvMapMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Environment Map,10/50" ); >;
        // $normalmapalphaenvmapmask - use normalmap alpha as envmap mask (default 0)
        int g_nNormalMapAlphaEnvMapMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Environment Map,10/60" ); >;
    #endif

    #if S_SELFILLUM
        // $selfillumtint - self-illumination color tint (default [1 1 1])
        float3 g_vSelfIllumTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Self Illumination,10/20" ); >;
    #endif

    float2 CalcSeamlessUV( float3 vPositionWs, float3 vNormalWs, float flScale )
    {
        float3 vAbsNormal = abs( vNormalWs );
        float2 vUV;
        
        if ( vAbsNormal.z > max( vAbsNormal.x, vAbsNormal.y ) )
            vUV = vPositionWs.xy;
        else if ( vAbsNormal.y > vAbsNormal.x )
            vUV = vPositionWs.xz;
        else
            vUV = vPositionWs.yz;
            
        return vUV * flScale;
    }

    float3 ApplyDetailTexture( float3 vBase, float3 vDetail, int nBlendMode, float flBlendFactor )
    {
        switch ( nBlendMode )
        {
            case 0: // Mod2x
            default:
                return vBase * lerp( float3( 1, 1, 1 ), vDetail * 2.0, flBlendFactor );
            case 1: // Additive
            case 5: // Unlit additive
            case 6: // Unlit additive threshold fade
                return saturate( vBase + vDetail * flBlendFactor );
            case 2: // Translucent detail
            case 3: // Blend factor fade
                return lerp( vBase, vDetail, flBlendFactor );
            case 4: // Translucent base
            case 9: // Base over detail
                return lerp( vDetail, vBase, flBlendFactor );
            case 7: // Two-pattern decal modulate
                return vBase * lerp( float3( 1, 1, 1 ), vDetail * 2.0, flBlendFactor );
            case 8: // Multiply
                return vBase * lerp( float3( 1, 1, 1 ), vDetail, flBlendFactor );
        }
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float2 vUV = i.vTextureCoords.xy;
        
        #if S_SEAMLESS
            vUV = CalcSeamlessUV( i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs, i.vNormalWs, g_flSeamlessScale );
        #endif

        // Sample base texture
        float4 vBaseTexture = g_tColor.Sample( g_sAniso, vUV );
        float3 vAlbedo = vBaseTexture.rgb * g_vColorTint;
        float flBaseAlpha = vBaseTexture.a;
        float flAlpha = flBaseAlpha * g_flAlpha;

        // Normal mapping
        float3 vNormalTs = float3( 0.0, 0.0, 1.0 );
        float flNormalAlpha = 1.0;
        #if S_BUMPMAP
            float4 vNormalSample = g_tNormal.Sample( g_sAniso, vUV );
            vNormalTs = DecodeNormal( vNormalSample.rgb );
            vNormalTs = normalize( vNormalTs );
            flNormalAlpha = vNormalSample.a;
        #endif
        
        float3 vNormalWs = TransformNormal( vNormalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

        #if S_BLEND
        {
            float flBlendFactor = i.vVertexColor.a;

            float2 vBlendMod = g_tBlendModulate.Sample( g_sAniso, vUV ).rg;
            float flBlendMin = saturate( vBlendMod.r - vBlendMod.g );
            float flBlendMax = saturate( vBlendMod.r + vBlendMod.g );
            
            flBlendFactor = smoothstep( flBlendMin, flBlendMax, flBlendFactor );

            float4 vBaseTexture2 = g_tColor2.Sample( g_sAniso, vUV );
            
            vAlbedo = lerp( vAlbedo, vBaseTexture2.rgb * g_vColorTint, flBlendFactor );
            flBaseAlpha = lerp( flBaseAlpha, vBaseTexture2.a, flBlendFactor );

            #if S_BUMPMAP
                float4 vNormalSample2 = g_tNormal2.Sample( g_sAniso, vUV );
                float3 vNormalTs2 = DecodeNormal( vNormalSample2.rgb );
                vNormalTs2 = normalize( vNormalTs2 );
                
                vNormalTs = normalize( lerp( vNormalTs, vNormalTs2, flBlendFactor ) );
                vNormalWs = TransformNormal( vNormalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
                flNormalAlpha = lerp( flNormalAlpha, vNormalSample2.a, flBlendFactor );
            #endif
        }
        #endif

        #if S_DETAIL
        {
            float2 vDetailUV = vUV * g_flDetailScale;
            float3 vDetail = g_tDetail.Sample( g_sAniso, vDetailUV ).rgb * g_vDetailTint;
            vAlbedo = ApplyDetailTexture( vAlbedo, vDetail, g_nDetailBlendMode, g_flDetailBlendFactor );
        }
        #endif

        #if S_MODE_DEPTH
            return float4( 0, 0, 0, flAlpha );
        #endif

        Material m = Material::Init( i );
        m.Albedo = vAlbedo;
        m.Normal = vNormalWs;
        m.Opacity = flAlpha;
        
        m.Roughness = 0.7;
        m.Metalness = 0.0;
        m.AmbientOcclusion = 1.0;

        float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        float3 vViewWs = normalize( g_vCameraPositionWs - vPositionWs );
        float flNdotV = saturate( dot( vNormalWs, vViewWs ) );

        #if S_ENVMAP
        {
            float flEnvMapMask = 1.0;
            
            if ( g_nBaseAlphaEnvMapMask != 0 )
            {
                flEnvMapMask = 1.0 - flBaseAlpha;
            }
            else if ( g_nNormalMapAlphaEnvMapMask != 0 )
            {
                #if S_BUMPMAP
                    flEnvMapMask = flNormalAlpha;
                #endif
            }
            else
            {
                flEnvMapMask = g_tEnvMapMask.Sample( g_sAniso, vUV ).r;
            }
        
            float flFresnel = SourceFresnel4( vNormalWs, vViewWs );
            flFresnel = g_flFresnelReflection + ( 1.0 - g_flFresnelReflection ) * flFresnel;

            float3 vReflectWs = reflect( -vViewWs, vNormalWs );

            float3 vEnvColor = g_tEnvMap.SampleLevel( g_sAniso, vReflectWs, m.Roughness * 6.0 ).rgb;

            float3 vEnvSquared = vEnvColor * vEnvColor;
            vEnvColor = lerp( vEnvColor, vEnvSquared, g_flEnvMapContrast );
    
            float flLuminance = dot( vEnvColor, float3( 0.299, 0.587, 0.114 ) );
            vEnvColor = lerp( float3( flLuminance, flLuminance, flLuminance ), vEnvColor, g_flEnvMapSaturation );

            m.Emission += vEnvColor * g_vEnvMapTint * flEnvMapMask * flFresnel;
        }
        #endif

        #if S_SELFILLUM
        {
            float flSelfIllumMask = flBaseAlpha;
            m.Emission += m.Albedo * flSelfIllumMask * g_vSelfIllumTint;
        }
        #endif

        float3 vVertexLighting = i.vVertexColor.rgb;
        float flVertexLightIntensity = dot( vVertexLighting, float3( 0.333, 0.333, 0.333 ) );
        if ( flVertexLightIntensity < 0.99 )
        {
            m.Albedo *= vVertexLighting;
        }

        return ShadingModelStandard::Shade( m );
    }
}
