FEATURES
{
	Feature( F_NEW_TEXTURE_PACKING, 0..1 );
	// New materials use the improved 3-texture packing; materials authored before this
	// upgrade never stored the feature, so they keep the old 6-texture path (value 0).
	FeatureUpgrade( F_NEW_TEXTURE_PACKING, 1 );
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth(); 
}

COMMON
{
	#ifndef S_ALPHA_TEST
		#define S_ALPHA_TEST 1
	#endif

	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vColor : COLOR0;
};

VS
{
	#include "common/vertex.hlsl"
	#include "procedural.hlsl"

	float g_flWindFreq 	< Default( 0.36 ); Range( 0, 100 ); UiGroup( "Wind Displacement, 80/10" ); >;
	float g_flWindNoise < Default( 0.36 ); Range( 0, 10 ); UiGroup( "Wind Displacement, 80/10" );  >;
	float g_flWind 		< Default( 0.36 ); Range( 0, 100 ); UiGroup( "Wind Displacement, 80/30" ); >;

	// Check this if vertex colors on your mesh were exported in sRGB color space. By default this isn't enabled.
	// Keep in mind that alpha channel does not get converted, this affects only RGB channels.
	bool g_bSrgbToLinearColorSpace < UiType( CheckBox); Default( 0 ); UiGroup( "Vertex Colors, 70/10" ); >;
	
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );

		i.vColor = select( g_bSrgbToLinearColorSpace, float4( SrgbGammaToLinear( v.vColor.rgb ), v.vColor.a ), v.vColor );

		float2 vNoiseUV = i.vTextureCoords.xy * g_flWindFreq + g_flTime * 0.025 * g_flWindNoise;
		float flNoise = g_tBlueNoise.SampleLevel( g_sBilinearWrap, vNoiseUV, 0 ).r;

		float flWindDisplacement = i.vColor.r * flNoise * g_flWind;

		i.vPositionWs += flWindDisplacement;
		i.vPositionPs = Position3WsToPs( i.vPositionWs );
		
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	StaticCombo( S_NEW_TEXTURE_PACKING, F_NEW_TEXTURE_PACKING, Sys( ALL ) );

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
	
	//
	// This is split between legacy and new packing methods, by default it'll be old one so we don't break
	// old materials that rely on this. However I highly encourage you to enable new packing, it's more sane
	//
	#if !S_NEW_TEXTURE_PACKING 
		CreateInputTexture2D( FurNoise, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
		CreateInputTexture2D( BaseColor, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
		CreateInputTexture2D( Normal, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
		CreateInputTexture2D( Roughness, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
		CreateInputTexture2D( Metalness, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
		CreateInputTexture2D( AO, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );

		Texture2D g_tFurNoise < Channel( RGBA, Box( FurNoise ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
		Texture2D g_tBaseColor < Channel( RGBA, Box( BaseColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
		Texture2D g_tNormal < Channel( RGBA, Box( Normal ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
		Texture2D g_tRoughness < Channel( RGBA, Box( Roughness ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
		Texture2D g_tMetalness < Channel( RGBA, Box( Metalness ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
		Texture2D g_tAO < Channel( RGBA, Box( AO ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	#else
		CreateInputTexture2D( BaseColor, Srgb, 8, "None", "_color", "Material, 10/10", Default( 1 ) );
		CreateInputTexture2D( Normal, Linear, 8, "NormalizeNormals", "_normal", "Material, 10/20", Default3( 0.5, 0.5, 1.0 ) );
		CreateInputTexture2D( Roughness, Linear, 8, "None", "_rough", "Material, 10/30", Default( 0.5 ) );
		CreateInputTexture2D( Metalness, Linear, 8, "None", "_metal", "Material, 10/40", Default( 0 ) );
		CreateInputTexture2D( AO, Linear, 8, "None", "_ao", "Material, 10/50", Default( 1 ) );
		CreateInputTexture2D( FurNoise, Linear, 8, "None", "_fur", "Fur, 20/10", Default( 0.5 ) );

		Texture2D g_tBaseColor < Channel( RGB, Box( BaseColor ), Srgb ); Channel( A, PreserveCoverage( FurNoise ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >; 
		Texture2D g_tNormal < Channel( RGB, Box( Normal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >; 
		Texture2D g_tRMA < 
			Channel( R, Box( Roughness ), Linear );
			Channel( G, Box( Metalness ), Linear ); 
			Channel( B, Box( AO ), Linear );
			OutputFormat( DXT5 ); 
			SrgbRead( false ); 
		>;
	#endif
	
	float g_flAOAmount 		< UiGroup( "Material, 10/51" ); Default( 0 ); Range( 0, 1 ); >;
	float g_flNoiseAOAmount < UiGroup( "Material, 10/52" ); Default( 0 ); Range( 0, 1 ); >;

	float g_flNoiseTiling 			< UiGroup( "Fur, 20/20" ); Default( 1 ); Range( 0, 64 ); >;
	float g_flNoiseAlbedoMultiply 	< UiGroup( "Fur, 20/21" ); Default( 0 ); Range( 0, 1 ); >;
	float g_flMinClipFudge 			< UiGroup( "Fur, 20/22" ); Default( 0.01 ); Range( 0, 1 ); >;

	float3 g_vRimColour			< UiType( Color ); UiGroup( "Rim Lighting, 30/10" ); Default3( 0.25, 0.25, 0.25 ); >;
	float g_flRimPower 			< UiGroup( "Rim Lighting, 30/20" ); Default( 0.84 ); Range( 0, 10 ); >;
	float g_flRimFudge 			< UiGroup( "Rim Lighting, 30/30" ); Default( 0.01 ); Range( 0.01, 1 ); >;
	bool g_bUseGeometryNormals 	< UiType( CheckBox ); UiGroup( "Rim Lighting, 30/40" ); Default( 1 ); >;
	
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		// Some initials
		Material m = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		float2 vScaledUV = i.vTextureCoords.xy * g_flNoiseTiling;

		// 
		// Sample textures
		//
		#if !S_NEW_TEXTURE_PACKING
			float flFur = g_tFurNoise.Sample( g_sAniso, vScaledUV ).r; 
			float3 vAlbedo = g_tBaseColor.Sample( g_sAniso, i.vTextureCoords.xy ).rgb; 
			float3 vNormal = g_tNormal.Sample( g_sAniso, i.vTextureCoords.xy ).rgb; 
			float3 vRMA; 
			{
				vRMA.r = g_tRoughness.Sample( g_sAniso, i.vTextureCoords.xy ).r;
				vRMA.g = g_tMetalness.Sample( g_sAniso, i.vTextureCoords.xy ).r;
				vRMA.b = g_tAO.Sample( g_sAniso, i.vTextureCoords.xy ).r;
			}
		#else
			float flFur = g_tBaseColor.Sample( g_sAniso, vScaledUV ).a;
			float3 vAlbedo = g_tBaseColor.Sample( g_sAniso, i.vTextureCoords.xy ).rgb;
			float3 vNormal = g_tNormal.Sample( g_sAniso, i.vTextureCoords.xy ).rgb; 
			float3 vRMA = g_tRMA.Sample( g_sAniso, i.vTextureCoords.xy ).rgb; 		
		#endif

		// Ambient occlusion contributors 
		vRMA.b = lerp( 1, vRMA.b, g_flAOAmount );
		vRMA.b = lerp( vRMA.b, vRMA.b * flFur, g_flNoiseAOAmount );	

		// Process normals
		vNormal = DecodeNormal( vNormal );
		vNormal = TransformNormal( vNormal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs ); 

		// Fresnel
		float3 vFresnelNormalSrc = select( g_bUseGeometryNormals, i.vNormalWs, vNormal );
		float flFresnel = pow( 1.0f - dot( vFresnelNormalSrc, CalculatePositionToCameraDirWs( m.WorldPosition ) ), g_flRimPower );
		float3 vRimColour = g_vRimColour * saturate( flFresnel / g_flRimFudge );

		// Albedo AO multiply 
		vAlbedo = lerp( vAlbedo, vAlbedo * flFur, g_flNoiseAlbedoMultiply );
		vAlbedo += vRimColour;

		// Finish this by setting up material slots
		m.Albedo = vAlbedo;
		m.Normal = vNormal;
		m.Roughness = vRMA.r;
		m.Metalness = vRMA.g;
		m.AmbientOcclusion = vRMA.b;
		
		// Fur alpha clipping
		float flClipFudge = flFur * ( 1 - g_flMinClipFudge ) + g_flMinClipFudge;
		flClipFudge = flClipFudge - i.vColor.r + 0.5;
		m.Opacity = saturate( flClipFudge );
		
		// for some toolvis shit
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
        m.TextureCoords = i.vTextureCoords.xy;
		
		return ShadingModelStandard::Shade( i, m );
	}
}
