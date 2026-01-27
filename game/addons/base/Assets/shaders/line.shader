HEADER
{
	Description = "Line Shader for S&box";
}

FEATURES
{
	// Why do I need this to compile?
	// I don't want this, none of the features work for this shader.
	#include "vr_common_features.fxc"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 WorldPosition : POSITION < Semantic( None ); >;
	float3 Normal : NORMAL < Semantic( None ); >;
	float3 Tangent : Tangent < Semantic( None ); >;
	float4 Color : COLOR0 < Semantic( None ); >;
	float2 TextureCoords : TEXCOORD0 < Semantic( None ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool face : SV_IsFrontFace;
	#endif
};

VS
{
	PixelInput MainVs( const VertexInput v )
	{
		PixelInput i;
		i.vPositionWs = v.WorldPosition - g_vHighPrecisionLightingOffsetWs.xyz;
		i.vPositionPs.xyzw = Position3WsToPs( v.WorldPosition);
		i.vNormalWs = v.Normal;
		i.vVertexColor = v.Color;
		i.vTextureCoords = float4( v.TextureCoords, v.TextureCoords );
		i.vTangentUWs = v.Tangent.xyz;
		i.vTangentVWs = normalize( cross( v.Normal.xyz, v.Tangent.xyz ) );

				// Bias position to reduce Shadow Acne
		#if ( S_MODE_DEPTH == 1 )
			i.vPositionPs.z =  Position3WsToPs( v.WorldPosition + g_vCameraDirWs ).z;
		#endif

		return i;
	}
}

PS
{
	#include "common/pixel.hlsl"
	#include "common/blendmode.hlsl"

	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );
	DynamicCombo( D_WIREFRAME, 0..1, Sys( ALL ) );
	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_ENABLE_LIGHTING, 0..1, Sys( ALL ) );
	
	float g_DepthFeather < Attribute( "g_DepthFeather" ); >;
	float g_FogStrength < Attribute( "g_FogStrength" ); >;

	int SamplerIndex < Attribute("SamplerIndex"); >;

	CreateInputTexture2D(TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4(1.0, 1.0, 1.0, 1.0));
	CreateInputTexture2D(TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/20", Default3(0.5, 0.5, 1.0));
	CreateInputTexture2D(TextureRoughness, Linear, 8, "", "_rough", "Material,10/30", Default(0.5));
	CreateInputTexture2D(TextureMetalness, Linear, 8, "", "_metal", "Material,10/40", Default(1.0));
	CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/50", Default(1.0));

	Texture2D g_tColor < Channel(RGBA, Box(TextureColor), Srgb ); SrgbRead( true ); OutputFormat(BC7); >;
	Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); OutputFormat(BC7); SrgbRead(false); > ;
	Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureMetalness), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); OutputFormat(BC7); SrgbRead(false); > ;

	RenderState( DepthWriteEnable, true );
	RenderState( CullMode, D_ENABLE_LIGHTING == 1 && D_OPAQUE == 0 ? DEFAULT : NONE );

	#if S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif

	// Additive
	#if D_BLEND
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA ); 
		RenderState( DstBlend, ONE );
		RenderState( DepthWriteEnable, false );
	#endif

	#if D_OPAQUE == 1
		RenderState( DepthWriteEnable, true );
		RenderState( BlendEnable, false );
		RenderState( AlphaToCoverageEnable, S_MODE_DEPTH == 0 );
	#elif S_MODE_DEPTH == 0
		RenderState( DepthWriteEnable, false );
	#endif

	#if D_WIREFRAME
		RenderState( FillMode, WIREFRAME );
	#endif

	static float4 ShadeLine( Material m )
	{
		// Do alpha calculations early
		if ( g_DepthFeather > 0 )
		{
			float3 pos = Depth::GetWorldPosition( m.ScreenPosition.xy );

			float dist = distance( pos, m.WorldPosition.xyz );
			float feather = clamp(dist / g_DepthFeather, 0.0, 1.0 );
			m.Opacity *= feather;
		}

		clip( m.Opacity - 0.0001 );

		#if S_MODE_DEPTH
			//  ShadingModel handles opaque fade (even better, with MSAA support ) and gbuffer
			return ShadingModelStandard::Shade( m);
		#endif

		#if D_ENABLE_LIGHTING
			// Can't call ShadingModelStandard::Shade directly because we need to pass in D_BLEND to DoAtmospherics
			LightingTerms_t lightingTerms = InitLightingTerms();
			CombinerInput combinerInput = ShadingModelStandard::MaterialToCombinerInput( m );

			combinerInput = CalculateDiffuseAndSpecularFromAlbedoAndMetalness( combinerInput, m.Albedo.rgb, m.Metalness );

			combinerInput.vRoughness.xy = AdjustRoughnessByGeometricNormal( combinerInput.vRoughness.xy, combinerInput.vNormalWs.xyz );
			ComputeDirectLighting( lightingTerms, combinerInput );

			CalculateIndirectLighting( lightingTerms, combinerInput );

			float3 vDiffuseAO = CalculateDiffuseAmbientOcclusion( combinerInput, lightingTerms );
			lightingTerms.vIndirectDiffuse.rgb *= vDiffuseAO.rgb;
			lightingTerms.vDiffuse.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vDiffuseAO.rgb, combinerInput.flAmbientOcclusionDirectDiffuse );
			
			float3 vSpecularAO = CalculateSpecularAmbientOcclusion( combinerInput, lightingTerms );
			lightingTerms.vIndirectSpecular.rgb *= vSpecularAO.rgb;
			lightingTerms.vSpecular.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vSpecularAO.rgb, combinerInput.flAmbientOcclusionDirectSpecular );

			float3 vDiffuse = ( ( lightingTerms.vDiffuse.rgb + lightingTerms.vIndirectDiffuse.rgb ) * combinerInput.vDiffuseColor.rgb ) + combinerInput.vEmissive.rgb;
			float3 vSpecular = lightingTerms.vSpecular.rgb + lightingTerms.vIndirectSpecular.rgb;

			float4 color = float4( vDiffuse + vSpecular, combinerInput.flOpacity );

			if( DepthNormals::WantsDepthNormals() )
				return DepthNormals::Output( m.Normal, m.Roughness, color.a );

			if( ToolsVis::WantsToolsVis() )
				return ShadingModelStandard::DoToolsVis( color, m, lightingTerms );

			// Pass in blend to support addtive blending
			color = DoAtmospherics( m.WorldPosition, m.ScreenPosition.xy, color, D_BLEND );

			return color;

		#else

			return float4( m.Albedo.rgb + m.Emission.rgb, m.Opacity );

		#endif
	}


	float4 MainPs(PixelInput i) : SV_Target0
	{
		// Negate the world normal if we are rendering the back face 
		i.vNormalWs *= ((i.face ? 1.0 : -1.0));

		float4 col = 0;
		
		SamplerState sampler = Bindless::GetSampler( NonUniformResourceIndex( SamplerIndex ) );

		float2 vUV = i.vTextureCoords.xy;
		float4 texAlbedo = g_tColor.Sample(sampler, vUV) * float4(SrgbGammaToLinear(i.vVertexColor.rgb), i.vVertexColor.a);
		float4 texNormal = g_tNormal.Sample(sampler, vUV);
		float4 texRMA = g_tRma.Sample(sampler, vUV);

		// Decode the normal map
		float3 decodedTexNormal = DecodeNormal(texNormal.xyz);
		
		Material m = Material::Init();
		
		m.Albedo = texAlbedo.rgb;
		m.Normal = TransformNormal(decodedTexNormal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs);
		m.Roughness = texRMA.r;
		m.Metalness = texRMA.g;
		m.AmbientOcclusion = texRMA.b;
		m.TintMask = texNormal.a;
		m.Opacity = texAlbedo.a;
		m.Emission = float3(0.0f, 0.0f, 0.0f);
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		
		m.WorldPosition = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		m.WorldPositionWithOffset = i.vPositionWithOffsetWs;
		m.ScreenPosition = i.vPositionSs;

		col = ShadeLine(m);

		return col;
	}
}