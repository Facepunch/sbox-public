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
	// Native only: the instance count is in OcclusionStats[STAT_INSTANCES]
	DynamicCombo( D_GPU_COUNT, 0..1, Sys( ALL ) );

	// r_occlusion_debug: draw and count what the cull decided
	DynamicCombo( D_OCCLUSION_DEBUG, 0..1, Sys( ALL ) );

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

	// Aggregate fragment runs, native only. Each instance is a fragment: Id is its index in the run, its args are at
	// FirstArg + Id. RUN_COMPACT args are laid out as [ survivors ][ fragments ][ count ], so the fragments start at
	// FirstArg + DrawCount and the survivor count is the IndexCount at FirstArg + 2 * DrawCount.
	#define RUN_FRAGMENTS 8
	#define RUN_COMPACT 16

	// Most chain texels a side a box is tested against. Fragments are usually big on screen, so they test more
	#define INSTANCE_GRID 2
	#define FRAGMENT_GRID 8

	// OcclusionStats slots. Must match CSceneViewOcclusionCull::Stat_t; managed only has the first two. The fragment
	// slots count the final phase's aggregate fragments
	#define STAT_VISIBLE 0 // final phase instances kept
	#define STAT_INSTANCES 1 // instances recorded
	#define STAT_FINAL_INSTANCES 2 // instances recorded for the final phase, set by the CPU
	#define STAT_FRAGMENTS 3
	#define STAT_FRAGMENTS_HIDDEN 4
	#define STAT_TRIANGLES 5
	#define STAT_TRIANGLES_HIDDEN 6
	#define STAT_TRIANGLES_MISSED 7 // estimate of hidden triangles in fragments that were still drawn
	#define STAT_TRIANGLES_UNTESTED 8

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

	// Last frame's full resolution chain, or the frozen chain, and the view it was drawn from. Viewport z (mip count) is 0 without one
	Texture2D<float2> OcclusionPreviousChain < Attribute( "OcclusionPreviousChain" ); SrgbRead( false ); >;
	float4 OcclusionPreviousViewport < Attribute( "OcclusionPreviousViewport" ); >;
	float2 OcclusionPreviousDepthRange < Attribute( "OcclusionPreviousDepthRange" ); >;
	float4x4 OcclusionPreviousWorldToProjection < Attribute( "OcclusionPreviousWorldToProjection" ); >;

	// r_occlusion_early 2: every cullable instance misses the early cull, so the late prepass draws it
	int OcclusionRejectEarly < Attribute( "OcclusionRejectEarly" ); >;

	#if D_OCCLUSION_DEBUG
		// Counters, then a grid of the view's pixels, each the highest CELL_ of the box edges drawn through it
		RWStructuredBuffer<uint> OcclusionDebug < Attribute( "OcclusionDebug" ); >;
		int2 OcclusionDebugSize < Attribute( "OcclusionDebugSize" ); >;

		// A bit per CELL_ that gets boxes drawn
		int OcclusionDebugShown < Attribute( "OcclusionDebugShown" ); >;

		// The camera's view, which the boxes follow even while the cull is frozen
		float4x4 OcclusionDebugWorldToProjection < Attribute( "OcclusionDebugWorldToProjection" ); >;

		// Must match OcclusionDebugTarget
		#define DEBUG_HEADER 16

		// What the final phase made of an instance, in the order they win a cell. The counters use the same slots
		#define CELL_NEVER 1
		#define CELL_VISIBLE 2
		#define CELL_UNTESTED 3
		#define CELL_PARTLY_HIDDEN 4 // a fragment that's drawn, but some of its texels are hidden
		#define CELL_HIDDEN 5
		#define CELL_LATE 6 // drawn by the late prepass: hidden last frame, visible now

		#define COUNT_FINAL 0
		#define COUNT_EARLY 7
		#define COUNT_EARLY_DRAWN 8
	#endif

	// Chain texels under a box. Tested is 0 when the box can't be tested: it crosses the near plane, is off screen, or is
	// too big for the top mip
	struct Coverage
	{
		uint Hidden;
		uint Tested;
	};

	bool IsHidden( Coverage coverage )
	{
		return coverage.Tested > 0 && coverage.Hidden == coverage.Tested;
	}

	// Tests the box against the chain at the finest mip where it spans at most grid texels a side.
	// Depth is reverse-Z, so the box's nearest depth is its largest and the farthest surface behind it is the chain's smallest.
	// Precise, so testing the same box against the same chain in both passes always agrees.
	Coverage TestBox( Texture2D<float2> chain, float4 viewport, float2 depthRange, float4x4 worldToProjection, float3 center, float3 extents, int grid )
	{
		Coverage untested = { 0, 0 };
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
				return untested;

			precise float3 ndc = clip.xyz / clip.w;
			precise float2 pixel = float2( ndc.x * 0.5 + 0.5, 0.5 - ndc.y * 0.5 ) * viewport.xy;
			pixelMin = min( pixelMin, pixel );
			pixelMax = max( pixelMax, pixel );
			nearest = max( nearest, ndc.z );
		}

		if ( any( pixelMax < 0 ) || any( pixelMin >= viewport.xy ) )
			return untested;

		float2 last = viewport.xy - 1;
		// Pad a pixel for TAA jitter
		int2 lo = (int2)clamp( floor( pixelMin ) - 1, 0, last );
		int2 hi = (int2)clamp( floor( pixelMax ) + 1, 0, last );

		// Pick the mip where the box spans at most grid texels
		float size = max( 1.0, max( hi.x - lo.x + 1, hi.y - lo.y + 1 ) );
		int firstLevel = (int)viewport.w;
		int level = clamp( (int)ceil( log2( size / ( grid - 1 ) ) ), firstLevel, firstLevel + (int)viewport.z - 1 );
		int mip = level - firstLevel;

		uint width, height, mips;
		chain.GetDimensions( mip, width, height, mips );

		int2 a = lo >> level;
		int2 b = hi >> level;
		if ( b.x >= (int)width || b.y >= (int)height || any( b - a >= grid ) )
			return untested;

		// Margin keeps coplanar surfaces visible
		precise float depth = depthRange.x + nearest * ( depthRange.y - depthRange.x );

		uint hidden = 0;
		[loop]
		for ( int y = a.y; y <= b.y; y++ )
		{
			[loop]
			for ( int x = a.x; x <= b.x; x++ )
				hidden += ( depth * 1.001 < chain.Load( int3( x, y, mip ) ).r ) ? 1 : 0;
		}

		Coverage coverage = { hidden, ( b.x - a.x + 1 ) * ( b.y - a.y + 1 ) };
		return coverage;
	}

	bool Occluded( Texture2D<float2> chain, float4 viewport, float2 depthRange, float4x4 worldToProjection, float3 center, float3 extents, int grid )
	{
		return IsHidden( TestBox( chain, viewport, depthRange, worldToProjection, center, extents, grid ) );
	}

	// Every draw call in the run gets the same instances
	void EmitInstance( CullRun run, uint phase, uint id )
	{
		uint arg = run.FirstArg + phase * (uint)OcclusionPhaseStride.x;

		uint slot;
		InterlockedAdd( OcclusionArgs[arg].InstanceCount, 1, slot );
		for ( uint d = 1; d < run.DrawCount; d++ )
			InterlockedAdd( OcclusionArgs[arg + d].InstanceCount, 1 );

		OcclusionCulledIds[run.FirstInstance + phase * (uint)OcclusionPhaseStride.y + slot] = id;
	}

	// Where a fragment's own draw is: after the survivors in a compacted run
	uint FragmentArg( CullRun run, uint index )
	{
		return run.FirstArg + index + ( ( run.Phases & RUN_COMPACT ) ? run.DrawCount : 0 );
	}

	// Draws the fragment once: in place, or appended to the survivors and counted
	void EmitFragment( CullRun run, uint index )
	{
		if ( ( run.Phases & RUN_COMPACT ) == 0 )
		{
			OcclusionArgs[run.FirstArg + index].InstanceCount = 1;
			return;
		}

		uint slot;
		InterlockedAdd( OcclusionArgs[run.FirstArg + 2 * run.DrawCount].IndexCount, 1, slot ); // the survivor count

		DrawArgs args = OcclusionArgs[FragmentArg( run, index )];
		args.InstanceCount = 1;
		OcclusionArgs[run.FirstArg + slot] = args;
	}

	void Emit( CullRun run, uint phase, CullInstance inst )
	{
		if ( run.Phases & RUN_FRAGMENTS )
			EmitFragment( run, inst.Id );
		else
			EmitInstance( run, phase, inst.Id );
	}

	// Final phase fragment stats
	void CountFragment( CullRun run, uint index, Coverage coverage )
	{
		uint triangles = OcclusionArgs[FragmentArg( run, index )].IndexCount / 3;
		InterlockedAdd( OcclusionStats[STAT_FRAGMENTS], 1 );
		InterlockedAdd( OcclusionStats[STAT_TRIANGLES], triangles );

		if ( coverage.Tested == 0 )
		{
			InterlockedAdd( OcclusionStats[STAT_TRIANGLES_UNTESTED], triangles );
		}
		else if ( IsHidden( coverage ) )
		{
			InterlockedAdd( OcclusionStats[STAT_FRAGMENTS_HIDDEN], 1 );
			InterlockedAdd( OcclusionStats[STAT_TRIANGLES_HIDDEN], triangles );
		}
		else if ( coverage.Hidden > 0 )
		{
			// Rough, assumes the triangles are spread evenly over the box
			InterlockedAdd( OcclusionStats[STAT_TRIANGLES_MISSED], (uint)( (float)triangles * coverage.Hidden / coverage.Tested ) );
		}
	}

	#if D_OCCLUSION_DEBUG

	void DebugCount( uint slot )
	{
		InterlockedAdd( OcclusionDebug[slot], 1 );
	}

	void DebugMark( int x, int y, uint cell )
	{
		InterlockedMax( OcclusionDebug[DEBUG_HEADER + y * OcclusionDebugSize.x + x], cell );
	}

	// Trims the segment to the grid (Liang-Barsky). False if none of it is on the grid
	bool DebugClip( inout float2 a, inout float2 b )
	{
		float2 d = b - a;
		float2 last = OcclusionDebugSize - 1;
		float p[4] = { -d.x, d.x, -d.y, d.y };
		float q[4] = { a.x, last.x - a.x, a.y, last.y - a.y };
		float t0 = 0;
		float t1 = 1;

		[unroll]
		for ( int i = 0; i < 4; i++ )
		{
			if ( p[i] == 0 )
			{
				if ( q[i] < 0 )
					return false;
				continue;
			}

			float t = q[i] / p[i];
			if ( p[i] < 0 )
				t0 = max( t0, t );
			else
				t1 = min( t1, t );
		}

		if ( t0 > t1 )
			return false;

		b = a + d * t1;
		a = a + d * t0;
		return true;
	}

	float2 DebugPixel( float4 clip )
	{
		return float2( clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5 ) * OcclusionDebugSize;
	}

	// A pixel wide line between clip space points, clipped to the near plane and the grid
	void DebugLine( float4 p, float4 q, uint cell )
	{
		const float nearest = 1e-3;
		if ( p.w < nearest && q.w < nearest )
			return;

		if ( p.w < nearest )
			p = lerp( p, q, ( nearest - p.w ) / ( q.w - p.w ) );
		else if ( q.w < nearest )
			q = lerp( q, p, ( nearest - q.w ) / ( p.w - q.w ) );

		float2 a = DebugPixel( p );
		float2 b = DebugPixel( q );
		if ( !DebugClip( a, b ) )
			return;

		float2 d = b - a;
		int steps = (int)ceil( max( abs( d.x ), abs( d.y ) ) );
		float2 step = steps > 0 ? d / steps : 0;

		[loop]
		for ( int i = 0; i <= steps; i++ )
		{
			int2 pixel = (int2)( a + step * i );
			DebugMark( pixel.x, pixel.y, cell );
		}
	}

	// The box's 12 edges as the camera sees them
	void DebugBox( CullInstance inst, uint cell )
	{
		if ( ( OcclusionDebugShown & ( 1 << cell ) ) == 0 )
			return;

		float4 corners[8];

		[unroll]
		for ( int i = 0; i < 8; i++ )
		{
			float3 corner = inst.Center + inst.Extents * float3( ( i & 1 ) ? 1 : -1, ( i & 2 ) ? 1 : -1, ( i & 4 ) ? 1 : -1 );
			corners[i] = mul( OcclusionDebugWorldToProjection, float4( corner, 1 ) );
		}

		// Corners one bit apart share an edge
		[unroll]
		for ( int c = 0; c < 8; c++ )
		{
			[unroll]
			for ( int axis = 0; axis < 3; axis++ )
			{
				int bit = 1 << axis;
				if ( ( c & bit ) == 0 )
					DebugLine( corners[c], corners[c | bit], cell );
			}
		}
	}

	// Counts and draws an instance as the late cull leaves it
	void DebugLate( CullInstance inst, bool missedEarly, bool finalPhase, bool fragment, bool cullable, bool hidden, Coverage coverage )
	{
		// Native's final phase never tests the early cull, so a late prepass draw is the only place it shows
		if ( missedEarly && !hidden )
		{
			DebugCount( CELL_LATE );
			DebugBox( inst, CELL_LATE );
		}

		if ( !finalPhase )
			return;

		uint cell = !cullable ? CELL_NEVER
			: coverage.Tested == 0 ? CELL_UNTESTED
			: hidden ? CELL_HIDDEN
			: fragment && coverage.Hidden > 0 ? CELL_PARTLY_HIDDEN
			: CELL_VISIBLE;

		DebugCount( COUNT_FINAL );
		DebugCount( cell );
		DebugBox( inst, cell );
	}

	#endif

	// Must match nThreadsPerGroup in CSceneViewOcclusionCull::Upload.
	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		#if D_GPU_COUNT
			uint instanceCount = OcclusionStats[STAT_INSTANCES];
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

		bool fragment = ( run.Phases & RUN_FRAGMENTS ) != 0;
		int grid = fragment ? FRAGMENT_GRID : INSTANCE_GRID;

		// Recompute the early result identically in both passes so late only emits prepass misses.
		bool cullable = inst.Extents.x >= 0;
		bool early = !cullable || OcclusionPreviousViewport.z == 0 || ( phases & ( PHASE_EARLY | PHASE_LATE ) ) == 0
			|| ( OcclusionRejectEarly == 0 && !Occluded( OcclusionPreviousChain, OcclusionPreviousViewport, OcclusionPreviousDepthRange, OcclusionPreviousWorldToProjection, inst.Center, inst.Extents, grid ) );

		if ( OcclusionPass == 0 )
		{
			if ( early )
				Emit( run, 0, inst );

			#if D_OCCLUSION_DEBUG
				DebugCount( COUNT_EARLY );
				if ( early )
					DebugCount( COUNT_EARLY_DRAWN );
			#endif
			return;
		}

		// The late prepass only draws what the early one missed. Without that or the final phase, nothing needs testing
		bool missedEarly = !early && ( phases & PHASE_LATE ) != 0;
		bool finalPhase = ( phases & PHASE_FINAL ) != 0;
		if ( !missedEarly && !finalPhase )
			return;

		Coverage coverage = { 0, 0 };
		if ( cullable )
			coverage = TestBox( OcclusionDepthChain, OcclusionViewport, OcclusionDepthRange, OcclusionWorldToProjection, inst.Center, inst.Extents, grid );

		bool hidden = IsHidden( coverage );
		if ( fragment && finalPhase )
			CountFragment( run, inst.Id, coverage );

		#if D_OCCLUSION_DEBUG
			DebugLate( inst, missedEarly, finalPhase, fragment, cullable, hidden, coverage );
		#endif

		if ( hidden )
			return;

		if ( missedEarly )
			Emit( run, 1, inst );

		if ( finalPhase )
		{
			Emit( run, 2, inst );

			if ( !fragment )
				InterlockedAdd( OcclusionStats[STAT_VISIBLE], 1 );
		}
	}
}
