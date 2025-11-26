HEADER
{
	DevShader = false;
	Description = "Modern Multiblend Shader";
	Version = 1;
}

//=========================================================================================================================

MODES
{
	VrForward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

//=========================================================================================================================

FEATURES
{
    #include "common/features.hlsl"

    Feature( F_MULTIBLEND, 0..3 ( 0="1 Layers", 1="2 Layers", 2="3 Layers", 3="4 Layers" ), "Number Of Blendable Layers" );
	Feature( F_USE_TINT_MASKS_IN_VERTEX_PAINT, 0..1, "Use Tint Masks In Vertex Paint" );
}

//=========================================================================================================================

COMMON
{
	#include "common/shared.hlsl"
}


//=========================================================================================================================

struct VertexInput
{	
	float4 vColorBlendValues : TEXCOORD4 < Semantic( VertexPaintBlendParams ); >;
	float4 vColorPaintValues : TEXCOORD5 < Semantic( VertexPaintTintColor ); >;
	#include "common/vertexinput.hlsl"
};

//=========================================================================================================================

struct PixelInput
{
	float4 vBlendValues		 : TEXCOORD14;
	float4 vPaintValues		 : TEXCOORD15;
	#include "common/pixelinput.hlsl"
};

//=========================================================================================================================

VS
{
	StaticCombo( S_MULTIBLEND, F_MULTIBLEND, Sys( PC ) );
	
	#include "common/vertex.hlsl"

	BoolAttribute( VertexPaintUI2Layer, F_MULTIBLEND == 1 );
	BoolAttribute( VertexPaintUI3Layer, F_MULTIBLEND == 2 );
	BoolAttribute( VertexPaintUI4Layer, F_MULTIBLEND == 3 );
	BoolAttribute( VertexPaintUI5Layer, F_MULTIBLEND == 4 );
	BoolAttribute( VertexPaintUIPickColor, true );
	BoolAttribute( ShadowFastPath, true );

	//
	// Main
	//
	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = ProcessVertex( i );

		o.vBlendValues = i.vColorBlendValues;
        o.vPaintValues = i.vColorPaintValues;

		// Models don't have vertex paint data, let's avoid painting them black
		[flatten]
		if( o.vPaintValues.w == 0 )
			o.vPaintValues = 1.0f;

		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
	//
	// Combos
	//
	StaticCombo( S_MULTIBLEND, F_MULTIBLEND, Sys( PC ) );
    StaticCombo( S_USE_TINT_MASKS_IN_VERTEX_PAINT, F_USE_TINT_MASKS_IN_VERTEX_PAINT, Sys( PC ) );

	int ShaderVersion < Source( ShaderVersion ); >;
	//
	// Includes
	//
    #include "common/pixel.hlsl"


	//
	// Inputs
	//
		//
		// Material A
		//
		CreateInputTexture2D( TextureColorA,            Srgb,   8, "",                 "_color",  "Material A,10/10", Default3( 1.0, 1.0, 1.0 ) );
		CreateInputTexture2D( TextureNormalA,           Linear, 8, "NormalizeNormals", "_normal", "Material A,10/20", Default3( 0.5, 0.5, 1.0 ) );
		CreateInputTexture2D( TextureRoughnessA,        Linear, 8, "Inverse",          "_rough",  "Material A,10/30", Default3( 0.5, 0.5, 0.5 ) );
		CreateInputTexture2D( TextureMetalnessA,        Linear, 8, "",                 "_metal",  "Material A,10/40", Default( 1.0 ) );
		CreateInputTexture2D( TextureAmbientOcclusionA, Linear, 8, "",                 "_ao",     "Material A,10/50", Default( 1.0 ) );
		CreateInputTexture2D( TextureBlendMaskA,        Linear, 8, "",                 "_blend",  "Material A,10/60", Default( 1.0 ) );
		CreateInputTexture2D( TextureTintMaskA,         Linear, 8, "",                 "_tint",   "Material A,10/70", Default( 1.0 ) );
		float3 g_flTintColorA < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material A,10/80" ); >;
		float g_flBlendSoftnessA < Default( 0.5 ); Range( 0.1, 1.0 ); UiGroup( "Material A,10/90" ); >;

		#if S_TRIPLANAR
			float g_flTriplanarBlendA < Default( 0.5f ); Range( 0.0f, 1.0f ); UiGroup( "Material A,10/110"); >;
			float2 g_flTriplanarTileA < Default2( 1.0f, 1.0f ); Range2( 0.01f, 0.01f, 10.0f, 10.0f ); UiGroup( "Material A,10/120"); >;
		#endif

		Texture2D g_tColorA  < Channel( RGB,  Box( TextureColorA ), Srgb ); Channel( A, Box( TextureTintMaskA ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
		Texture2D g_tNormalA < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalA, TextureRoughnessA ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
		Texture2D g_tRmaA < Channel( R, Box( TextureMetalnessA ), Linear ); Channel( G, Box( TextureAmbientOcclusionA ), Linear ); Channel( B, Box( TextureBlendMaskA ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

		TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColorA );
    	TextureAttribute( RepresentativeTexture, g_tColorA );

	#if S_MULTIBLEND >= 1
		//
		// Material B
		//
		CreateInputTexture2D( TextureColorB,            Srgb,   8, "",                 "_color",  "Material B,10/10", Default3( 1.0, 1.0, 1.0 ) );
		CreateInputTexture2D( TextureNormalB,           Linear, 8, "NormalizeNormals", "_normal", "Material B,10/20", Default3( 0.5, 0.5, 1.0 ) );
		CreateInputTexture2D( TextureRoughnessB,        Linear, 8, "Inverse",          "_rough",  "Material B,10/30", Default3( 0.5, 0.5, 0.5 ) );
		CreateInputTexture2D( TextureMetalnessB,        Linear, 8, "",                 "_metal",  "Material B,10/40", Default( 1.0 ) );
		CreateInputTexture2D( TextureAmbientOcclusionB, Linear, 8, "",                 "_ao",     "Material B,10/50", Default( 1.0 ) );
		CreateInputTexture2D( TextureBlendMaskB,        Linear, 8, "",                 "_blend",  "Material B,10/60", Default( 1.0 ) );
		CreateInputTexture2D( TextureTintMaskB,         Linear, 8, "",                 "_tint",   "Material B,10/70", Default( 1.0 ) );
		float3 g_flTintColorB < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material B,10/80" ); >;
		float g_flBlendSoftnessB < Default( 0.5 ); Range( 0.1, 1.0 ); UiGroup( "Material B,10/90" ); >;
		float2 g_vTexCoordScale2 < Default2( 1.0, 1.0 ); Range2( 0.0, 0.0, 10.0, 10.0 ); UiGroup( "Material B,10/100" ); >;

		#if S_TRIPLANAR
			float g_flTriplanarBlendB < Default( 0.5f ); Range( 0.0f, 1.0f ); UiGroup( "Material B,10/110"); >;
			float2 g_flTriplanarTileB < Default2( 1.0f, 1.0f ); Range2( 0.01f, 0.01f, 10.0f, 10.0f ); UiGroup( "Material B,10/120"); >;
		#endif

		Texture2D g_tColorB  < Channel( RGB,  Box( TextureColorB ), Srgb ); Channel( A, Box( TextureTintMaskB ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
		Texture2D g_tNormalB < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalB, TextureRoughnessB ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
		Texture2D g_tRmaB < Channel( R, Box( TextureMetalnessB ), Linear ); Channel( G, Box( TextureAmbientOcclusionB ), Linear ); Channel( B, Box( TextureBlendMaskB ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;


	#if S_MULTIBLEND >= 2
		//
		// Material C
		//
		CreateInputTexture2D( TextureColorC,            Srgb,   8, "",                 "_color",  "Material C,10/10", Default3( 1.0, 1.0, 1.0 ) );
		CreateInputTexture2D( TextureNormalC,           Linear, 8, "NormalizeNormals", "_normal", "Material C,10/20", Default3( 0.5, 0.5, 1.0 ) );
		CreateInputTexture2D( TextureRoughnessC,        Linear, 8, "Inverse",          "_rough",  "Material C,10/30", Default3( 0.5, 0.5, 0.5 ) );
		CreateInputTexture2D( TextureMetalnessC,        Linear, 8, "",                 "_metal",  "Material C,10/40", Default( 1.0 ) );
		CreateInputTexture2D( TextureAmbientOcclusionC, Linear, 8, "",                 "_ao",     "Material C,10/50", Default( 1.0 ) );
		CreateInputTexture2D( TextureBlendMaskC,        Linear, 8, "",                 "_blend",  "Material C,10/60", Default( 1.0 ) );
		CreateInputTexture2D( TextureTintMaskC,         Linear, 8, "",                 "_tint",   "Material C,10/70", Default( 1.0 ) );
		float3 g_flTintColorC < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material C,10/80" ); >;
		float g_flBlendSoftnessC < Default( 0.5 ); Range( 0.1, 1.0 ); UiGroup( "Material C,10/90" ); >;
		float2 g_vTexCoordScale3 < Default2( 1.0, 1.0 ); Range2( 0.0, 0.0, 10.0, 10.0 ); UiGroup( "Material C,10/100" ); >;

		#if S_TRIPLANAR
			float g_flTriplanarBlendC < Default( 0.5f ); Range( 0.0f, 1.0f ); UiGroup( "Material C,10/110"); >;
			float2 g_flTriplanarTileC < Default2( 1.0f, 1.0f ); Range2( 0.01f, 0.01f, 10.0f, 10.0f ); UiGroup( "Material C,10/120"); >;
		#endif

		Texture2D g_tColorC  < Channel( RGB,  Box( TextureColorC ), Srgb ); Channel( A, Box( TextureTintMaskC ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
		Texture2D g_tNormalC < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalC, TextureRoughnessC ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
		Texture2D g_tRmaC < Channel( R, Box( TextureMetalnessC ), Linear ); Channel( G, Box( TextureAmbientOcclusionC ), Linear ); Channel( B, Box( TextureBlendMaskC ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;


	#if S_MULTIBLEND >= 3
		//
		// Material D
		//
		CreateInputTexture2D( TextureColorD,            Srgb,   8, "",                 "_color",  "Material D,10/10", Default3( 1.0, 1.0, 1.0 ) );
		CreateInputTexture2D( TextureNormalD,           Linear, 8, "NormalizeNormals", "_normal", "Material D,10/20", Default3( 0.5, 0.5, 1.0 ) );
		CreateInputTexture2D( TextureRoughnessD,        Linear, 8, "Inverse",          "_rough",  "Material D,10/30", Default3( 0.5, 0.5, 0.5 ) );
		CreateInputTexture2D( TextureMetalnessD,        Linear, 8, "",                 "_metal",  "Material D,10/40", Default( 1.0 ) );
		CreateInputTexture2D( TextureAmbientOcclusionD, Linear, 8, "",                 "_ao",     "Material D,10/50", Default( 1.0 ) );
		CreateInputTexture2D( TextureBlendMaskD,        Linear, 8, "",                 "_blend",  "Material D,10/60", Default( 1.0 ) );
		CreateInputTexture2D( TextureTintMaskD,         Linear, 8, "",                 "_tint",   "Material D,10/70", Default( 1.0 ) );
		float3 g_flTintColorD < UiType( Color ); Default3( 1.0, 1.0, 1.0 ); UiGroup( "Material D,10/80" ); >;
		float g_flBlendSoftnessD < Default( 0.5 ); Range( 0.1, 1.0 ); UiGroup( "Material D,10/90" ); >;
		float2 g_vTexCoordScale4 < Default2( 1.0, 1.0 ); Range2( 0.0, 0.0, 10.0, 10.0 ); UiGroup( "Material D,10/100" ); >;

		
		#if S_TRIPLANAR
			float g_flTriplanarBlendD < Default( 0.5f ); Range( 0.0f, 1.0f ); UiGroup( "Material D,10/110"); >;
			float2 g_flTriplanarTileD < Default2( 1.0f, 1.0f ); Range2( 0.01f, 0.01f, 10.0f, 10.0f ); UiGroup( "Material D,10/120"); >;
		#endif

		Texture2D g_tColorD  < Channel( RGB,  Box( TextureColorD ), Srgb ); Channel( A, Box( TextureTintMaskD ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
		Texture2D g_tNormalD < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalD, TextureRoughnessD ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
		Texture2D g_tRmaD < Channel( R, Box( TextureMetalnessD ), Linear ); Channel( G, Box( TextureAmbientOcclusionD ), Linear ); Channel( B, Box( TextureBlendMaskD ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	#endif // 3
	#endif // 2
	#endif // 1

	#define FETCH_MULTIBLEND( X, S, B ) \
			MaterialMultiblend::From( i, \
                g_tColor##X.Sample( 	g_sDefault, i.vTextureCoords.xy * S ), \
                g_tNormal##X.Sample( 	g_sDefault, i.vTextureCoords.xy * S ), \
                g_tRma##X.Sample( 		g_sDefault, i.vTextureCoords.xy * S ), \
                g_flTintColor##X, \
                B \
            )

	//-------------------------------------------------------------------------------------------------------------------------------------------
	// We assume flBlendSoftness is >=0.01 so we don't divide by 0 below
	//-------------------------------------------------------------------------------------------------------------------------------------------
	float ComputeBlendWeight( float flBlendIntensity, float flBlendSoftness, float flBlendRevealMask )
	{
		//return Gain( saturate( flBlendIntensity + 2.0 * ( flBlendRevealMask - 0.5 ) ), flBlendSoftness );
		//return step( flBlendRevealMask, flBlendIntensity );
		//return flBlendIntensity;
		//return flBlendRevealMask;

		// 8 instructions - Scale range so at 0.0 the full min/max range is <=0.0, and at 1.0 the full min/max range is >=1.0
		float flScaledInput = ( flBlendIntensity * ( 1.0 + ( flBlendSoftness * 2.0 ) ) ) - flBlendSoftness;
		float flMin = flScaledInput - flBlendSoftness;
		float flMax = flScaledInput + flBlendSoftness;
		return 1.0 - saturate( ( flBlendRevealMask - flMin ) / ( flBlendSoftness * 2.0 ) );

		// 12 instructions (Bake 4D min/max into a 2D texture?)
		//float flOldMin = saturate( flBlendIntensity - flBlendSoftness );
		//float flOldMax = saturate( flBlendIntensity + flBlendSoftness );
		//float flNewMin = saturate( 1.0 - ( 1.0 - flBlendIntensity ) / flBlendSoftness );
		//float flNewMax = saturate( flBlendIntensity / flBlendSoftness );
		//return RemapValClamped( flBlendRevealMask, flOldMin, flOldMax, flNewMax, flNewMin );
	}

	//
	// Structures
	//
	class MaterialMultiblend : Material
	{
		static Material lerp( Material a, Material b, float fBlendValue, float fBlendMaskB, float fSoftness = 0.5 )
		{
			float fBlendfactor = ComputeBlendWeight( fBlendValue, fSoftness, fBlendMaskB );
			return Material::lerp( a, b, fBlendfactor );
		}

		static Material From( PixelInput i, float4 vColor, float4 vNormalTexel, float4 vRMA, float3 vTintColor, out float blendMask )
		{
			Material p = Material::Init( i );
			p.Albedo = vColor.rgb;

			// We want to support per-instance tint color as well
			vTintColor *= i.vVertexColor.rgb;

			if ( ShaderVersion >= 1 )
			{
				p.Normal = TransformNormal( DecodeHemiOctahedronNormal( vNormalTexel.rg ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
				p.Roughness = vNormalTexel.b;
				p.Metalness = vRMA.r;
				p.AmbientOcclusion = vRMA.g;

				blendMask = vRMA.b;
			}
			else
			{
				p.Normal = TransformNormal( DecodeNormal( vNormalTexel.rgb ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
				p.Roughness = vRMA.r;
				p.Metalness = vRMA.g;
				p.AmbientOcclusion = vRMA.b;

				blendMask = vRMA.a;
			}

			p.TintMask = vColor.a;
			p.Opacity = 1.0f;
			p.Emission = float3( 0.0f, 0.0f, 0.0f );
			p.Transmission = 0;

			p.WorldTangentU = i.vTangentUWs;
			p.WorldTangentV = i.vTangentVWs;			
			
			// Do tint
			#if ( S_USE_TINT_MASKS_IN_VERTEX_PAINT )
				p.Albedo = ::lerp( p.Albedo.rgb, p.Albedo.rgb * vTintColor * i.vPaintValues.xyz, p.TintMask );
			#else
				p.Albedo = ::lerp( p.Albedo.rgb, p.Albedo.rgb * vTintColor, p.TintMask );
			#endif

			return p;
		}

		static Material From( PixelInput i )
		{
			float flBlendMaskA;
			Material materialA = FETCH_MULTIBLEND( A, 1.0f, flBlendMaskA );

			#if S_MULTIBLEND >= 1
				if( i.vBlendValues.r > 0.0f )
				{
					float flBlendMaskB;
					Material materialB = FETCH_MULTIBLEND( B, g_vTexCoordScale2, flBlendMaskB );
					materialA = lerp( materialA, materialB, i.vBlendValues.r, flBlendMaskB, g_flBlendSoftnessB ); 
				}
			#if S_MULTIBLEND >= 2
				if( i.vBlendValues.g > 0.0f )
				{
					float flBlendMaskC;
					Material materialC = FETCH_MULTIBLEND( C, g_vTexCoordScale3, flBlendMaskC );
					materialA = lerp( materialA, materialC, i.vBlendValues.g, flBlendMaskC, g_flBlendSoftnessC );
				}
			#if S_MULTIBLEND >= 3
				if( i.vBlendValues.b > 0.0f )
				{
					float flBlendMaskD;
					Material materialD = FETCH_MULTIBLEND( D, g_vTexCoordScale4, flBlendMaskD );
					materialA = lerp( materialA, materialD, i.vBlendValues.b, flBlendMaskD, g_flBlendSoftnessD );
				}
			#endif // 3
			#endif // 2
			#endif // 1

			return materialA;
		}
	};

	//
	// Main
	//
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		//
		// Set up materials
		//
		Material m = MaterialMultiblend::From( i );

		//
		// Vertex Painting
		//
		#if( S_USE_TINT_MASKS_IN_VERTEX_PAINT == 0 )
		{
			m.Albedo = m.Albedo.xyz * i.vPaintValues.xyz;
		}
		#endif			

		//
		// Write to final combiner
		//
		return ShadingModelStandard::Shade( i, m );

	}
}