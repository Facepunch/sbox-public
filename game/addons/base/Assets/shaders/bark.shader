HEADER
{
	Description = "Bark shader for trees, syncs with foliage.shader";
	DevShader = true;
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_BARK_ANIMATION, 0..1, "Bark Animation" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "vr_tools_shading_complexity.shader" );
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
	#include "common/trunk_bending.hlsl"

	StaticCombo( S_BARK_ANIMATION, F_BARK_ANIMATION, Sys( ALL ) );

#if S_BARK_ANIMATION
	float g_flSwayStrength < Default( 1.0 ); Range( 0.0, 25.0 ); UiGroup( "Bark Animation" ); >;
	float g_flSwaySpeed < Default( 1.0 ); Range( 0.0, 10.0 ); UiGroup( "Bark Animation" ); >;
#endif

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );

#if S_BARK_ANIMATION
		float3 vPositionOs = i.vPositionOs.xyz;
		float3 vWind = g_vWindDirection.xyz * g_vWindStrengthFreqMulHighStrength.x;

		ApplyTrunkBending( vPositionOs, g_flSwayStrength, g_flSwaySpeed, vWind, g_flTime );

		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID );
		o.vPositionWs = mul( matObjectToWorld, float4( vPositionOs, 1.0 ) );
		o.vPositionPs = Position3WsToPs( o.vPositionWs.xyz );
#endif

		return FinalizeVertex( o );
	}
}

PS
{
	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );

	float g_flSpecularOcclusion < Default( 0.5 ); Range( 0.0, 1.0 ); UiGroup( "Bark" ); >;

	// https://seblagarde.files.wordpress.com/2015/07/course_notes_moving_frostbite_to_pbr_v32.pdf (page 77)
	float ComputeSpecularOcclusion( float NdotV, float ao, float roughness )
	{
		return saturate( pow( NdotV + ao, exp2( -16.0 * roughness - 1.0 ) ) - 1.0 + ao );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		// Specular occlusion
		float3 viewDir = normalize( g_vCameraPositionWs - m.WorldPosition );
		float NdotV = saturate( dot( m.Normal, viewDir ) );
		float specOcc = ComputeSpecularOcclusion( NdotV, m.AmbientOcclusion, m.Roughness );
		m.AmbientOcclusion *= lerp( 1.0, specOcc, g_flSpecularOcclusion );

		return ShadingModelStandard::Shade( i, m );
	}
}
