//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Resolves a depth copy from one texture to another, doesnt care about dest format.";
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
CS
{
	#include "common.fxc"
    #define floatx float2

	DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

	// Min and max of every sample over each 4x4 pixels.
	DynamicCombo( D_CONSERVATIVE_QUARTER, 0..1, Sys( ALL ) );

#if D_MSAA
	Texture2DMS<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#else
	Texture2D<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#endif

	RWTexture2D<floatx> g_tDestDepth    < Attribute( "DestDepth" ); >;

	float2 g_vSourceSize < Attribute( "DepthResolveSourceSize" ); >;

	float2 LoadMinMax( uint2 pixel )
	{
		float lo = 1, hi = 0;

		#if D_MSAA
			uint2 dim;
			uint sampleCount;
			g_tSourceDepth.GetDimensions( dim.x, dim.y, sampleCount );
			for ( uint i = 0; i < sampleCount; i++ )
			{
				float d = g_tSourceDepth.Load( g_vViewportOffset + pixel, i ).r;
				lo = min( lo, d );
				hi = max( hi, d );
			}
		#else
			float d = g_tSourceDepth.Load( int3( g_vViewportOffset.xy + pixel, 0 ) ).r;
			lo = d;
			hi = d;
		#endif

		return float2( lo, hi );
	}

	#if D_CONSERVATIVE_QUARTER
		groupshared float2 s_minMax[ 16 * 16 ];

		[numthreads( 16, 16, 1 )]
	#else
		[numthreads( 8, 8, 1 )]
	#endif
	void MainCs( uint nGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupThreadId : SV_GroupThreadID )
	{
		floatx result;

		#if D_CONSERVATIVE_QUARTER
		{
			float2 minMax = float2( 1, 0 );
			if ( vThreadId.x < (uint)g_vSourceSize.x && vThreadId.y < (uint)g_vSourceSize.y )
			{
				minMax = LoadMinMax( vThreadId.xy );
			}

			s_minMax[ nGroupIndex ] = minMax;
			GroupMemoryBarrierWithGroupSync();

			if ( ( vGroupThreadId.x & 3 ) != 0 || ( vGroupThreadId.y & 3 ) != 0 )
				return;

			for ( uint y = 0; y < 4; y++ )
			{
				for ( uint x = 0; x < 4; x++ )
				{
					float2 d = s_minMax[ ( vGroupThreadId.y + y ) * 16 + vGroupThreadId.x + x ];
					minMax = float2( min( minMax.x, d.x ), max( minMax.y, d.y ) );
				}
			}

			g_tDestDepth[ vThreadId.xy / 4 ] = minMax;
			return;
		}
		#else
		{
			//
			// use min to get the closest depth to the camera
			//
			#if D_MSAA
				result = g_tSourceDepth.Load( g_vViewportOffset + vThreadId.xy, 0 ).r;
			#else
				result = g_tSourceDepth.Load( int3( g_vViewportOffset.xy + vThreadId.xy, 0 ) ).r;
			#endif
		}
		#endif

		g_tDestDepth[ vThreadId.xy ] = result;
	}
}
