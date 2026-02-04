//=========================================================================================================================
// Optional
//=========================================================================================================================
HEADER
{
	Description = "Foliage shader for s&box";
    DevShader = true;
    DebugInfo = false;
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
FEATURES
{
    #include "common/features.hlsl"
    Feature( F_ALPHA_TEST, 0..1, "Rendering" );
    Feature( F_FOLIAGE_ANIMATION, 0..1( 0 = "None", 1 = "Vertex Color Based" ), "Foliage Animation" );
    Feature( F_TRANSMISSIVE, 0..1, "Rendering" );
    Feature( F_GRAZING_FADE, 0..1, "Rendering" );    
}

//=========================================================================================================================
// Optional
//=========================================================================================================================
MODES
{
    Forward();													    // Indicates this shader will be used for main rendering
    Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" ); 	// Shows how expensive drawing is in debug view
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

    float4 vColor				: COLOR0 < Semantic( Color ); >;
};

//=========================================================================================================================

struct PixelInput
{
	#include "common/pixelinput.hlsl"

    float4 vColor				: COLOR0;
};

//=========================================================================================================================

VS
{
	#include "common/vertex.hlsl"
	#include "common/trunk_bending.hlsl"

    StaticCombo( S_FOLIAGE_ANIMATION, F_FOLIAGE_ANIMATION, Sys( ALL ) );

    // Vertex Color
    #if S_FOLIAGE_ANIMATION == 1

    float g_flEdgeFrequency < Default( 1.0 ); Range( 0.1, 5.0 ); UiGroup( "Foliage Animation,10/Detail" ); >;
    float g_flEdgeAmplitude < Default( 0.1 ); Range( 0.0, 1.0 ); UiGroup( "Foliage Animation,10/Detail" ); >;
    float g_flBranchFrequency < Default( 0.5 ); Range( 0.1, 5.0 ); UiGroup( "Foliage Animation,10/Detail" ); >;
    float g_flBranchAmplitude < Default( 0.1 ); Range( 0.0, 1.0 ); UiGroup( "Foliage Animation,10/Detail" ); >;

    // Trunk bending
    float g_flSwayStrength < Default( 1.0 ); Range( 0.0, 25.0 ); UiGroup( "Foliage Animation,20/Trunk" ); >;
    float g_flSwaySpeed < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Foliage Animation,20/Trunk" ); >;

    float4 SmoothCurve( float4 x )
    {
        return x * x * ( 3.0 - 2.0 * x );
    }

    float4 TriangleWave( float4 x )
    {
        return abs( frac( x + 0.5 ) * 2.0 - 1.0 );
    }

    float4 SmoothTriangleWave( float4 x )
    {
        return SmoothCurve( TriangleWave( x ) );
    }

    // High-frequency displacement used in Unity's TerrainEngine; based on "Vegetation Procedural Animation and Shading in Crysis"
    // http://developer.nvidia.com/gpugems/GPUGems3/gpugems3_ch16.html
    void FoliageDetailBending( inout float3 vPositionOs, float3 vNormalOs, float3 vVertexColor, float3x4 matObjectToWorld, float3 vWind )
    {
        const float4 vFoliageFreqs = float4( 1.975, 0.793, 0.375, 0.193 );

        // Attenuation and phase offset is encoded in the vertex color
        const float flEdgeAtten = vVertexColor.r;
        const float flBranchAtten = vVertexColor.g;
        const float flDetailPhase = vVertexColor.b;

        // Material defined frequency and amplitude
        const float flEdgeAmp = g_flEdgeAmplitude;
        const float flBranchAmp = g_flBranchAmplitude;

        // Phases
        float flObjPhase = dot( mul( matObjectToWorld, float4( 0, 0, 0, 1 ) ), 1 );
        float flBranchPhase = flDetailPhase + flObjPhase;
        float flVtxPhase = dot( vPositionOs.xyz, flDetailPhase + flBranchPhase );

        const float maxPhase = 50000.0f;

        float2 vTime = g_flTime * float2( g_flEdgeFrequency, g_flBranchFrequency );
        float2 vPhaseOffset = fmod( float2( flVtxPhase, flBranchPhase ), maxPhase );
        float2 vWavesIn = vTime + vPhaseOffset;

        float4 vWaves = frac( vWavesIn.xxyy * vFoliageFreqs ) * 2.0 - 1.0;
        vWaves = SmoothTriangleWave( vWaves );
        float2 vWavesSum = vWaves.xz + vWaves.yw;

        float flWindIntensity = saturate( length( vWind ) * 0.2 );

        float flBranchWindBend = 1.0f - abs( dot( normalize( vWind.xyz + 0.001 ), normalize( float3( vPositionOs.xy, 0.0f ) + 0.001 ) ) );
        flBranchWindBend *= flBranchWindBend;

        vPositionOs.xyz += vWavesSum.x * flEdgeAtten * flEdgeAmp * flWindIntensity * vNormalOs.xyz;
        vPositionOs.xyz += vWavesSum.y * flBranchAtten * flBranchAmp * flWindIntensity * float3( 0.0f, 0.0f, 1.0f );
        vPositionOs.xyz += vWavesSum.y * flBranchAtten * flBranchAmp * flBranchWindBend * flWindIntensity * vWind.xyz;
    }
#endif
	//
	// Main
	//
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );

        o.vColor = i.vColor;

        float3 vNormalOs;
        float4 vTangentUOs_flTangentVSign;

        VS_DecodeObjectSpaceNormalAndTangent( i, vNormalOs, vTangentUOs_flTangentVSign );

        float3 vPositionOs = i.vPositionOs.xyz;

        float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );

#if ( S_FOLIAGE_ANIMATION == 1 )
        float3 vWind = g_vWindDirection.xyz * g_vWindStrengthFreqMulHighStrength.x;

        // trunk bending
        ApplyTrunkBending( vPositionOs, g_flSwayStrength, g_flSwaySpeed, vWind, g_flTime );

        // detail bending on top
        FoliageDetailBending( vPositionOs, vNormalOs, i.vColor.xyz, matObjectToWorld, vWind );
#endif

        o.vPositionWs = mul( matObjectToWorld, float4( vPositionOs.xyz, 1.0 ) );
	    o.vPositionPs.xyzw = Position3WsToPs( o.vPositionWs.xyz );

		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
}

//=========================================================================================================================

PS
{
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"
	#include "common/classes/Light.hlsl"

	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSMISSIVE, F_TRANSMISSIVE, Sys( ALL ) );
	StaticCombo( S_GRAZING_FADE, F_GRAZING_FADE, Sys( ALL ) );

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	#if ( S_MODE_DEPTH == 0 )
		RenderState( DepthFunc, EQUAL );
		RenderState( DepthWriteEnable, false );
	#endif

	#if S_ALPHA_TEST
		TextureAttribute( LightSim_Opacity_A, g_tColor );
		float g_flAlphaDistanceStart < Default( 500.0 ); Range( 0.0, 5000.0 ); UiGroup( "Alpha Test" ); >;
		float g_flAlphaDistanceEnd < Default( 2000.0 ); Range( 0.0, 10000.0 ); UiGroup( "Alpha Test" ); >;
	#endif

	float g_flWrapAmount < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Foliage" ); >;
	float g_flWrapStrength < Default( 0.3 ); Range( 0.0, 1.0 ); UiGroup( "Foliage" ); >;

	float g_flRimStrength < Default( 0.0 ); Range( 0.0, 2.0 ); UiGroup( "Foliage" ); >;
	float g_flRimPower < Default( 4.0 ); Range( 1.0, 8.0 ); UiGroup( "Foliage" ); >;

	float g_flBackfaceDarkening < Default( 0.7 ); Range( 0.0, 1.0 ); UiGroup( "Foliage" ); >;
	float g_flAmbientBoost < Default( 0.0 ); Range( 0.0, 0.5 ); UiGroup( "Foliage" ); >;
	float g_flDetailFadeDistance < Default( 500.0 ); Range( 100.0, 2000.0 ); UiGroup( "Foliage" ); >;
	float g_flMinRoughness < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Foliage" ); >;
	float g_flNormalVariation < Default( 0.1 ); Range( 0.0, 0.5 ); UiGroup( "Foliage" ); >;
	float g_flGrassNormalUp < Default( 0.0 ); Range( 0.0, 1.0 ); UiGroup( "Foliage" ); >;


	#if S_GRAZING_FADE
		float g_flGrazingFadeStart < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Grazing Fade" ); >;
		float g_flGrazingFadeEnd < Default( 0.1 ); Range( 0.0, 1.0 ); UiGroup( "Grazing Fade" ); >;
	#endif

	#if S_TRANSMISSIVE
		CreateInputTexture2D( TextureTransmissiveColor, Srgb, 8, "", "_color", "Material,10/60", Default3( 1.0, 1.0, 1.0 ) );
		Texture2D g_tTransmissiveColor < Channel( RGB, Box( TextureTransmissiveColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
		float g_flTransmissionScale < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Transmissive" ); >;

		void ApplyTranslucency( inout Material m, Light light, float3 viewDir, float3 transmissiveColor )
		{
			float3 lightThrough = normalize( light.Direction + m.Normal * 0.2 );
			float backlit = saturate( dot( viewDir, -lightThrough ) );
			backlit *= backlit;
			m.Emission += transmissiveColor * m.Albedo * light.Color * light.Visibility * backlit * g_flTransmissionScale * light.Attenuation;
		}
	#endif

	#if S_ALPHA_TEST
	float ApplyAlphaToCoverage( float opacity, float dist )
	{
		float eps = 1.0f / 255.0f;

		// Clip first to try to kill the wave if we're in an area of all zero
		clip( opacity - eps );

		// Distance-based alpha reference for softer edges at distance
		float distFactor = saturate( ( dist - g_flAlphaDistanceStart ) / max( g_flAlphaDistanceEnd - g_flAlphaDistanceStart, 0.001 ) );
		float alphaRef = lerp( g_flAlphaTestReference, 0.1, distFactor );

		// Sharp anti-aliased edge using shader derivatives
		float sharpness = saturate( ( opacity - alphaRef ) / max( fwidth( opacity ), 0.0001 ) + 0.5 );

		// Use sharpness as the opacity for alpha to coverage
		return sharpness;
	}

	void ApplyAlphaTest( inout Material m, float dist )
	{
		float distFactor = saturate( ( dist - g_flAlphaDistanceStart ) / max( g_flAlphaDistanceEnd - g_flAlphaDistanceStart, 0.001 ) );
		float alphaRef = lerp( g_flAlphaTestReference, 0.1, distFactor );
		float sharpness = saturate( ( m.Opacity - alphaRef ) / max( fwidth( m.Opacity ), 0.0001 ) + 0.5 );
		clip( sharpness - 0.5 );
		m.Opacity = 1.0;
	}

	#endif

	#if S_GRAZING_FADE
	void ApplyGrazingAngleFade( float3 positionWs, float3 viewDir, float2 screenPos )
	{
		float3 geometricNormal = normalize( cross( ddx( positionWs ), ddy( positionWs ) ) );
		float NdotV = abs( dot( geometricNormal, viewDir ) );
		float fade = saturate( ( NdotV - g_flGrazingFadeEnd ) / max( g_flGrazingFadeStart - g_flGrazingFadeEnd, 0.001 ) );

		const float4x4 bayer = float4x4(
			0.0/16.0,  8.0/16.0,  2.0/16.0, 10.0/16.0,
			12.0/16.0, 4.0/16.0, 14.0/16.0,  6.0/16.0,
			3.0/16.0, 11.0/16.0,  1.0/16.0,  9.0/16.0,
			15.0/16.0, 7.0/16.0, 13.0/16.0,  5.0/16.0
		);

		int2 pixel = int2( screenPos ) % 4;
		clip( fade - bayer[pixel.x][pixel.y] );
	}
	#endif

	// https://developer.nvidia.com/gpugems/gpugems/part-iii-materials/chapter-16-real-time-approximations-subsurface-scattering
	void ApplyWrappedLighting( inout Material m, Light light, float lightScale )
	{
		if ( g_flWrapStrength <= 0.0 )
			return;

		float NdotL = dot( m.Normal, light.Direction );
		float wrapped = saturate( ( NdotL + g_flWrapAmount ) / ( 1.0 + g_flWrapAmount ) );
		float wrapContribution = wrapped - saturate( NdotL );

		if ( wrapContribution > 0.0 )
			m.Emission += m.Albedo * light.Color * wrapContribution * g_flWrapStrength * lightScale;
	}

	// https://www.ronja-tutorials.com/post/012-fresnel/
	void ApplyRimLighting( inout Material m, float3 viewDir )
	{
		if ( g_flRimStrength <= 0.0 )
			return;

		float rim = 1.0 - saturate( dot( m.Normal, viewDir ) );
		rim = pow( rim, g_flRimPower );
		m.Emission += m.Albedo * rim * g_flRimStrength;
	}
	
	// Force early-z in forward pass
	#if ( S_MODE_DEPTH == 0 )
		[earlydepthstencil]
	#endif
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		// Specular occlusion
		m.Roughness = max( m.Roughness, g_flMinRoughness );

		// Grass normal
		m.Normal = normalize( lerp( m.Normal, float3( 0, 0, 1 ), g_flGrassNormalUp ) );

		// Normal variation
		if ( g_flNormalVariation > 0.0 )
		{
			float2 uv = floor( m.TextureCoords * 2.0 );
			float hash = frac( sin( dot( uv, float2( 12.9898, 78.233 ) ) ) * 43758.5453 );
			float angle = hash * 6.283;
			float2 offset = float2( cos( angle ), sin( angle ) );
			m.Normal = normalize( m.Normal + m.WorldTangentU * offset.x * g_flNormalVariation + m.WorldTangentV * offset.y * g_flNormalVariation );
		}

		float3 viewDir = normalize( g_vCameraPositionWs - m.WorldPosition );
		float dist = length( i.vPositionWithOffsetWs.xyz );
		bool closeUp = dist < g_flDetailFadeDistance * 10.0f;

		bool isBackface = dot( m.Normal, viewDir ) < 0.0;
		if ( closeUp && isBackface )
		{
			m.Normal = -m.Normal;
			
			m.Albedo *= g_flBackfaceDarkening;
		}

		#if S_GRAZING_FADE
			ApplyGrazingAngleFade( m.WorldPosition, viewDir, m.ScreenPosition.xy );
		#endif

		#if S_TRANSMISSIVE
			float3 transmissiveColor = g_tTransmissiveColor.Sample( TextureFiltering, i.vTextureCoords.xy ).rgb;
			float transmission = dot( transmissiveColor, float3( 0.299, 0.587, 0.114 ) );
		#else
			float transmission = 0.0;
		#endif

		#if S_ALPHA_TEST
			// Old alpha test method for non-MSAA - clip pixels here
			if (g_nMSAASampleCount == 1)
			{
				ApplyAlphaTest( m, dist );
			}

			float opacity = m.Opacity;
		#endif

		uint lightCount = Light::Count( m.WorldPosition );
		if ( lightCount > 0 )
		{
			Light light = Light::From( m.WorldPosition, 0, i.vLightmapUV );
			float wrapScale = light.Attenuation; // Wrap lighting ignores shadows
			float lightScale = light.Attenuation * light.Visibility;

			ApplyWrappedLighting( m, light, wrapScale );

			#if S_TRANSMISSIVE
			if ( closeUp )
				ApplyTranslucency( m, light, viewDir, transmissiveColor );
			#endif
		}

		if ( closeUp )
			ApplyRimLighting( m, viewDir );

		if ( g_flAmbientBoost > 0.0 )
			m.Emission += m.Albedo * g_flAmbientBoost;

		float4 output = ShadingModelStandard::Shade( i, m );

		// Output custom alpha to coverage for foliage, overriding the one in shadingmodel
		// Ensures we have smooth MSAA edges on foliage that are still sharp on distance
		#if S_ALPHA_TEST
			output.a = ApplyAlphaToCoverage( opacity, dist );
		#endif

		return output;
	}
}
