//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "Basalt GPU-driven visibility: marks every triangle that owns a sample of the prepass's ids.";
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

	DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );

	#ifndef BINDLESS_VERTEX_BUFFERS_DECLARED
	#define BINDLESS_VERTEX_BUFFERS_DECLARED
	ByteAddressBuffer g_bindless_VertexBuffers[] EXTERNAL_DESC_SET( t, g_globalLateBoundBindlessSet, 17 );
	#endif

	#if D_MSAA
		Texture2DMS<uint> g_tIds < Attribute( "BasaltIds" ); >;
	#else
		Texture2D<uint> g_tIds < Attribute( "BasaltIds" ); >;
	#endif

	RWByteAddressBuffer g_Mask < Attribute( "BasaltCullMask" ); >;

	cbuffer BasaltGpuDrivenMark_t
	{
		uint g_nMarkWidth;
		uint g_nMarkHeight;
		uint g_nMarkSamples;
		uint g_nMarkVisibility;	// bindless slot of each visibility fragment's first triangle bit
		uint g_nMarkFragments;	// visibility fragments there are
		uint g_nMarkPad0;
		uint g_nMarkPad1;
		uint g_nMarkPad2;
	};

	// Every distinct id of a pixel's samples sets its triangle's bit: (fragment + 1) << 16 | triangle, 0 for nothing
	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		if ( vThreadId.x >= g_nMarkWidth || vThreadId.y >= g_nMarkHeight )
			return;

		uint ids[8];
		uint nSamples = min( g_nMarkSamples, 8u );
		for ( uint s = 0; s < nSamples; s++ )
		{
			#if D_MSAA
				uint v = g_tIds.Load( int2( vThreadId.xy ), s );
			#else
				uint v = g_tIds.Load( int3( vThreadId.xy, 0 ) );
			#endif

			ids[s] = v;
			bool bSeen = v == 0;
			for ( uint j = 0; j < s; j++ )
				bSeen = bSeen || ids[j] == v;
			if ( bSeen )
				continue;

			uint nFragment = ( v >> 16 ) - 1;
			if ( nFragment >= g_nMarkFragments )
				continue;

			uint nBit = g_bindless_VertexBuffers[ g_nMarkVisibility ].Load( nFragment * 4 );
			if ( nBit == 0xffffffffu )
				continue;

			nBit += v & 0xffff;
			g_Mask.InterlockedOr( ( nBit >> 5 ) * 4, 1u << ( nBit & 31 ) );
		}
	}
}
