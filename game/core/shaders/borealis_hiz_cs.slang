// borealis_hiz_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md). Its
// reflection is what the backend's constants struct is checked against; it draws only with -hiz-occlusion.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Borealis two-phase Hi-Z occlusion: builds a prepass's Hi-Z from the depth its first phase drew and culls the second phase's fragments against it.";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "sbox/system.slangh" // This should always be the first include in COMMON

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_CS )
#include "sbox/view.slangh"
#include "borealis_records.fxc"

// 1: the Hi-Z's first levels from the depth (D_MSAA: multisampled); 0: its later levels, or the second phase's cull
DynamicCombo( D_HIZ_DEPTH, 0..1, Sys( ALL ) );
DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

// The depth (passes from it only; the others bind the placeholder)
#if D_MSAA
	Texture2DMS<float> g_tDepth < Attribute( "BorealisHiZDepth" ); >;
#else
	Texture2D<float> g_tDepth < Attribute( "BorealisHiZDepth" ); >;
#endif

// The Hi-Z (BackendDevice.VisibilityView.HiZ): its levels one after another, a float per texel
RWByteAddressBuffer g_HiZ < Attribute( "BorealisHiZ" ); >;

// The second phase: the call's region (candidates in; instance records, commands, multi-draw counts, fragment words and
// listed fragments out), the view's fragment stamps and meshlet stamps
RWByteAddressBuffer g_Frame < Attribute( "BorealisCullFrame" ); >;
RWByteAddressBuffer g_Stamps < Attribute( "BorealisCullStamps" ); >;
RWByteAddressBuffer g_MeshletStamps < Attribute( "BorealisMeshletStamps" ); >;

// Constants: the float members are g_n-named too, as the backend's checker reads one prefix
cbuffer BorealisHiZ_t
{
	uint g_nHiZPass;			// 0: levels from the depth (from level 0), 1: levels from g_nHiZSource, 2: the second phase's cull
	uint g_nHiZWidth;			// level 0's size: a texel per 4x4 pixels
	uint g_nHiZHeight;
	uint g_nHiZLevels;
	uint g_nHiZSource;			// pass 1: the level read
	uint g_nHiZDepthWidth;		// pass 0: the depth target's size and samples
	uint g_nHiZDepthHeight;
	uint g_nHiZSamples;
	uint g_nHiZCandidates;		// pass 2: byte offset of the candidates: groups, 1, 1, count, occluded, then from byte 32 64 bytes each
	uint g_nHiZRuns;			// byte offset of the second phase's multi-draws: first command slot and count each
	uint g_nHiZListed;			// byte offset of the first phase's listed fragments (for the meshlet pass), or 0
	uint g_nHiZListed2;			// byte offset of the second phase's: dispatch arguments and the byte offset of its first entry
	uint g_nHiZInstances;		// byte offset of the instance records
	uint g_nHiZCommands;		// byte offset of the commands
	uint g_nHiZFirstInstance;	// the first instance record's index in g_Frame
	uint g_nHiZFragmentWords;	// g_Frame byte offset of instance 0's fragment word for the ids (wraps)
	uint g_nHiZFrame;			// this frame's stamp
	uint g_nHiZFlags;			// 1: test against the Hi-Z (else every candidate draws), 2: write meshlet stamps
	uint g_nHiZPad0;
	uint g_nHiZPad1;
	float4 g_nHiZViewport;		// width and height in pixels, and the depth range the view draws into (its minimum and maximum)
	float4 g_nHiZClipX;			// world to clip: x, y, z, w = dot( row, float4( world, 1 ) ), y before the vertex shaders negate it
	float4 g_nHiZClipY;
	float4 g_nHiZClipZ;
	float4 g_nHiZClipW;
};

static const uint None = 0xffffffffu;

// A level's size and its first texel in g_HiZ: each level half the last, rounded up
uint2 LevelSize( uint nLevel )
{
	uint2 v = uint2( g_nHiZWidth, g_nHiZHeight );
	for ( uint l = 0; l < nLevel; l++ )
		v = ( v + 1 ) >> 1;
	return v;
}

