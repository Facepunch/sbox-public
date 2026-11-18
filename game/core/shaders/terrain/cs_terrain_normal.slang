// cs_terrain_normal: a standalone module, the whole shader for Borealis.Gpu (docs/managed/standalone-modules.md).
// HEADER: DevShader = true;
// HEADER: Description = "Bake terrain geometric normal from the heightmap";

#include "sbox/pass.slangh"

Mode( Default );

#include "system.fxc" // This should always be the first include in COMMON

#if ( PROGRAM == VFX_PROGRAM_CS )
#include "common.fxc"

RWTexture2D<float>  Heightmap < Attribute( "Heightmap" ); >;   // R16, normalized height [0,1]
RWTexture2D<float2> OutputMap < Attribute( "OutputMap" ); >;   // RG = normal.xy * 0.5 + 0.5

float HeightScale   < Attribute( "HeightScale" ); >;   // world units for full [0,1] height
float UnitsPerTexel < Attribute( "UnitsPerTexel" ); >; // world units between texels

float LoadHeight( int2 c, int w, int h )
{
	return Heightmap.Load( clamp( c, int2( 0, 0 ), int2( w - 1, h - 1 ) ) ).x;
}

[numthreads( 16, 16, 1 )]
[shader( "compute" )] void MainCs( uint3 vThreadId : SV_DispatchThreadID )
{
	uint uw, uh;
	Heightmap.GetDimensions( uw, uh );
	int w = (int)uw, h = (int)uh;

	int2 p = int2( vThreadId.xy );
	if ( p.x >= w || p.y >= h )
		return;

	// Geometric normal from central differences (edges clamped).
	float hl = LoadHeight( int2( p.x - 1, p.y ), w, h );
	float hr = LoadHeight( int2( p.x + 1, p.y ), w, h );
	float ht = LoadHeight( int2( p.x, p.y - 1 ), w, h );
	float hb = LoadHeight( int2( p.x, p.y + 1 ), w, h );

	float zScale = UnitsPerTexel / max( HeightScale, 1e-4f );
	float3 n = normalize( float3( hl - hr, ht - hb, zScale ) );

	OutputMap[p] = n.xy * 0.5f + 0.5f;
}

#endif // CS

