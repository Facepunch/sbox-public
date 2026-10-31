HEADER
{
	Description = "UnlitGeneric";
}

FEATURES
{
	#include "source1/generic_features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#define S_SOURCE_VORTWARP 0
	#define S_SOURCE_TRANSITION 0
	#define S_SOURCE_UNLIT 1
	#include "source1/generic_common.hlsl"
}

VS
{
	#include "source1/generic_vs.hlsl"
}

PS
{
	#include "source1/generic_ps.hlsl"
}
