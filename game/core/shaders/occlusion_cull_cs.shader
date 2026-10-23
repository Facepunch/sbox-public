HEADER
{
	DevShader = true;
	Description = "Scene renderer occlusion culling: tests instance bounds against last frame's and this frame's depth chains";
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
	// Native only: the instance count is in OcclusionStats[1]
	DynamicCombo( D_GPU_COUNT, 0..1, Sys( ALL ) );

	struct CullInstance
	{
		float3 Center;
		uint Run;
		float3 Extents; // x below zero: never cull
		uint Id;
	};

	struct CullRun
	{
		uint FirstInstance;
		uint FirstArg;
		uint DrawCount;
		uint Phases;
	};

	// Phase bits, in the order of their outputs
	#define PHASE_EARLY 1 // prepass
	#define PHASE_LATE 2 // late prepass
	#define PHASE_FINAL 4 // forward pass

	// GpuBuffer.IndirectDrawIndexedArguments
	struct DrawArgs
	{
		uint IndexCount;
		uint InstanceCount;
		uint FirstIndex;
		int BaseVertex;
		uint FirstInstance;
	};

	StructuredBuffer<CullInstance> OcclusionInstances < Attribute( "OcclusionInstances" ); >;
	StructuredBuffer<CullRun> OcclusionRuns < Attribute( "OcclusionRuns" ); >;
	RWStructuredBuffer<DrawArgs> OcclusionArgs < Attribute( "OcclusionArgs" ); >;
	RWStructuredBuffer<uint> OcclusionCulledIds < Attribute( "OcclusionCulledIds" ); >;
	RWStructuredBuffer<uint> OcclusionStats < Attribute( "OcclusionStats" ); >;

	int OcclusionInstanceCount < Attribute( "OcclusionInstanceCount" ); >;

	// 0 culls the early phase before the prepass, 1 the late and final phases after it
	int OcclusionPass < Attribute( "OcclusionPass" ); >;

	// Offset from one phase's args and ids to the next's. Zero when each run only has one phase
	int2 OcclusionPhaseStride < Attribute( "OcclusionPhaseStride" ); >;

	// This frame's depth so far: min (r) and max (g)
	Texture2D<float2> OcclusionDepthChain < Attribute( "OcclusionDepthChain" ); SrgbRead( false ); >;
	float4 OcclusionViewport < Attribute( "OcclusionViewport" ); >; // width, height, mip count, log2 of a mip 0 texel's width in pixels
	float2 OcclusionDepthRange < Attribute( "OcclusionDepthRange" ); >;

	// Row-vector matrix, read column-major, so it goes through mul( M, pos )
	float4x4 OcclusionWorldToProjection < Attribute( "OcclusionWorldToProjection" ); >;

	// Last frame's full resolution chain and the view it was drawn from. Viewport z (mip count) is 0 without one
	Texture2D<float2> OcclusionPreviousChain < Attribute( "OcclusionPreviousChain" ); SrgbRead( false ); >;
	float4 OcclusionPreviousViewport < Attribute( "OcclusionPreviousViewport" ); >;
	float2 OcclusionPreviousDepthRange < Attribute( "OcclusionPreviousDepthRange" ); >;
	float4x4 OcclusionPreviousWorldToProjection < Attribute( "OcclusionPreviousWorldToProjection" ); >;

	// Depth is reverse-Z, so the box's nearest depth is its largest and the farthest surface behind it is the chain's smallest.
	// Precise, so testing the same box against the same chain in both passes always agrees.
	bool Occluded( Texture2D<float2> chain, float4 viewport, float2 depthRange, float4x4 worldToProjection, float3 center, float3 extents )
	{
		precise float2 pixelMin = float2( 1e30, 1e30 );
		precise float2 pixelMax = float2( -1e30, -1e30 );
		precise float nearest = -1e30;

		[unroll]
		for ( int i = 0; i < 8; i++ )
		{
			float3 corner = center + extents * float3( ( i & 1 ) ? 1 : -1, ( i & 2 ) ? 1 : -1, ( i & 4 ) ? 1 : -1 );
			precise float4 clip = mul( worldToProjection, float4( corner, 1 ) );

			// Crosses the near plane
			if ( clip.w <= 0 )
				return false;

			precise float3 ndc = clip.xyz / clip.w;
			precise float2 pixel = float2( ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5 ) * viewport.xy;
			pixelMin = min( pixelMin, pixel );
			pixelMax = max( pixelMax, pixel );
			nearest = max( nearest, ndc.z );
		}

		if ( any( pixelMax < 0 ) || any( pixelMin >= viewport.xy ) )
			return false;

		float2 last = viewport.xy - 1;
		// Pad a pixel for TAA jitter
		int2 lo = (int2)clamp( floor( pixelMin ) - 1, 0, last );
		int2 hi = (int2)clamp( floor( pixelMax ) + 1, 0, last );

		float size = max( 1.0, max( hi.x - lo.x + 1, hi.y - lo.y + 1 ) );
		int firstLevel = (int)viewport.w;
		int level = clamp( (int)ceil( log2( size ) ), firstLevel, firstLevel + (int)viewport.z - 1 );
		int mip = level - firstLevel;

		uint width, height, mips;
		chain.GetDimensions( mip, width, height, mips );

		// The four texels only cover the box if it spans at most two a side, which the top mip can't always manage
		int2 a = lo >> level;
		int2 b = hi >> level;
		if ( b.x >= (int)width || b.y >= (int)height || b.x - a.x > 1 || b.y - a.y > 1 )
			return false;

		float farthest = chain.Load( int3( a.x, a.y, mip ) ).r;
		farthest = min( farthest, chain.Load( int3( b.x, a.y, mip ) ).r );
		farthest = min( farthest, chain.Load( int3( a.x, b.y, mip ) ).r );
		farthest = min( farthest, chain.Load( int3( b.x, b.y, mip ) ).r );

		// Margin keeps coplanar surfaces visible
		precise float depth = depthRange.x + nearest * ( depthRange.y - depthRange.x );
		return depth * 1.001 < farthest;
	}

	// Every draw call in the run gets the same instances
	void Emit( CullRun run, uint phase, uint id )
	{
		uint arg = run.FirstArg + phase * (uint)OcclusionPhaseStride.x;

		uint slot;
		InterlockedAdd( OcclusionArgs[arg].InstanceCount, 1, slot );
		for ( uint d = 1; d < run.DrawCount; d++ )
			InterlockedAdd( OcclusionArgs[arg + d].InstanceCount, 1 );

		OcclusionCulledIds[run.FirstInstance + phase * (uint)OcclusionPhaseStride.y + slot] = id;
	}

	// Must match nThreadsPerGroup in CSceneViewOcclusionCull::Upload.
	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		#if D_GPU_COUNT
			uint instanceCount = OcclusionStats[1];
		#else
			uint instanceCount = (uint)OcclusionInstanceCount;
		#endif

		if ( id.x >= instanceCount )
			return;

		CullInstance inst = OcclusionInstances[id.x];
		CullRun run = OcclusionRuns[inst.Run];

		uint phases = run.Phases & ( OcclusionPass == 0 ? PHASE_EARLY : ( PHASE_LATE | PHASE_FINAL ) );
		if ( run.DrawCount == 0 || phases == 0 )
			return;

		// Recompute the early result identically in both passes so late only emits prepass misses.
		bool cullable = inst.Extents.x >= 0;
		bool early = !cullable || OcclusionPreviousViewport.z == 0 || ( phases & ( PHASE_EARLY | PHASE_LATE ) ) == 0
			|| !Occluded( OcclusionPreviousChain, OcclusionPreviousViewport, OcclusionPreviousDepthRange, OcclusionPreviousWorldToProjection, inst.Center, inst.Extents );

		if ( OcclusionPass == 0 )
		{
			if ( early )
				Emit( run, 0, inst.Id );
			return;
		}

		if ( cullable && Occluded( OcclusionDepthChain, OcclusionViewport, OcclusionDepthRange, OcclusionWorldToProjection, inst.Center, inst.Extents ) )
			return;

		if ( !early && ( phases & PHASE_LATE ) )
			Emit( run, 1, inst.Id );

		if ( phases & PHASE_FINAL )
		{
			Emit( run, 2, inst.Id );

			InterlockedAdd( OcclusionStats[0], 1 );
		}
	}
}
