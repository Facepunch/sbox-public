// screen_space_shadows_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
//=========================================================================================================
// Screen Space Shadows (wavefront contact shadows)
//
// Port of Bend Studio's screen-space shadow technique (SIGGRAPH 2023, Sony Interactive Entertainment).
// The actual ray-marching/wavefront math lives verbatim in common/thirdparty/bend_sss_gpu.h (Apache-2.0).
// This shader only wires the s&box pipeline depth-chain, output mask and runtime parameters into it.
//
// Dispatched up to 8 times per light from C# (see ScreenSpaceShadows component), each dispatch using
// numthreads[WAVE_SIZE,1,1] with group counts (WAVE_SIZE, WaveCountY, WaveCountZ) and a per-dispatch
// WaveOffset constant. LightCoordinate is shared across all dispatches of a light.
//=========================================================================================================
// HEADER: Description = "Bend Studio screen space contact shadows";
// HEADER: DevShader = true;

#include "sbox/pass.slangh"

Mode( Default );

#include "sbox/system.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
#include "sbox/view.slangh"
#include "sbox/math.slangh"
#include "sbox/classes/depth.slangh"

// Compile-time configuration for the Bend header (see bend_sss_gpu.h for meaning).
#define WAVE_SIZE 64
#define SAMPLE_COUNT 60
#define HARD_SHADOW_SAMPLES 4
#define FADE_OUT_SAMPLES 8

#include "sbox/thirdparty/bend_sss_gpu.slangh"

// Pipeline depth chain, typed as <float> so it matches DispatchParameters.DepthTexture (reads .r).
Texture2D<float> g_tSssDepth < Attribute( "DepthChainDownsample" ); SrgbRead( false ); >;

// Output single-channel screen-space shadow mask (1 = lit, 0 = shadowed).
RWTexture2D<float> g_outShadow < Attribute( "OutputShadow" ); >;

// Point sampler, clamp-to-border so off-screen reads return the far-depth border colour (0 in reverse-Z).
SamplerState g_sSssPointBorder < Filter( POINT ); AddressU( BORDER ); AddressV( BORDER ); AddressW( BORDER ); BorderColor( float4( 0, 0, 0, 0 ) ); >;

struct SssConstants_t
{
	float4 LightCoordinate;
	int2 WaveOffset;
	float2 InvDepthTextureSize;
	float ShadowContrast;
};

cbuffer SssConstants
{
	SssConstants_t g_Sss;
};

[numthreads( WAVE_SIZE, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupID : SV_GroupID, uint vGroupThreadID : SV_GroupThreadID )
{
	DispatchParameters p;
	p.SetDefaults();

	p.LightCoordinate = g_Sss.LightCoordinate;
	p.WaveOffset = g_Sss.WaveOffset;
	p.InvDepthTextureSize = g_Sss.InvDepthTextureSize;
	p.ShadowContrast = g_Sss.ShadowContrast;

	// Fixed tuning is set here, not in the constant buffer, so the compiler folds it
	p.UseEarlyOut = true;
	p.FarDepthValue = 0; // reverse-Z
	p.NearDepthValue = 1;

	p.DepthTexture = g_tSssDepth;
	p.OutputTexture = g_outShadow;
	p.PointBorderSampler = g_sSssPointBorder;

	WriteScreenSpaceShadow( p, (int3)vGroupID, (int)vGroupThreadID );
}

#endif // CS

