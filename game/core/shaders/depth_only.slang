// depth_only: the engine's stand-in depth material. Fast-path depth and shadows of opaque .shader and surface materials draw
// with it (materials/dev/depth_only.vmat), and so does the Depth mode shaders name it as the fallback for. A standalone
// module: Borealis.Gpu draws it from here alone, GPU-driven too; depth_only.shader is only Source 2's renderer's now.
// See docs/managed/standalone-modules.md.
#include "sbox/pass.slangh"
#include "vr_common.fxc"

Mode( Depth );

Feature( F_ALPHA_TEST, 0..1, "Translucent" );
Feature( F_TRANSLUCENT, 0..1, "Translucent" );
Feature( F_RENDER_BACKFACES, 0..1, "Rendering" );

StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

// An object fading out (its tint's alpha under 1) dithers its depth by the view's blue noise
DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );
DynamicComboRule( Allow1( S_ALPHA_TEST, S_TRANSLUCENT, D_OPAQUE_FADE ) );

struct PS_INPUT
{
	#if ( S_TRANSLUCENT || S_ALPHA_TEST )
		float2 vTextureCoords : TEXCOORD0;
	#endif

	#if ( S_TRANSLUCENT || S_ALPHA_TEST || D_OPAQUE_FADE )
		float flTranslucency : TEXCOORD2;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_VS )
		#if ( S_TRANSLUCENT || S_ALPHA_TEST )
			float4 vPositionPs : SV_Position;
		#else
			noperspective float4 vPositionPs : SV_Position;
		#endif
	#elif ( D_OPAQUE_FADE )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
#include "instancing.fxc"

// GPU-driven: positions pulled by vertex index, the transform from the instance record. Not skinned (the vertex cache
// never draws GPU-driven), and not alpha-tested or translucent (those read the material's texture: they draw classic)
GpuDrivenVariant( !S_ALPHA_TEST && !S_TRANSLUCENT && !D_CS_VERTEX_ANIMATION );

struct VS_INPUT
{
	float3 vPositionOs : POSITION;

	#if ( D_CS_VERTEX_ANIMATION )
		float nVertexIndex : TEXCOORD14;
		float nVertexCacheIndex : TEXCOORD15;
	#endif

	#if ( S_TRANSLUCENT || S_ALPHA_TEST )
		float2 vTextureCoords : TEXCOORD0;
	#endif

	uint nInstanceTransformID : TEXCOORD13;
	uint nBoneIndex : BLENDINDICES;
};

PS_INPUT DepthVertex( VS_INPUT i )
{
	PS_INPUT o;
	ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( i.nInstanceTransformID );

	#if ( D_CS_VERTEX_ANIMATION )
		CachedAnimatedVertex_t vertex = FetchCachedVertex( extraShaderData.nVertexCacheOffset + uint( i.nVertexCacheIndex ) );
		o.vPositionPs.xyzw = Position3WsToPs( vertex.vPosWs.xyz );
	#else
		float3x4 matObjectToWorld = GetTransformMatrix( i.nInstanceTransformID, i.nBoneIndex.x );
		float3 vPositionWs = mul( matObjectToWorld, float4( i.vPositionOs.xyz, 1.0 ) );
		o.vPositionPs.xyzw = Position3WsToPs( vPositionWs.xyz );
	#endif

	#if ( S_TRANSLUCENT || S_ALPHA_TEST )
		o.vTextureCoords.xy = i.vTextureCoords.xy;
	#endif

	#if ( S_TRANSLUCENT || S_ALPHA_TEST || D_OPAQUE_FADE )
		o.flTranslucency = extraShaderData.vTint.a;
	#endif

	return o;
}

#if ( BOREALIS_GPU_DRIVEN )
#include "borealis_gpu_driven.fxc"

// What GPU-driven stages pass on ahead of their own outputs: the draw, and the vertex's bit for the id prepass
struct GpuDrivenOutput
{
	nointerpolation uint nDraw : BOREALIS_DRAW;
	nointerpolation uint nVertexBit : BOREALIS_VERTEX;
	PS_INPUT o;
};

[shader( "vertex" )] GpuDrivenOutput MainVs( uint nVertex : SV_VulkanVertexID, uint nInstance : SV_VulkanInstanceID )
{
	uint2 vInstance = BorealisGpuDrivenInstance( nInstance );
	uint4 vDraw = BorealisGpuDrivenDraw( vInstance.y );

	VS_INPUT i = (VS_INPUT)0;
	i.vPositionOs = asfloat( BorealisGpuDrivenFetch( vDraw.x, 0, nVertex, true ).xyz );	// POSITION
	i.nBoneIndex = BorealisGpuDrivenFetch( vDraw.x, 32, nVertex, false ).x;			// BLENDINDICES
	i.nInstanceTransformID = vInstance.x;

	GpuDrivenOutput o;
	o.o = DepthVertex( i );
	o.nDraw = vInstance.y;
	o.nVertexBit = BorealisGpuDrivenVertexBit( nInstance, nVertex );
	return o;
}
#else
[shader( "vertex" )] PS_INPUT MainVs( VS_INPUT i ) { return DepthVertex( i ); }
#endif

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
GpuDrivenVariant( !S_ALPHA_TEST && !S_TRANSLUCENT );

