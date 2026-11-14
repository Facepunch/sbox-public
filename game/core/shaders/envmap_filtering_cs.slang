// envmap_filtering_cs.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
    #include "envmap_filtering.hlsl"

	DynamicCombo( D_QUALITY, 0..1, Sys( All ) );
	DynamicCombo( D_PASS, 0..1, Sys( All ) );
	
#if( D_PASS == 0)
	[numthreads( 8, 8, 6 )]
	[shader( "compute" )] void MainCs( uint3 vDispatchId : SV_DispatchThreadID )
	{
		DownsampleCubemap( vDispatchId );
	}
#else
	[numthreads( 64, 1, 1 )]
	[shader( "compute" )] void MainCs( uint3 vDispatchId : SV_DispatchThreadID )
	{
		FilterCubemapFast( vDispatchId );
	}
#endif

#endif // CS