uint LevelOffset( uint nLevel )
{
	uint2 v = uint2( g_nHiZWidth, g_nHiZHeight );
	uint nOffset = 0;
	for ( uint l = 0; l < nLevel; l++ )
	{
		nOffset += v.x * v.y;
		v = ( v + 1 ) >> 1;
	}
	return nOffset;
}

float LoadHiZ( uint nOffset, uint2 vSize, uint2 vTexel )
{
	vTexel = min( vTexel, vSize - 1 );
	return asfloat( g_HiZ.Load( ( nOffset + vTexel.y * vSize.x + vTexel.x ) * 4 ) );
}

//
// The build: a 16x16 group reads 16x16 values, each the farthest (reverse-Z: least) of a pixel's samples (pass 0, two levels
// below level 0's 4x4 pixels) or of 2x2 texels of the level before (pass 1, level g_nHiZSource + 1), then halves them four
// times in group memory, writing each level from 0 on. A value past an edge reads the last row or column again, which the
// texel over it covers anyway
//
groupshared float gs_Depth[256];

void Build( uint2 vGroup, uint2 vThread )
{
	int nFirst = g_nHiZPass == 0 ? -2 : int( g_nHiZSource ) + 1;
	uint2 vTexel = vGroup * 16 + vThread;
	float flFar = 1;

	#if D_HIZ_DEPTH
		int2 vPixel = int2( min( vTexel, uint2( g_nHiZDepthWidth, g_nHiZDepthHeight ) - 1 ) );
		#if D_MSAA
			[unroll]
			for ( uint s = 0; s < 8; s++ )
			{
				if ( s < g_nHiZSamples )
					flFar = min( flFar, g_tDepth.Load( vPixel, s ) );
			}
		#else
			flFar = g_tDepth.Load( int3( vPixel, 0 ) );
		#endif
	#else
		uint2 vSourceSize = LevelSize( g_nHiZSource );
		uint nSource = LevelOffset( g_nHiZSource );
		flFar = min( min( LoadHiZ( nSource, vSourceSize, vTexel * 2 ), LoadHiZ( nSource, vSourceSize, vTexel * 2 + uint2( 1, 0 ) ) ),
			min( LoadHiZ( nSource, vSourceSize, vTexel * 2 + uint2( 0, 1 ) ), LoadHiZ( nSource, vSourceSize, vTexel * 2 + uint2( 1, 1 ) ) ) );

		uint2 vSize = LevelSize( uint( nFirst ) );
		if ( all( vTexel < vSize ) )
			g_HiZ.Store( ( LevelOffset( uint( nFirst ) ) + vTexel.y * vSize.x + vTexel.x ) * 4, asuint( flFar ) );
	#endif

	gs_Depth[vThread.y * 16 + vThread.x] = flFar;
	GroupMemoryBarrierWithGroupSync();

	for ( uint k = 1; k < 5 && nFirst + int( k ) < int( g_nHiZLevels ); k++ )
	{
		uint nSide = 16u >> k;
		bool bActive = all( vThread < nSide );
		float flLevel = 1;
		if ( bActive )
		{
			// The level before's 2x2 texels, at stride 16 in group memory
			uint nAt = vThread.y * 2 * 16 + vThread.x * 2;
			flLevel = min( min( gs_Depth[nAt], gs_Depth[nAt + 1] ), min( gs_Depth[nAt + 16], gs_Depth[nAt + 17] ) );
		}
		GroupMemoryBarrierWithGroupSync();

		int nLevel = nFirst + int( k );
		if ( bActive )
		{
			gs_Depth[vThread.y * 16 + vThread.x] = flLevel;
			uint2 vAt = vGroup * nSide + vThread;
			uint2 vLevelSize = LevelSize( uint( max( nLevel, 0 ) ) );
			if ( nLevel >= 0 && all( vAt < vLevelSize ) )
				g_HiZ.Store( ( LevelOffset( uint( nLevel ) ) + vAt.y * vLevelSize.x + vAt.x ) * 4, asuint( flLevel ) );
		}
		GroupMemoryBarrierWithGroupSync();
	}
}

