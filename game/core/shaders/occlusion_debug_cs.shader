HEADER
{
	DevShader = true;
	Description = "Occlusion culling debug view: clears the cull's debug grid, then resolves it into an image";
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
	#include "system.fxc"
	#include "common.fxc"
}

CS
{
	// 0 clears the grid and counters, 1 resolves the grid into OcclusionDebugOutput
	DynamicCombo( D_RESOLVE, 0..1, Sys( ALL ) );

	// Must match occlusion_cull_cs.shader
	#define DEBUG_HEADER 16

	RWStructuredBuffer<uint> OcclusionDebug < Attribute( "OcclusionDebug" ); >;
	int2 OcclusionDebugSize < Attribute( "OcclusionDebugSize" ); >;

	#if D_RESOLVE
		RWTexture2D<float4> OcclusionDebugOutput < Attribute( "OcclusionDebugOutput" ); >;

		// The chain the late cull tested against, drawn under the boxes when the mip is 0 or more
		Texture2D<float2> OcclusionDebugChain < Attribute( "OcclusionDebugChain" ); SrgbRead( false ); >;
		int OcclusionDebugMip < Attribute( "OcclusionDebugMip" ); >;

		// The occlusion cull shader's CELL_ values. Must match DebugOverlay.OcclusionCulling
		float4 CellColor( uint cell )
		{
			switch ( cell )
			{
				case 1: return float4( 0.5, 0.5, 0.5, 1 ); // never culled
				case 2: return float4( 0.2, 0.9, 0.3, 1 ); // visible
				case 3: return float4( 0.3, 0.5, 1.0, 1 ); // can't test
				case 4: return float4( 1.0, 0.55, 0.1, 1 ); // partly hidden fragment
				case 5: return float4( 1.0, 0.2, 0.2, 1 ); // hidden
				case 6: return float4( 1.0, 0.95, 0.2, 1 ); // late prepass
				default: return 0;
			}
		}

		// Reverse-Z falls off quickly with distance, so the depth is shown on a log scale with the near plane white
		float3 DepthShade( uint2 cell )
		{
			uint width, height, mips;
			OcclusionDebugChain.GetDimensions( 0, width, height, mips );
			int mip = min( OcclusionDebugMip, (int)mips - 1 );
			OcclusionDebugChain.GetDimensions( mip, width, height, mips );

			float2 uv = ( cell + 0.5 ) / OcclusionDebugSize;
			float depth = OcclusionDebugChain.Load( int3( uv * float2( width, height ), mip ) ).r;
			return saturate( 1 + log2( max( depth, 1e-6 ) ) / 16 ).xxx;
		}
	#endif

	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		if ( any( id.xy >= (uint2)OcclusionDebugSize ) )
			return;

		uint index = DEBUG_HEADER + id.y * OcclusionDebugSize.x + id.x;

		#if D_RESOLVE
			float4 color = CellColor( OcclusionDebug[index] );
			if ( OcclusionDebugMip >= 0 )
				color = float4( lerp( DepthShade( id.xy ), color.rgb, color.a ), 1 );

			OcclusionDebugOutput[id.xy] = color;
		#else
			OcclusionDebug[index] = 0;

			if ( all( id.xy == 0 ) )
			{
				for ( uint i = 0; i < DEBUG_HEADER; i++ )
					OcclusionDebug[i] = 0;
			}
		#endif
	}
}
