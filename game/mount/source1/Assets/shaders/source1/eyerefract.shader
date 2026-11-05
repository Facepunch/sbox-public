HEADER
{
	Description = "eyerefract";
}

FEATURES
{
	#include "source1/eye_features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define S_SOURCE_EYE_REFRACT 1
	#include "source1/eye_common.hlsl"
}

VS
{
	#include "source1/eye_vs.hlsl"
}

PS
{
	#include "source1/eye_ps.hlsl"
}