//
// The second phase: a thread per candidate the first phase's culls deferred (an ids fragment forward didn't see last
// frame, inside the view). Its box's nearest depth against the farthest of the Hi-Z texels under its screen rect, at the
// level where the rect spans at most 2x2; a box reaching the near plane passes
//
bool Occluded( float3 vCenter, float3 vExtents )
{
	float flNear = 0;
	float2 vMin = 1e30;
	float2 vMax = -1e30;
	[unroll]
	for ( uint k = 0; k < 8; k++ )
	{
		float4 v = float4( vCenter + vExtents * float3( ( k & 1 ) != 0 ? 1 : -1, ( k & 2 ) != 0 ? 1 : -1, ( k & 4 ) != 0 ? 1 : -1 ), 1 );
		float4 vClip = float4( dot( g_nHiZClipX, v ), dot( g_nHiZClipY, v ), dot( g_nHiZClipZ, v ), dot( g_nHiZClipW, v ) );
		if ( !( vClip.w > 1e-4 ) )
			return false;

		flNear = max( flNear, vClip.z / vClip.w );
		float2 vPixel = float2( vClip.x / vClip.w * 0.5 + 0.5, 0.5 - vClip.y / vClip.w * 0.5 ) * g_nHiZViewport.xy;
		vMin = min( vMin, vPixel );
		vMax = max( vMax, vPixel );
	}

	// Reverse depth: the near plane is z = w. The depth the Hi-Z holds is in the viewport's depth range (the world's leaves
	// room for the skybox's)
	if ( flNear >= 1 )
		return false;
	flNear = g_nHiZViewport.z + flNear * ( g_nHiZViewport.w - g_nHiZViewport.z );

	vMin = max( vMin, 0 );
	vMax = min( vMax, g_nHiZViewport.xy - 1 );
	if ( any( vMin > vMax ) )
		return false;

	uint2 vLow = uint2( vMin ) / 4;
	uint2 vHigh = uint2( vMax ) / 4;
	uint nLevel = 0;
	while ( nLevel + 1 < g_nHiZLevels && any( ( vHigh >> nLevel ) - ( vLow >> nLevel ) > 1 ) )
		nLevel++;

	uint2 vSize = LevelSize( nLevel );
	uint nOffset = LevelOffset( nLevel );
	uint2 a = vLow >> nLevel;
	uint2 b = vHigh >> nLevel;
	float flFar = min( min( LoadHiZ( nOffset, vSize, a ), LoadHiZ( nOffset, vSize, uint2( b.x, a.y ) ) ),
		min( LoadHiZ( nOffset, vSize, uint2( a.x, b.y ) ), LoadHiZ( nOffset, vSize, b ) ) );
	return flNear < flFar;
}

uint TakeSlot( uint nRun )
{
	uint nIndex;
	g_Frame.InterlockedAdd( g_nHiZRuns + nRun * MultiDraw_Size + MultiDraw_Count, 1, nIndex );
	return g_Frame.Load( g_nHiZRuns + nRun * MultiDraw_Size + MultiDraw_FirstSlot ) + nIndex;
}

