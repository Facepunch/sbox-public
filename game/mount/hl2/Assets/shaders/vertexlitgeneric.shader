HEADER
{
    Description = "Source Engine VertexLitGeneric Shader";
    Version = 1;
    DevShader = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_TRANSLUCENT, 0..1, "Rendering" );
    Feature( F_ALPHA_TEST, 0..1, "Rendering" );
    Feature( F_BUMPMAP, 0..1, "Normal Mapping" );
    Feature( F_PHONG, 0..1, "Specular" );
    Feature( F_SELFILLUM, 0..1, "Self Illumination" );
    Feature( F_RIMLIGHT, 0..1, "Rim Lighting" );
    Feature( F_DETAIL, 0..1, "Detail Texture" );
    Feature( F_ENVMAP, 0..1, "Environment Map" );
    Feature( F_HALFLAMBERT, 0..1, "Lighting" );
    Feature( F_LIGHTWARP, 0..1, "Light Warp" );
    Feature( F_PHONGWARP, 0..1, "Phong Warp" );
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
    StaticCombo( S_BUMPMAP, F_BUMPMAP, Sys( ALL ) );
    StaticCombo( S_PHONG, F_PHONG, Sys( ALL ) );
    StaticCombo( S_SELFILLUM, F_SELFILLUM, Sys( ALL ) );
    StaticCombo( S_RIMLIGHT, F_RIMLIGHT, Sys( ALL ) );
    StaticCombo( S_DETAIL, F_DETAIL, Sys( ALL ) );
    StaticCombo( S_ENVMAP, F_ENVMAP, Sys( ALL ) );
    StaticCombo( S_HALFLAMBERT, F_HALFLAMBERT, Sys( ALL ) );
    StaticCombo( S_LIGHTWARP, F_LIGHTWARP, Sys( ALL ) );
    StaticCombo( S_PHONGWARP, F_PHONGWARP, Sys( ALL ) );

    #include "common/pixel.hlsl"

    float SourceFresnel( float3 vNormal, float3 vEyeDir, float3 vRanges )
    {
        // Optimized piecewise fresnel: blends low->mid (0-0.5) and mid->high (0.5-1)
        // vRanges is pre-encoded as ((mid-min)*2, mid, (max-mid)*2)
        float3 vEncodedRanges = float3(
            ( vRanges.y - vRanges.x ) * 2.0,
            vRanges.y,
            ( vRanges.z - vRanges.y ) * 2.0
        );
        float f = saturate( 1.0 - dot( vNormal, vEyeDir ) );
        f = f * f - 0.5;
        return vEncodedRanges.y + ( f >= 0.0 ? vEncodedRanges.z : vEncodedRanges.x ) * f;
    }

    float SourceFresnel4( float3 vNormal, float3 vEyeDir )
    {
        // Traditional fresnel using 4th power (square twice)
        float fresnel = saturate( 1.0 - dot( vNormal, vEyeDir ) );
        fresnel = fresnel * fresnel;
        return fresnel * fresnel;
    }

    float HalfLambert( float flNdotL )
    {
        float flHalfLambert = flNdotL * 0.5 + 0.5;
        return flHalfLambert * flHalfLambert;
    }

    float Lambert( float flNdotL )
    {
        return saturate( flNdotL );
    }

    #if S_LIGHTWARP
        CreateInputTexture2D( TextureLightWarp, Srgb, 8, "", "_lightwarp", "Light Warp,10/10", Default3( 1.0, 1.0, 1.0 ) );
        Texture2D g_tLightWarp < Channel( RGB, Box( TextureLightWarp ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); AddressU( CLAMP ); AddressV( CLAMP ); >;
    #endif

    #if S_PHONGWARP
        CreateInputTexture2D( TexturePhongWarp, Srgb, 8, "", "_phongwarp", "Phong Warp,10/10", Default3( 1.0, 1.0, 1.0 ) );
        Texture2D g_tPhongWarp < Channel( RGB, Box( TexturePhongWarp ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); AddressU( CLAMP ); AddressV( CLAMP ); >;
    #endif

    void SourceSpecularAndRimTerms( 
        float3 vWorldNormal, float3 vLightDir, float flSpecularExponent,
        float3 vEyeDir, float3 vLightColor, float flFresnel,
        bool bDoRimLighting, float flRimExponent,
        bool bDoSpecularWarp,
        out float3 specularLighting, out float3 rimLighting )
    {
        rimLighting = float3( 0.0, 0.0, 0.0 );
        
        float3 vReflect = 2.0 * vWorldNormal * dot( vWorldNormal, vEyeDir ) - vEyeDir;
        float flLdotR = saturate( dot( vReflect, vLightDir ) );
        specularLighting = pow( flLdotR, flSpecularExponent );

        if ( bDoSpecularWarp )
        {
            #if S_PHONGWARP
                specularLighting *= g_tPhongWarp.Sample( g_sAniso, float2( specularLighting.x, flFresnel ) ).rgb;
            #endif
        }
        
        float flNdotL = saturate( dot( vWorldNormal, vLightDir ) );
        specularLighting *= flNdotL;
        specularLighting *= vLightColor;
        
        if ( bDoRimLighting )
        {
            rimLighting = pow( flLdotR, flRimExponent ) * flNdotL * vLightColor;
        }
    }

    class ShadingModelSource
    {
        static float4 Shade( 
            PixelInput i, float3 vAlbedo, float3 vNormalWs, float flOpacity, float3 vEmission,
            bool bHalfLambert, bool bPhong, float flPhongExponent, float flPhongBoost,
            float3 vPhongTint, float3 vPhongFresnelRanges, float flPhongAlbedoTint, float flPhongMask,
            bool bRimLight, float flRimExponent, float flRimBoost, float flRimMask,
            bool bLightWarp, bool bPhongWarp, float2 vUV )
        {
            float4 vColor = float4( 0, 0, 0, flOpacity );
            
            float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
            float3 vViewWs = normalize( g_vCameraPositionWs - vPositionWs );

            float flFresnelRanges = 1.0;
            float flRimFresnel = SourceFresnel4( vNormalWs, vViewWs );
            
            if ( bPhong )
                flFresnelRanges = SourceFresnel( vNormalWs, vViewWs, vPhongFresnelRanges );
            
            float3 vDiffuse = float3( 0, 0, 0 );
            float3 vSpecular = float3( 0, 0, 0 );
            float3 vRimLighting = float3( 0, 0, 0 );

            uint nLightCount = Light::Count( i.vPositionSs.xy );
            for ( uint idx = 0; idx < nLightCount; idx++ )
            {
                Light light = Light::From( i.vPositionSs.xy, vPositionWs, idx );
                float3 vLightDir = light.Direction;
                float3 vLightColor = light.Color * light.Attenuation * light.Visibility;
                float flNdotL = dot( vNormalWs, vLightDir );

                float3 vDiffuseTerm;
                if ( bLightWarp )
                {
                    float flWarpCoord = bHalfLambert ? saturate( flNdotL * 0.5 + 0.5 ) : saturate( flNdotL );
                    #if S_LIGHTWARP
                        vDiffuseTerm = 2.0 * g_tLightWarp.Sample( g_sAniso, float2( flWarpCoord, 0.5 ) ).rgb;
                    #else
                        vDiffuseTerm = float3( flWarpCoord, flWarpCoord, flWarpCoord );
                    #endif
                }
                else
                {
                    float flDiffuseScalar = bHalfLambert ? HalfLambert( flNdotL ) : Lambert( flNdotL );
                    vDiffuseTerm = float3( flDiffuseScalar, flDiffuseScalar, flDiffuseScalar );
                }
                vDiffuse += vAlbedo * vLightColor * vDiffuseTerm;
                
                if ( bPhong || bRimLight )
                {
                    float3 localSpec = float3( 0, 0, 0 );
                    float3 localRim = float3( 0, 0, 0 );

                    SourceSpecularAndRimTerms( vNormalWs, vLightDir, flPhongExponent, vViewWs, vLightColor,
                        flFresnelRanges, bRimLight, flRimExponent, bPhongWarp, localSpec, localRim );
                    
                    vSpecular += localSpec;
                    vRimLighting += localRim;
                }
            }

            if ( bPhong )
            {
                if ( bPhongWarp )
                {
                    vSpecular *= flPhongMask * flPhongBoost;
                }
                else
                {
                    float flFinalMask = flPhongMask * flFresnelRanges;
                    vSpecular *= flFinalMask * flPhongBoost;
                }
            }

            if ( bRimLight )
            {
                float flRimMultiply = flRimMask * flRimFresnel;
                vRimLighting *= flRimMultiply;
            }

            float3 vAmbientNormal = normalize( lerp( vNormalWs, vViewWs, 0.3 ) );
            float3 vAmbient = AmbientLight::From( vPositionWs, i.vPositionSs.xy, vAmbientNormal );
            float flAmbientScale = bHalfLambert ? 1.15 : 1.0;
            float3 vIndirectDiffuse = vAlbedo * vAmbient * flAmbientScale;

            float3 vAmbientRim = float3( 0, 0, 0 );
            if ( bRimLight )
            {
                float flRimMultiply = flRimMask * flRimFresnel;
                float3 vAmbientRimColor = AmbientLight::From( vPositionWs, i.vPositionSs.xy, vViewWs );
                vAmbientRim = vAmbientRimColor * flRimBoost * saturate( flRimMultiply * vNormalWs.z );
            }

            vRimLighting = max( vRimLighting, vAmbientRim );
            vSpecular = max( vSpecular, vRimLighting );

            float3 vSpecTint = lerp( vPhongTint, vPhongTint * vAlbedo, flPhongAlbedoTint );
            vSpecular *= vSpecTint;

            vColor.rgb = vDiffuse + vIndirectDiffuse + vSpecular + vEmission;
            
            if ( DepthNormals::WantsDepthNormals() )
            {
                float flRoughness = bPhong ? sqrt( 2.0 / ( flPhongExponent + 2.0 ) ) : 1.0;
                return DepthNormals::Output( vNormalWs, flRoughness, flOpacity );
            }
            
            if ( ToolsVis::WantsToolsVis() )
            {
                ToolsVis toolVis = ToolsVis::Init( vColor, vDiffuse, vSpecular, vIndirectDiffuse, float3(0,0,0), float3(0,0,0) );
                toolVis.HandleFullbright( vColor, vAlbedo, vPositionWs, vNormalWs );
                toolVis.HandleDiffuseLighting( vColor );
                toolVis.HandleSpecularLighting( vColor );
                toolVis.HandleAlbedo( vColor, vAlbedo );
                toolVis.HandleNormalWs( vColor, vNormalWs );
                return vColor;
            }
            
            vColor = DoAtmospherics( vPositionWs, i.vPositionSs.xy, vColor );
            return vColor;
        }
    };

    // $basetexture
    CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); Channel( A, Box( TextureColor ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
    TextureAttribute( RepresentativeTexture, g_tColor );

    // $color - color tint (default [1 1 1])
    float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material,10/30" ); >;
    // $alpha - opacity (default 1)
    float g_flAlpha < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Material,10/40" ); >;

    #if S_BUMPMAP
        // $bumpmap
        CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal Map,10/10", Default3( 0.5, 0.5, 1.0 ) );
        Texture2D g_tNormal < Channel( RGB, Box( TextureNormal ), Linear ); Channel( A, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
    #endif

    #if S_PHONG
        // $phongexponenttexture - R=exponent, G=albedotint, B=unused, A=rimmask
        CreateInputTexture2D( TexturePhongExponent, Linear, 8, "", "_exponent", "Phong,10/10", Default4( 0.5, 0.0, 0.0, 1.0 ) );
        Texture2D g_tPhongExponent < Channel( RGBA, Box( TexturePhongExponent ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

        // $phongexponent - specular exponent (default 5, use -1 to read from texture)
        float g_flPhongExponent < Default( 20.0 ); Range( -1.0, 150.0 ); UiGroup( "Phong,10/20" ); >;
        // $phongboost - specular boost multiplier (default 1)
        float g_flPhongBoost < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Phong,10/30" ); >;
        // $phongtint - specular color tint (default [1 1 1])
        float3 g_vPhongTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Phong,10/40" ); >;
        // $phongfresnelranges - fresnel remap [min center max] (default [0 0.5 1])
        float3 g_vPhongFresnelRanges < Default3( 0.0, 0.5, 1.0 ); UiGroup( "Phong,10/50" ); >;
        // $phongalbedotint - tint specular by albedo (default 0)
        float g_flPhongAlbedoTint < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Phong,10/60" ); >;
        // $basemapalphaphongmask - use base texture alpha as phong mask (default 0)
        int g_nBaseMapAlphaPhongMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Phong,10/70" ); >;
        // $invertphongmask - invert the phong mask (default 0)
        int g_nInvertPhongMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Phong,10/80" ); >;
    #endif

    #if S_SELFILLUM
        // $selfillummask - separate self-illumination mask texture
        CreateInputTexture2D( TextureSelfIllumMask, Linear, 8, "", "_selfillum", "Self Illumination,10/10", Default( 0.0 ) );
        Texture2D g_tSelfIllumMask < Channel( R, Box( TextureSelfIllumMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
        
        // $selfillumtint - self-illumination color tint (default [1 1 1])
        float3 g_vSelfIllumTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Self Illumination,10/20" ); >;
        // 0 = use base alpha, 1 = use mask texture
        float g_flSelfIllumMaskControl < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Self Illumination,10/30" ); >;
    #endif

    #if S_RIMLIGHT
        // $rimlightexponent - rim light exponent (default 4)
        float g_flRimLightExponent < Default( 4.0 ); Range( 0.1, 20.0 ); UiGroup( "Rim Light,10/10" ); >;
        // $rimlightboost - rim light boost (default 1)
        float g_flRimLightBoost < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Rim Light,10/20" ); >;
        // $rimmask - use exponent texture alpha as rim mask (default 0)
        int g_nRimMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Rim Light,10/30" ); >;
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
        // $envmapfresnel - fresnel for envmap (default 0)
        float g_flEnvMapFresnel < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/50" ); >;
        // $basealphaenvmapmask - use base alpha as envmap mask (default 0)
        int g_nBaseAlphaEnvMapMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Environment Map,10/60" ); >; 
        // $normalmapalphaenvmapmask - use normalmap alpha as envmap mask (default 0)
        int g_nNormalMapAlphaEnvMapMask < Default( 0 ); Range( 0, 1 ); UiGroup( "Environment Map,10/70" ); >;
    #endif

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

        float4 vBaseTexture = g_tColor.Sample( g_sAniso, vUV );
        float3 vAlbedo = vBaseTexture.rgb * g_vColorTint;
        float flBaseAlpha = vBaseTexture.a;
        float flAlpha = flBaseAlpha * g_flAlpha;

        #if S_ALPHA_TEST
            if ( flAlpha < g_flAlphaTestReference )
                discard;
        #endif

        float3 vNormalTs = float3( 0.0, 0.0, 1.0 );
        float flNormalAlpha = 1.0;
        #if S_BUMPMAP
            float4 vNormalSample = g_tNormal.Sample( g_sAniso, vUV );
            vNormalTs = DecodeNormal( vNormalSample.rgb );
            vNormalTs = normalize( vNormalTs );
            flNormalAlpha = vNormalSample.a;
        #endif
        
        float3 vNormalWs = TransformNormal( vNormalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

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

        float3 vPositionWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        float3 vViewWs = normalize( g_vCameraPositionWs - vPositionWs );
        float flNdotV = saturate( dot( vNormalWs, vViewWs ) );

        float3 vEmission = float3( 0, 0, 0 );

        float flPhongMask = 1.0;
        float flPhongExponent = 20.0;
        float flPhongBoost = 1.0;
        float3 vPhongTint = float3( 1, 1, 1 );
        float3 vPhongFresnelRanges = float3( 0, 0.5, 1 );
        float flPhongAlbedoTint = 0.0;
        
        #if S_PHONG
        {
            float4 vExpMapSample = float4( 1.0, 1.0, 0.0, 1.0 );
            bool bNeedExpTexture = ( g_flPhongExponent < 0.0 );
            
            if ( bNeedExpTexture )
            {
                vExpMapSample = g_tPhongExponent.Sample( g_sAniso, vUV );
            }

            if ( g_nBaseMapAlphaPhongMask != 0 )
            {
                flPhongMask = flBaseAlpha;
            }
            else
            {
                #if S_BUMPMAP
                    flPhongMask = flNormalAlpha;
                #else
                    flPhongMask = 1.0;
                #endif
            }
            
            if ( g_nInvertPhongMask != 0 )
                flPhongMask = 1.0 - flPhongMask;

            if ( g_flPhongExponent >= 0.0 )
            {
                flPhongExponent = g_flPhongExponent;
            }
            else
            {
                flPhongExponent = 1.0 + 149.0 * vExpMapSample.r;
            }
            
            flPhongBoost = g_flPhongBoost;
            vPhongTint = g_vPhongTint;
            vPhongFresnelRanges = g_vPhongFresnelRanges;
            flPhongAlbedoTint = g_flPhongAlbedoTint * ( bNeedExpTexture ? vExpMapSample.g : 1.0 );
        }
        #endif

        float flRimMask = 1.0;
        float flRimExponent = 4.0;
        float flRimBoost = 1.0;
        
        #if S_RIMLIGHT
        {
            flRimExponent = g_flRimLightExponent;
            flRimBoost = g_flRimLightBoost;

            #if S_PHONG
                if ( g_nRimMask != 0 )
                {
                    flRimMask = g_tPhongExponent.Sample( g_sAniso, vUV ).a;
                }
            #endif
        }
        #endif

        #if S_ENVMAP
        {
            float flEnvMapMask = 1.0;
            
            if ( g_nBaseAlphaEnvMapMask != 0 )
            {
                flEnvMapMask = flBaseAlpha;
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
            
            float flEnvFresnel = 1.0;
            if ( g_flEnvMapFresnel > 0.0 )
            {
                flEnvFresnel = SourceFresnel4( vNormalWs, vViewWs );
                flEnvFresnel = lerp( 1.0, flEnvFresnel, g_flEnvMapFresnel );
            }
            
            float3 vReflectWs = reflect( -vViewWs, vNormalWs );

            float flRoughness = 0.5;
            #if S_PHONG
                flRoughness = sqrt( 2.0 / ( flPhongExponent + 2.0 ) );
            #endif
            
            float3 vEnvColor = g_tEnvMap.SampleLevel( g_sAniso, vReflectWs, flRoughness * 6.0 ).rgb;

            vEnvColor = lerp( vEnvColor, vEnvColor * vEnvColor, g_flEnvMapContrast );
            
            float flLuminance = dot( vEnvColor, float3( 0.299, 0.587, 0.114 ) );
            vEnvColor = lerp( float3( flLuminance, flLuminance, flLuminance ), vEnvColor, g_flEnvMapSaturation );

            vEmission += vEnvColor * g_vEnvMapTint * flEnvMapMask * flEnvFresnel;
        }
        #endif

        #if S_SELFILLUM
        {
            float flSelfIllumMask = flBaseAlpha;
            if ( g_flSelfIllumMaskControl > 0.0 )
            {
                flSelfIllumMask = g_tSelfIllumMask.Sample( g_sAniso, vUV ).r;
            }

            vEmission += vAlbedo * flSelfIllumMask * g_vSelfIllumTint;
        }
        #endif

        return ShadingModelSource::Shade(
            i,
            vAlbedo,
            vNormalWs,
            flAlpha,
            vEmission,
            #if S_HALFLAMBERT || S_PHONG
                true,
            #else
                false,
            #endif
            #if S_PHONG
                true,
            #else
                false,
            #endif
            flPhongExponent,
            flPhongBoost,
            vPhongTint,
            vPhongFresnelRanges,
            flPhongAlbedoTint,
            flPhongMask,
            #if S_RIMLIGHT && S_PHONG
                true,
            #else
                false,
            #endif
            flRimExponent,
            flRimBoost,
            flRimMask,
            #if S_LIGHTWARP
                true,
            #else
                false,
            #endif
            #if S_PHONGWARP && S_PHONG
                true,
            #else
                false,
            #endif
            vUV
        );
    }
}
