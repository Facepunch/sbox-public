HEADER
{
	DevShader = true;
	Description = "Bitonic sort";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "common/shared.hlsl"
}

CS
{
	#define GROUP_SIZE 256
	#define MAX_DIM_GROUPS 1024
	#define MAX_DIM_THREADS ( GROUP_SIZE * MAX_DIM_GROUPS )

	RWStructuredBuffer<uint> SortBuffer < Attribute( "SortBuffer" ); >;

	// Sort keys, compared as uints. For sprites: camera distance biased by Z index, in order-preserving float bits.
	RWStructuredBuffer<uint> DistanceBuffer < Attribute( "DistanceBuffer" ); >;
	int Count < Attribute( "Count" ); >;
	int Block < Attribute( "Block" ); >;
	int Dim < Attribute( "Dim" ); >;

	DynamicCombo( D_CLEAR, 0..1, Sys( ALL ) );

	[numthreads( GROUP_SIZE, 1, 1 ) ]
	void MainCs( uint2 dispatchId : SV_DispatchThreadID )
	{
		uint currentIndex = dispatchId.x + dispatchId.y * MAX_DIM_THREADS;

		#if ( D_CLEAR )
		{
			if ( currentIndex >= Count )
				return;

			SortBuffer[currentIndex] = currentIndex;
			DistanceBuffer[currentIndex] = 0xFFFFFFFF;
		}
		#else
		{
			uint compareIndex = currentIndex ^ Block;
			if ( currentIndex >= Count || compareIndex >= Count || compareIndex < currentIndex )
				return;

			uint indexA = SortBuffer[currentIndex];
			uint indexB = SortBuffer[compareIndex];

			uint keyA = DistanceBuffer[indexA];
			uint keyB = DistanceBuffer[indexB];

			bool ascending = ( currentIndex & Dim ) == 0;
			bool outOfOrder = ascending ? ( keyA > keyB ) : ( keyA < keyB );

			if ( outOfOrder )
			{
				SortBuffer[currentIndex] = indexB;
				SortBuffer[compareIndex] = indexA;
			}
		}
		#endif
	}
}
