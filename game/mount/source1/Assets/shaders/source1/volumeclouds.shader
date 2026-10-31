HEADER
{
	Description = "Source 1 layered volume clouds";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
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

	PixelInput MainVs( VertexInput v )
	{
		return FinalizeVertex( ProcessVertex( v ) );
	}
}

PS
{
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#define CULL_MODE_ALREADY_SET 1
	#include "common/pixel.hlsl"

	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, INV_SRC_ALPHA );

	SamplerState g_sColor < Attribute( "g_tColorSampler" ); Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sTexture2 < Attribute( "g_tTexture2Sampler" ); Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sTexture3 < Attribute( "g_tTexture3Sampler" ); Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	CreateInputTexture2D( SourceColor, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( SourceTexture2, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 0 ) );
	CreateInputTexture2D( SourceTexture3, Srgb, 8, "None", "_color", "Source 1", Default4( 0, 0, 0, 0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( SourceColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tTexture2 < Channel( RGBA, Box( SourceTexture2 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tTexture3 < Channel( RGBA, Box( SourceTexture3 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	float3 g_vSourceCloudSpeed < Default3( 4, 2, 1 ); >;
	float g_flSourceEffectTime < Default( 0 ); >;
	float g_flSourceEffectRuntime < Default( 1 ); >;

	float2 RotateCloud( float2 value, float angle )
	{
		float cosine = cos( angle );
		float sine = sin( angle );
		return float2( cosine * value.x - sine * value.y, sine * value.x + cosine * value.y );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 view = normalize( positionWs - g_vCameraPositionWs.xyz );
		float2 tangentView = float2( dot( view, i.vTangentUWs ), dot( view, i.vTangentVWs ) );
		float time = lerp( g_flSourceEffectTime, g_flTime, g_flSourceEffectRuntime ) * 0.065;
		float2 uv0 = RotateCloud( i.vTextureCoords.xy - 0.5, time * g_vSourceCloudSpeed.x ) + 0.5;
		float2 uv1 = RotateCloud( i.vTextureCoords.xy - 0.5, time * g_vSourceCloudSpeed.y ) + 0.5;
		float2 uv2 = RotateCloud( i.vTextureCoords.xy - 0.5, time * g_vSourceCloudSpeed.z ) + 0.5;
		float2 view0 = RotateCloud( tangentView, time * g_vSourceCloudSpeed.x );
		float2 view1 = RotateCloud( tangentView, time * g_vSourceCloudSpeed.y );
		float2 view2 = RotateCloud( tangentView, time * g_vSourceCloudSpeed.z );
		float3 color = 0;
		float transparency = 1;
		[unroll] for ( int layer = 9; layer >= 0; layer-- )
		{
			float4 inner = g_tColor.Sample( g_sColor, saturate( uv0 + view0 * (0.005 * layer) ) );
			float4 middle = g_tTexture2.Sample( g_sTexture2, saturate( uv1 + view1 * (0.005 * layer) ) );
			float4 outer = g_tTexture3.Sample( g_sTexture3, saturate( uv2 + view2 * (0.005 * layer) ) );
			float opacity = 1 - (1 - inner.a) * (1 - middle.a) * (1 - outer.a);
			float blend = saturate( pow( opacity, lerp( 5, 0.8625, layer / 9.0 ) ) );
			color = inner.rgb * inner.a + middle.rgb * middle.a + outer.rgb * outer.a + color * (1 - blend);
			transparency *= 1 - blend;
		}
		return float4( color, 1 - transparency );
	}
}
