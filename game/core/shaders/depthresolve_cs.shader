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

#if D_MSAA
	Texture2DMS<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#else
	Texture2D<float>  g_tSourceDepth  < Attribute( "SourceDepth" ); >;
#endif

	RWTexture2D<floatx> g_tDestDepth    < Attribute( "DestDepth" ); >;

	[numthreads( 8, 8, 1 )]
	void MainCs( uint nGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID )
	{
		uint2 dim;
		uint sampleCount; 

		//
		// use min to get the closest depth to the camera
		//
		floatx result;
		#if D_MSAA
		{
			result = g_tSourceDepth.Load( g_vViewportOffset + vThreadId.xy, 0 ).r;
		}
		#else
		{
			result = g_tSourceDepth.Load( int3( g_vViewportOffset.xy + vThreadId.xy, 0 ) ).r;
		}
		#endif
 
        g_tDestDepth[ vThreadId.xy ] = result;
    }
}

