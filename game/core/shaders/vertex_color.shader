HEADER
{
	DevShader = true;
	Description = "Simple shader that makes use of vertex colors";
	Version = 1;
}

MODES
{
	Forward();

	Depth( "depth_only.shader" ); 
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

FEATURES
{
	#include "vr_common_features.fxc"
}

COMMON
{
	#define BLEND_MODE_ALREADY_SET

	#include "system.fxc"
	#include "vr_common.fxc" 
}

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"

	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
};

VS
{
	#include "vr_shared_standard_vs_code.fxc"

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );
		
		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( i.nInstanceTransformID );
		o.vVertexColor.rgb = SrgbGammaToLinear( i.vColor.rgb ) * extraShaderData.vTint.rgb;

		return VS_CommonProcessing_Post( o );
	}
}

PS
{
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );

	#include "vr_shared_standard_ps_code.fxc"

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );

		LightingTerms_t lightingTerms = InitLightingTerms();

		PS_OUTPUT ps_output;
		ps_output = PS_FinalCombinerDoLighting( finalCombinerInput, lightingTerms );
		ps_output = PS_FinalCombinerDoPostProcessing( finalCombinerInput, lightingTerms, ps_output );

		return ps_output;
	}
}
