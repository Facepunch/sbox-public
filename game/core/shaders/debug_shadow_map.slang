// debug_shadow_map: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md).
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Shadow map debug view: one shadow map's depth as a thumbnail, framed by its cache state";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Forward );
Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "common/classes/Bindless.hlsl"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
struct VS_INPUT
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct VS_OUTPUT
{
	float4 vPositionPs : SV_Position;
	float2 vTexCoord : TEXCOORD0;
};

[shader( "vertex" )] VS_OUTPUT MainVs( VS_INPUT i )
{
	VS_OUTPUT o;
	o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
	o.vTexCoord = i.vTexCoord;
	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
// A point light's cube, its faces in a 3 by 2 grid, or a 2D map
DynamicCombo( D_CUBE, 0..1, Sys( ALL ) );

RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );
RenderState( CullMode, NONE );

int ShadowMapIndex < Attribute( "ShadowMapIndex" ); >;

// An orthographic cascade's depth is linear; a perspective map's falls off quickly, so it's shown on a log scale
int ShadowMapOrthographic < Attribute( "ShadowMapOrthographic" ); >;

// Its cache state's colour, around the edge
float4 ShadowMapFrame < Attribute( "ShadowMapFrame" ); >;

float3 CubeDirection( uint face, float2 uv )
{
	float2 st = uv * 2 - 1;
	switch ( face )
	{
		case 0: return float3( 1, -st.y, -st.x );
		case 1: return float3( -1, -st.y, st.x );
		case 2: return float3( st.x, 1, st.y );
		case 3: return float3( st.x, -1, -st.y );
		case 4: return float3( st.x, -st.y, 1 );
		default: return float3( -st.x, -st.y, -1 );
	}
}

[shader( "pixel" )] float4 MainPs( float4 vPositionPs : SV_Position, float2 vTexCoord : TEXCOORD0 ) : SV_Target0
{
	// Two pixels of frame
	float2 edge = fwidth( vTexCoord ) * 2;
	if ( any( vTexCoord < edge ) || any( vTexCoord > 1 - edge ) )
		return float4( ShadowMapFrame.rgb, 1 );

	#if D_CUBE
		float2 grid = vTexCoord * float2( 3, 2 );
		uint face = (uint)grid.x + 3 * (uint)grid.y;
		float depth = Bindless::GetTextureCube( ShadowMapIndex ).SampleLevel( g_sPointClamp, CubeDirection( face, frac( grid ) ), 0 ).r;
	#else
		float depth = Bindless::GetTexture2D( ShadowMapIndex ).SampleLevel( g_sPointClamp, vTexCoord, 0 ).r;
	#endif

	// Reverse-Z: the near plane is white, what nothing drew is black
	float shade = ShadowMapOrthographic != 0 ? depth : saturate( 1 + log2( max( depth, 1e-6 ) ) / 10 );
	return float4( shade.xxx, 1 );
}

#endif // PS

