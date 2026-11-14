// sort_cs.shader as a pass module: Basalt.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// HEADER: DevShader = true;
// HEADER: Description = "Bitonic sort";

// MODES: Default();

#include "sbox/pass.slangh"

#include "common/shared.hlsl"

#if ( PROGRAM == VFX_PROGRAM_CS )
#define GROUP_SIZE 256
#define MAX_DIM_GROUPS 1024
#define MAX_DIM_THREADS ( GROUP_SIZE * MAX_DIM_GROUPS )
#define FLT_MAX 3.402823466e+38f;

RWStructuredBuffer<uint> SortBuffer < Attribute( "SortBuffer" ); >;
RWStructuredBuffer<float> DistanceBuffer < Attribute( "DistanceBuffer" ); >;
int Count < Attribute( "Count" ); >;
int Block < Attribute( "Block" ); >;
int Dim < Attribute( "Dim" ); >;

DynamicCombo( D_CLEAR, 0..1, Sys( ALL ) );

[numthreads( GROUP_SIZE, 1, 1 ) ]
[shader( "compute" )] void MainCs( uint2 dispatchId : SV_DispatchThreadID )
{
	uint currentIndex = dispatchId.x + dispatchId.y * MAX_DIM_THREADS;

	#if ( D_CLEAR )
	{
		if ( currentIndex >= Count )
			return;

		SortBuffer[currentIndex] = currentIndex;
		DistanceBuffer[currentIndex] = FLT_MAX;
	}
	#else
	{
		uint compareIndex = currentIndex ^ Block;
		if ( currentIndex >= Count || compareIndex >= Count || compareIndex < currentIndex )
			return;

		uint indexA = SortBuffer[currentIndex];
		uint indexB = SortBuffer[compareIndex];

		float distanceA = DistanceBuffer[indexA];
		float distanceB = DistanceBuffer[indexB];

		bool ascending = ( currentIndex & Dim ) == 0;
		float comparison = ( distanceA - distanceB ) * ( ascending ? 1 : -1 );

		if ( comparison > 0 )
		{
			SortBuffer[currentIndex] = indexB;
			SortBuffer[compareIndex] = indexA;
		}
	}
	#endif
}

#endif // CS

