HEADER
{
    Description = "Depth of Field";
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
    float3 vPositionOs : POSITION  < Semantic( PosXyz ); >;
    float2 vTexCoord   : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
    float2 vTexCoord        : TEXCOORD0;
    float4 vPositionPs		: SV_Position;
};
 
VS
{
    float FocusPlane < Attribute( "FocusPlane" ); Default(1.0f); >;
    
    float GetProjected( float linearDepth )
    {
        float a = g_flFarPlane / (g_flFarPlane - g_flNearPlane);
        float b = g_flFarPlane * g_flNearPlane / (g_flNearPlane - g_flFarPlane);
        return b / linearDepth + a;
    }

    PixelInput MainVs( VertexInput i )
    {
        PixelInput o;
        o.vPositionPs = float4(i.vPositionOs.xyz, 1.0f);

        // Position our far plane at the focus plane
        o.vPositionPs.z = 1.0 - GetProjected( FocusPlane );

        o.vTexCoord = i.vTexCoord;
        return o;
    }
}

PS
{
    #include "common/classes/Depth.hlsl"

    #define DOF_PASS_COMBINE_BACK 0
    #define DOF_PASS_COMBINE_FRONT 1

    DynamicCombo( D_DOF_TYPE, 0..1, Sys( PC ) );
    DynamicCombo( D_DEBUG_COC, 0..1, Sys( PC ) );

    // --------------------------------------------------------------------------------------------------------------------------------------------------------

    RenderState( DepthWriteEnable,  false );
    RenderState( DepthEnable,       D_DOF_TYPE == 0 && D_DEBUG_COC == 0 ? true : false );

    RenderState( DepthFunc, D_DOF_TYPE == 0 ? GREATER_EQUAL : LESS_EQUAL );

    RenderState( BlendEnable, true );
    RenderState( SrcBlend, SRC_ALPHA );
    RenderState( DstBlend, INV_SRC_ALPHA );

    // --------------------------------------------------------------------------------------------------------------------------------------------------------

    Texture2D Color              < Attribute("Final"); >;
	SamplerState BilinearClamp   < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
    
    // --------------------------------------------------------------------------------------------------------------------------------------------------------

    float4 MainPs( PixelInput i ) : SV_Target0
    {       
        int2 dimensions;
        int levels;
        Color.GetDimensions( 0, dimensions.x, dimensions.y, levels );

        float4 vColor = Color.Sample( BilinearClamp, i.vTexCoord );

        #if D_DEBUG_COC
            // Grayscale CoC, the front layer only covers pixels where it has any CoC so the back layer stays visible
            return float4( saturate( vColor.aaa ), D_DOF_TYPE == DOF_PASS_COMBINE_FRONT && vColor.a <= 0.0f ? 0.0f : 1.0f );
        #endif

        vColor.a += fwidth( vColor.a); // Correct with neighbours

        float flBias = D_DOF_TYPE == 0 ? 0.001f : 0.001f;

        vColor.a = RemapValClamped( vColor.a, 0.0f, flBias, 0.0f, 1.0f ); // Smooth fade the alpha from CoC
        
        return vColor;
    }
}