// Shadows cut alpha-tested surfaces at the material's reference; a depth prepass only where they're almost opaque, since
// what the colour pass softens with alpha to coverage would show unshaded through the prepass
DynamicCombo( D_ALPHA_TEST_CONSERVATIVE, 0..1, Sys( ALL ) );
DynamicComboRule( Requires1( D_ALPHA_TEST_CONSERVATIVE, S_ALPHA_TEST ) );
DynamicCombo( D_DISABLE_TRANSLUCENT_CLIP, 0..1, Sys( ALL ) );
DynamicComboRule( Requires1( D_DISABLE_TRANSLUCENT_CLIP, S_TRANSLUCENT ) );
DynamicCombo( D_RENDER_BACKFACES, 0..1, Sys( ALL ) );

RenderState( DepthEnable, true );
RenderState( DepthWriteEnable, true );
RenderState( DepthFunc, GREATER_EQUAL );
#if ( D_RENDER_BACKFACES )
	RenderState( CullMode, NONE );
#else
	RenderState( CullMode, DEFAULT );
#endif

#if ( S_TRANSLUCENT || S_ALPHA_TEST )
	// Filtered as the colour passes read it (g_sAniso), so the cut falls where theirs does
	#include "common_samplers.fxc"
	Texture2D g_tColor < SrgbRead( true ); >;
	float g_flOpacityScale < Default( 1.0 ); Range( 0.0, 1.0 ); UiGroup( "Translucent" ); >;
	float g_flAlphaTestReference < Default( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); >;
#endif

#if ( D_OPAQUE_FADE )
	#include "common/classes/Bindless.hlsl"
	#define g_tBlueNoise g_bindless_Texture2D[ g_vViewLookupIndices.y ]
#endif

void DepthPixel( PS_INPUT i )
{
	#if ( S_TRANSLUCENT || S_ALPHA_TEST || D_OPAQUE_FADE )
		float flTranslucency = i.flTranslucency;
	#endif

	#if ( S_TRANSLUCENT || S_ALPHA_TEST )
		flTranslucency *= g_tColor.Sample( g_sAniso, i.vTextureCoords.xy ).a;

		#if ( S_TRANSLUCENT )
			flTranslucency *= g_flOpacityScale;
			#if ( !D_DISABLE_TRANSLUCENT_CLIP )
				if ( flTranslucency < 1.0 )
					clip( -1.0 );
			#endif
		#elif ( D_ALPHA_TEST_CONSERVATIVE )
			clip( flTranslucency - 0.99 );
		#else
			clip( flTranslucency - g_flAlphaTestReference );
		#endif
	#endif

	#if ( D_OPAQUE_FADE )
		// The fade's dither, as vr_lighting.fxc's OpaqueFadeDepth
		float flNoise = g_tBlueNoise.Load( int3( i.vPositionSs.xy % TextureDimensions2D( g_tBlueNoise, 0 ).xy, 0 ) ).g;
		clip( mad( flTranslucency, 2.0, -1.5 ) + flNoise );
	#endif
}

// Opaque depth has no pixel stage, for double-speed depth-only rendering
#if ( S_TRANSLUCENT || S_ALPHA_TEST || D_OPAQUE_FADE )
#if ( BOREALIS_GPU_DRIVEN )
#include "borealis_gpu_driven.fxc"

// The GPU-driven form: the vertex's bit + 1 at target 1, where the id prepass reads ids (target 0 only keeps it there)
struct GpuDrivenTargets
{
	float4 vUnused : SV_Target0;
	uint nId : SV_Target1;
};

[shader( "pixel" )] GpuDrivenTargets MainPs( nointerpolation uint nDraw : BOREALIS_DRAW, nointerpolation uint nVertexBit : BOREALIS_VERTEX, PS_INPUT i )
{
	DepthPixel( i );
	GpuDrivenTargets o;
	o.vUnused = 1.0;
	o.nId = nVertexBit + 1;
	return o;
}
#else
[shader( "pixel" )] void MainPs( PS_INPUT i ) { DepthPixel( i ); }
#endif
#endif

#endif // PS
