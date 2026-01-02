HEADER
{
    Description = "Source Engine UnlitGeneric Shader";
    Version = 1;
    DevShader = false;
}

FEATURES
{
    #include "common/features.hlsl"
    Feature( F_TRANSLUCENT, 0..1, "Rendering" );
    Feature( F_ALPHA_TEST, 0..1, "Rendering" );
    Feature( F_DETAIL, 0..1, "Detail Texture" );
    Feature( F_ENVMAP, 0..1, "Environment Map" );
    Feature( F_VERTEX_COLOR, 0..1, "Vertex Color" );
    Feature( F_VERTEX_ALPHA, 0..1, "Vertex Alpha" );
}

MODES
{
    Forward();
    Depth( S_MODE_DEPTH );
    ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
    #define CUSTOM_MATERIAL_INPUTS
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
    StaticCombo( S_DETAIL, F_DETAIL, Sys( ALL ) );
    StaticCombo( S_ENVMAP, F_ENVMAP, Sys( ALL ) );
    StaticCombo( S_VERTEX_COLOR, F_VERTEX_COLOR, Sys( ALL ) );
    StaticCombo( S_VERTEX_ALPHA, F_VERTEX_ALPHA, Sys( ALL ) );

    #include "common/pixel.hlsl"

    // $basetexture
    CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3( 1.0, 1.0, 1.0 ) );
    Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb ); Channel( A, Box( TextureColor ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
    TextureAttribute( RepresentativeTexture, g_tColor );

    // $color - color tint (default [1 1 1])
    float3 g_vColorTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material,10/20" ); >;
    // $alpha - opacity (default 1)
    float g_flAlpha < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Material,10/30" ); >;

    #if S_DETAIL
        // $detail
        CreateInputTexture2D( TextureDetail, Srgb, 8, "", "_detail", "Detail,10/10", Default3( 0.5, 0.5, 0.5 ) );
        Texture2D g_tDetail < Channel( RGB, Box( TextureDetail ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
        
        // $detailscale - detail texture UV scale (default 4)
        float g_flDetailScale < Default( 4.0 ); Range( 0.1, 32.0 ); UiGroup( "Detail,10/20" ); >;
        // $detailblendfactor - detail blend amount (default 1)
        float g_flDetailBlendFactor < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Detail,10/30" ); >;
        // $detailblendmode - 0=mod2x, 1=additive, 2=alpha blend, 3=crossfade (default 0)
        int g_nDetailBlendMode < Default( 0 ); Range( 0, 3 ); UiGroup( "Detail,10/40" ); >;
    #endif

    #if S_ENVMAP
        // $envmap
        CreateInputTextureCube( TextureEnvMap, Srgb, 8, "", "_envmap", "Environment Map,10/10", Default3( 0.0, 0.0, 0.0 ) );
        TextureCube g_tEnvMap < Channel( RGBA, Box( TextureEnvMap ), Srgb ); OutputFormat( BC6H ); SrgbRead( true ); >;
        
        // $envmaptint - envmap color tint (default [1 1 1])
        float3 g_vEnvMapTint < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Environment Map,10/20" ); >;
        // $envmapcontrast - 0=normal, 1=color*color (default 0)
        float g_flEnvMapContrast < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/30" ); >;
        // $envmapsaturation - 0=greyscale, 1=normal (default 1)
        float g_flEnvMapSaturation < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Environment Map,10/40" ); >;
    #endif

    float3 ApplyUnlitDetail( float3 vBase, float3 vDetail, int nBlendMode, float flBlendFactor )
    {
        switch ( nBlendMode )
        {
            case 0: // Mod2x
            default:
                return vBase * lerp( float3( 1, 1, 1 ), vDetail * 2.0, flBlendFactor );
            case 1: // Additive
                return saturate( vBase + vDetail * flBlendFactor );
            case 2: // Alpha blend
            case 3: // Crossfade
                return lerp( vBase, vDetail, flBlendFactor );
        }
    }

    float4 MainPs( PixelInput i ) : SV_Target0
    {
        float4 vColor = g_tColor.Sample( g_sAniso, i.vTextureCoords.xy );

        vColor.rgb *= g_vColorTint;

        #if S_VERTEX_COLOR
            vColor.rgb *= i.vVertexColor.rgb;
        #endif

        #if S_VERTEX_ALPHA
            vColor.a *= i.vVertexColor.a;
        #endif

        vColor.a *= g_flAlpha;

        #if S_DETAIL
        {
            float2 vDetailUV = i.vTextureCoords.xy * g_flDetailScale;
            float3 vDetail = g_tDetail.Sample( g_sAniso, vDetailUV ).rgb;
            vColor.rgb = ApplyUnlitDetail( vColor.rgb, vDetail, g_nDetailBlendMode, g_flDetailBlendFactor );
        }
        #endif

        #if S_ENVMAP
        {
            float3 vViewRay = normalize( i.vPositionWithOffsetWs.xyz );
            float3 vReflect = reflect( vViewRay, i.vNormalWs );
            float3 vEnvColor = g_tEnvMap.SampleLevel( g_sAniso, vReflect, 0 ).rgb;
            
            vEnvColor = lerp( vEnvColor, vEnvColor * vEnvColor, g_flEnvMapContrast );
         
            float flLuminance = dot( vEnvColor, float3( 0.299, 0.587, 0.114 ) );
            vEnvColor = lerp( float3( flLuminance, flLuminance, flLuminance ), vEnvColor, g_flEnvMapSaturation );
            
            vColor.rgb += vEnvColor * g_vEnvMapTint;
        }
        #endif

        #if S_MODE_DEPTH
            return float4( 0, 0, 0, vColor.a );
        #endif

        if ( DepthNormals::WantsDepthNormals() )
            return DepthNormals::Output( i.vNormalWs, 1.0, vColor.a );

        if ( ToolsVis::WantsToolsVis() )
        {
            ToolsVis toolVis = ToolsVis::Init( vColor, float3(0,0,0), float3(0,0,0), vColor.rgb, float3(0,0,0), float3(0,0,0) );
            toolVis.HandleFullbright( vColor, vColor.rgb, i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs, i.vNormalWs );
            toolVis.HandleAlbedo( vColor, vColor.rgb );
            return vColor;
        }

        float3 vWorldPos = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs;
        
        #if S_ADDITIVE_BLEND
            vColor = DoAtmospherics( vWorldPos, i.vPositionSs.xy, vColor, true );
        #else
            vColor = DoAtmospherics( vWorldPos, i.vPositionSs.xy, vColor, false );
        #endif

        return vColor;
    }
}
