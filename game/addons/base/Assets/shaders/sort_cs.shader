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
	// Sorts after every real sprite, so padding entries end up outside the drawn range.
	#define SORTKEY_MAX uint2( 0xFFFFFFFFu, 0xFFFFFFFFu )

	RWStructuredBuffer<uint> SortBuffer < Attribute( "SortBuffer" ); >;

	// Sort keys are compared lexicographically: .x is the coarse key (sort layer and order in
	// layer, packed and inverted), .y is the fine key (sort axis distance, made unsigned-comparable).
	RWStructuredBuffer<uint2> SortKeyBuffer < Attribute( "SortKeyBuffer" ); >;
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
			SortKeyBuffer[currentIndex] = SORTKEY_MAX;
		}
		#else
		{
			uint compareIndex = currentIndex ^ Block;
			if ( currentIndex >= Count || compareIndex >= Count || compareIndex < currentIndex )
				return;

			uint indexA = SortBuffer[currentIndex];
			uint indexB = SortBuffer[compareIndex];

			uint2 keyA = SortKeyBuffer[indexA];
			uint2 keyB = SortKeyBuffer[indexB];

			// Lexicographic: the fine key only breaks ties in the coarse key.
			// Equal keys must not swap, so compare both directions explicitly.
			bool greater = ( keyA.x != keyB.x ) ? ( keyA.x > keyB.x ) : ( keyA.y > keyB.y );
			bool less = ( keyA.x != keyB.x ) ? ( keyA.x < keyB.x ) : ( keyA.y < keyB.y );

			bool ascending = ( currentIndex & Dim ) == 0;

			if ( ascending ? greater : less )
			{
				SortBuffer[currentIndex] = indexB;
				SortBuffer[compareIndex] = indexA;
			}
		}
		#endif
	}
}
