HEADER
{
	Description = "refract";
}

FEATURES
{
	#include "source1/refraction_features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#define S_SOURCE_CORE 0
	#define S_SOURCE_AFTERSHOCK 0
	#include "source1/refraction_common.hlsl"
}

VS
{
	#include "source1/refraction_vs.hlsl"
}

PS
{
	#include "source1/refraction_ps.hlsl"
}
