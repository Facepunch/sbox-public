HEADER
{
	Description = "VortWarp";
}

FEATURES
{
	#include "source1/generic_features.hlsl"
	Feature( F_SOURCE_UNLIT, 0..1, "Source 1" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define S_SOURCE_VORTWARP 1
	#define S_SOURCE_TRANSITION 0
	#include "source1/generic_common.hlsl"
}

VS
{
	#include "source1/generic_vs.hlsl"
}

PS
{
	StaticCombo( S_SOURCE_UNLIT, F_SOURCE_UNLIT, Sys( ALL ) );
	#include "source1/generic_ps.hlsl"
}
