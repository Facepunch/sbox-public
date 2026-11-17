#ifndef WATER_HOLES_HLSL
#define WATER_HOLES_HLSL

// Matches WaterHole.Data: rows mapping a world position into a hole's box, which spans -1 to 1 on each axis.
struct WaterHoleData
{
	float4 X;
	float4 Y;
	float4 Z;
};

// The scene's holes, binned on world XY by WaterHoleSystem. Bucket b holds
// WaterHoleSceneIndices[WaterHoleSceneStarts[b] .. WaterHoleSceneStarts[b + 1]); the bucket after the last holds
// footprints too large to bin, which every point tests.
StructuredBuffer<WaterHoleData> WaterHoleSceneBoxes < Attribute( "WaterHoleSceneBoxes" ); >;
StructuredBuffer<uint> WaterHoleSceneStarts < Attribute( "WaterHoleSceneStarts" ); >;
StructuredBuffer<uint> WaterHoleSceneIndices < Attribute( "WaterHoleSceneIndices" ); >;
uint WaterHoleSceneCount < Attribute( "WaterHoleSceneCount" ); Default( 0 ); >;
float WaterHoleSceneInverseCell < Attribute( "WaterHoleSceneInverseCell" ); >;
uint WaterHoleSceneMask < Attribute( "WaterHoleSceneMask" ); >;

struct WaterHoles
{
	static bool BoxContains( WaterHoleData hole, float3 position )
	{
		float3 box = float3( dot( hole.X.xyz, position ) + hole.X.w, dot( hole.Y.xyz, position ) + hole.Y.w,
			dot( hole.Z.xyz, position ) + hole.Z.w );
		return all( abs( box ) <= 1 );
	}

	// Matches WaterHoleGrid.Bucket.
	static uint Bucket( float2 point, float inverseCell, uint mask )
	{
		int2 cell = (int2)clamp( floor( point * inverseCell ), -1e9, 1e9 );
		return ( asuint( cell.x ) * 73856093u ^ asuint( cell.y ) * 19349663u ) & mask;
	}

	static bool SceneBucketContains( uint bucket, float3 position )
	{
		uint end = WaterHoleSceneStarts[bucket + 1];
		for ( uint k = WaterHoleSceneStarts[bucket]; k < end; k++ )
		{
			if ( BoxContains( WaterHoleSceneBoxes[WaterHoleSceneIndices[k]], position ) )
				return true;
		}
		return false;
	}

	// Whether any of the scene's holes removes water at a world position.
	static bool SceneContains( float3 position )
	{
		if ( WaterHoleSceneCount == 0 )
			return false;

		return SceneBucketContains( Bucket( position.xy, WaterHoleSceneInverseCell, WaterHoleSceneMask ), position )
			|| SceneBucketContains( WaterHoleSceneMask + 1, position );
	}
};

#endif
