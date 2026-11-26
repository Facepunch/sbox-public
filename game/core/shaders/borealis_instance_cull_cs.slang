// borealis_instance_cull_cs: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// HEADER: DevShader = true;
// HEADER: Description = "Borealis: culls a Graphics.DrawModelInstanced draw's instances against its view";

#include "sbox/pass.slangh"

Mode( Default );

#include "sbox/system.slangh"

#if ( PROGRAM == VFX_PROGRAM_CS )
// One TransformBufferData_t: rows of a 3x4 matrix, then alpha, tint, vertex cache offset and blend weight count
struct InstanceCullTransform
{
	float4 Row0;
	float4 Row1;
	float4 Row2;
	uint4 Extra;
};

// The draw's transforms from CullFirst, CullCount of them; the kept ones are written in order from CullKeptFirst
StructuredBuffer<InstanceCullTransform> CullTransforms < Attribute( "CullTransforms" ); >;
RWStructuredBuffer<InstanceCullTransform> CullKept < Attribute( "CullKept" ); >;

// Its indirect commands (five 32-bit values each) from byte CullArgumentsOffset: each one's instance count is set to the kept count
RWByteAddressBuffer CullArguments < Attribute( "CullArguments" ); >;

float CullFirst < Attribute( "CullFirst" ); >;
float CullCount < Attribute( "CullCount" ); >;
float CullKeptFirst < Attribute( "CullKeptFirst" ); >;
float CullArgumentsOffset < Attribute( "CullArgumentsOffset" ); >;
float CullCommands < Attribute( "CullCommands" ); >;

// The model's bounds in its own space, centre and extents
float3 CullBoundsCenter < Attribute( "CullBoundsCenter" ); >;
float3 CullBoundsExtents < Attribute( "CullBoundsExtents" ); >;

// The view's side planes (left, right, bottom, top), unnormalized: a box is outside one when all of it is behind it
float4 CullPlane0 < Attribute( "CullPlane0" ); >;
float4 CullPlane1 < Attribute( "CullPlane1" ); >;
float4 CullPlane2 < Attribute( "CullPlane2" ); >;
float4 CullPlane3 < Attribute( "CullPlane3" ); >;

bool Outside( float4 plane, float3 center, float3 extents )
{
	return dot( plane.xyz, center ) + plane.w + dot( extents, abs( plane.xyz ) ) < 0;
}

// The instance's bounds as a world box (the model's box transformed, as RenderWorld bounds an object), tested as the view
// tests objects
bool Visible( InstanceCullTransform t )
{
	float3x3 m = float3x3( t.Row0.xyz, t.Row1.xyz, t.Row2.xyz );
	float3 center = mul( m, CullBoundsCenter ) + float3( t.Row0.w, t.Row1.w, t.Row2.w );
	float3 extents = mul( abs( m ), CullBoundsExtents );
	return !Outside( CullPlane0, center, extents ) && !Outside( CullPlane1, center, extents )
		&& !Outside( CullPlane2, center, extents ) && !Outside( CullPlane3, center, extents );
}

#define CULL_GROUP 256
groupshared uint g_Scan[CULL_GROUP];

// One group for the draw, a block of instances at a time: each block's kept instances go after the ones before, in order
[numthreads( CULL_GROUP, 1, 1 )]
[shader( "compute" )] void MainCs( uint3 vThread : SV_GroupThreadID )
{
	uint lane = vThread.x;
	uint count = (uint)CullCount;
	uint first = (uint)CullFirst;
	uint keptFirst = (uint)CullKeptFirst;
	uint kept = 0;

	for ( uint block = 0; block < count; block += CULL_GROUP )
	{
		uint i = block + lane;
		InstanceCullTransform t = (InstanceCullTransform)0;
		bool keep = false;
		if ( i < count )
		{
			t = CullTransforms[first + i];
			keep = Visible( t );
		}

		// Inclusive scan of the kept flags across the block
		g_Scan[lane] = keep ? 1u : 0u;
		GroupMemoryBarrierWithGroupSync();
		[unroll]
		for ( uint step = 1; step < CULL_GROUP; step <<= 1 )
		{
			uint before = lane >= step ? g_Scan[lane - step] : 0u;
			GroupMemoryBarrierWithGroupSync();
			g_Scan[lane] += before;
			GroupMemoryBarrierWithGroupSync();
		}

		if ( keep ) CullKept[keptFirst + kept + g_Scan[lane] - 1] = t;
		kept += g_Scan[CULL_GROUP - 1];
		GroupMemoryBarrierWithGroupSync();
	}

	uint commands = (uint)CullCommands;
	uint offset = (uint)CullArgumentsOffset;
	for ( uint c = lane; c < commands; c += CULL_GROUP )
		CullArguments.Store( offset + c * 20 + 4, kept );
}

#endif // CS