// A candidate (HiZCandidate): centre and transform slot; extents and draw record; first index, index count, base vertex and
// the vertices' first bit; visibility fragment, multi-draw, compacted multi-draw and flags (1: the meshlet pass culls it)
void Cull( uint nCandidate )
{
	if ( nCandidate >= g_Frame.Load( g_nHiZCandidates + HiZCandidates_Count ) )
		return;

	uint nAt = g_nHiZCandidates + HiZCandidates_Size + nCandidate * HiZCandidate_Size;
	uint4 c0 = uint4( g_Frame.Load3( nAt + HiZCandidate_Center ), g_Frame.Load( nAt + HiZCandidate_Transform ) );
	uint4 c1 = uint4( g_Frame.Load3( nAt + HiZCandidate_Extents ), g_Frame.Load( nAt + HiZCandidate_Draw ) );
	uint4 c2 = uint4( g_Frame.Load( nAt + HiZCandidate_FirstIndex ), g_Frame.Load( nAt + HiZCandidate_IndexCount ), g_Frame.Load( nAt + HiZCandidate_BaseVertex ), g_Frame.Load( nAt + HiZCandidate_VertexBase ) );
	uint4 c3 = uint4( g_Frame.Load( nAt + HiZCandidate_Fragment ), g_Frame.Load( nAt + HiZCandidate_Run ), g_Frame.Load( nAt + HiZCandidate_CompactRun ), g_Frame.Load( nAt + HiZCandidate_Meshlets ) );
	uint nFragment = c3.x;

	// Occluded: stamped all the same, so forward lists it and its compaction finds nothing (and, where the view's prepass
	// culls meshlets, no kept triangles to scan)
	g_Stamps.Store( nFragment * 4, g_nHiZFrame );
	if ( ( g_nHiZFlags & 1 ) != 0 && Occluded( asfloat( c0.xyz ), asfloat( c1.xyz ) ) )
	{
		if ( ( g_nHiZFlags & 2 ) != 0 )
			g_MeshletStamps.Store4( nFragment * 16, uint4( g_nHiZFrame, None, 0, 0 ) );
		uint nOccluded;
		g_Frame.InterlockedAdd( g_nHiZCandidates + HiZCandidates_Occluded, 1, nOccluded );
		return;
	}

	if ( ( c3.w & 1 ) != 0 )
	{
		// Listed for the second meshlet pass after the first phase's entries: its arguments' fourth word is where they start
		uint nFirst = g_Frame.Load( g_nHiZListed + ListedArguments_GroupsX );
		uint nIndex;
		g_Frame.InterlockedAdd( g_nHiZListed2 + ListedArguments_GroupsX, 1, nIndex );
		g_Frame.Store( g_nHiZListed2 + ListedArguments_Entries, ( g_nHiZListed + ListedArguments_Size + nFirst * ListedFragment_Size ) - ( g_nHiZListed2 + ListedArguments_Size ) );
		uint nEntry = g_nHiZListed + ListedArguments_Size + ( nFirst + nIndex ) * ListedFragment_Size;
		g_Frame.Store( nEntry + ListedFragment_Transform, c0.w );
		g_Frame.Store( nEntry + ListedFragment_Draw, c1.w );
		g_Frame.Store( nEntry + ListedFragment_VertexBase, c2.w );
		g_Frame.Store( nEntry + ListedFragment_Run, c3.y );
		g_Frame.Store( nEntry + ListedFragment_CompactRun, c3.z );
		g_Frame.Store( nEntry + ListedFragment_Reserved0, 0 );
		g_Frame.Store( nEntry + ListedFragment_Reserved1, 0 );
		g_Frame.Store( nEntry + ListedFragment_Fragment, nFragment );
		return;
	}

	uint nSlot = TakeSlot( c3.y );
	g_Frame.Store( g_nHiZInstances + nSlot * InstanceRecord_Size + InstanceRecord_Transform, c0.w );
	g_Frame.Store( g_nHiZInstances + nSlot * InstanceRecord_Size + InstanceRecord_Draw, c1.w );
	uint nCommand = g_nHiZCommands + nSlot * 20;
	g_Frame.Store4( nCommand, uint4( c2.y, 1, c2.x, c2.z ) );
	g_Frame.Store( nCommand + 16, g_nHiZFirstInstance + nSlot );
	g_Frame.Store( g_nHiZFragmentWords + ( g_nHiZFirstInstance + nSlot ) * 4, c2.w );
}

[numthreads( 16, 16, 1 )]
[shader( "compute" )] void MainCs( uint3 vGroupId : SV_GroupID, uint3 vThreadId : SV_GroupThreadID, uint nThread : SV_GroupIndex )
{
	if ( g_nHiZPass == 2 )
	{
		Cull( vGroupId.x * 256 + nThread );
		return;
	}

	Build( vGroupId.xy, vThreadId.xy );
}

#endif // CS
