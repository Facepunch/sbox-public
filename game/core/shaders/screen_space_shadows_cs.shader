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
HEADER
{
	Description = "Bend Studio screen space contact shadows";
	DevShader = true;
}

MODES
{
	Default();
}

COMMON
{
	#include "system.fxc"
}

CS
{
	#include "common.fxc"
	#include "math_general.fxc"
	#include "common/classes/Depth.hlsl"

	// Compile-time configuration for the Bend header (see bend_sss_gpu.h for meaning).
	#define WAVE_SIZE 64
	#define SAMPLE_COUNT 60
	#define HARD_SHADOW_SAMPLES 4
	#define FADE_OUT_SAMPLES 8

	#include "common/thirdparty/bend_sss_gpu.hlsl"

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
		float2 DepthBounds;
		float SurfaceThickness;
		float BilinearThreshold;
		float ShadowContrast;
		float FarDepthValue;
		float NearDepthValue;
		int IgnoreEdgePixels;
		int UsePrecisionOffset;
		int BilinearSamplingOffsetMode;
		int UseEarlyOut;
	};

	cbuffer SssConstants
	{
		SssConstants_t g_Sss;
	};

	[numthreads( WAVE_SIZE, 1, 1 )]
	void MainCs( uint3 vGroupID : SV_GroupID, uint vGroupThreadID : SV_GroupThreadID )
	{
		DispatchParameters p;
		p.SetDefaults();

		p.LightCoordinate = g_Sss.LightCoordinate;
		p.WaveOffset = g_Sss.WaveOffset;
		p.InvDepthTextureSize = g_Sss.InvDepthTextureSize;
		p.DepthBounds = g_Sss.DepthBounds;
		p.SurfaceThickness = g_Sss.SurfaceThickness;
		p.BilinearThreshold = g_Sss.BilinearThreshold;
		p.ShadowContrast = g_Sss.ShadowContrast;
		p.FarDepthValue = g_Sss.FarDepthValue;
		p.NearDepthValue = g_Sss.NearDepthValue;
		p.IgnoreEdgePixels = g_Sss.IgnoreEdgePixels != 0;
		p.UsePrecisionOffset = g_Sss.UsePrecisionOffset != 0;
		p.BilinearSamplingOffsetMode = g_Sss.BilinearSamplingOffsetMode != 0;
		p.UseEarlyOut = g_Sss.UseEarlyOut != 0;

		p.DepthTexture = g_tSssDepth;
		p.OutputTexture = g_outShadow;
		p.PointBorderSampler = g_sSssPointBorder;

		WriteScreenSpaceShadow( p, (int3)vGroupID, (int)vGroupThreadID );
	}
}

