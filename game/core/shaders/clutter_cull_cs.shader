HEADER
{
	DevShader = true;
	Description = "Clutter GPU frustum culling";
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
	#include "transform_buffer.fxc" // TransformBufferData_t
}

CS
{
	// Every batch in the scene is culled in one go per view:
	//   0 = count: frustum + LOD test per instance, counts survivors per batch LOD
	//   1 = args:  prefix sums the counts into each batch LOD's range of the visible buffer and its draw arguments
	//   2 = scatter: copies each survivor into its batch LOD's range
	DynamicCombo( D_PASS, 0..2, Sys( ALL ) );

	struct ClutterBatch_t
	{
		float4 LodSwitchDistances;
		uint4 ArgsBase;  // First indirect draw entry per LOD
		uint4 DrawCount; // Indirect draw entries (one per material) per LOD
		float ModelRadius;
		uint LodCount;   // 0 for an unused batch slot
		uint CastShadows;
		uint Padding;
	};

	struct DrawIndexedArguments_t
	{
		uint IndexCount;
		uint InstanceCount;
		uint FirstIndex;
		int BaseVertex;
		uint FirstInstance;
	};

	StructuredBuffer<TransformBufferData_t> AllInstances < Attribute( "AllInstances" ); >;
	StructuredBuffer<float4> AllInstanceSpheres < Attribute( "AllInstanceSpheres" ); >;
	StructuredBuffer<uint> AllInstanceBatches < Attribute( "AllInstanceBatches" ); >;
	StructuredBuffer<ClutterBatch_t> ClutterBatches < Attribute( "ClutterBatches" ); >;
	int InstanceCount < Attribute( "InstanceCount" ); >;
	int BatchCount < Attribute( "BatchCount" ); >;

	// Survivors per batch LOD (count pass), then each range's write cursor (scatter pass).
	RWStructuredBuffer<uint> ClutterCounts < Attribute( "ClutterCounts" ); >;
	// Each instance's LOD from the count pass, or ~0 if culled.
	RWStructuredBuffer<uint> ClutterInstanceLods < Attribute( "ClutterInstanceLods" ); >;
	RWStructuredBuffer<DrawIndexedArguments_t> ClutterArgs < Attribute( "ClutterArgs" ); >;
	RWStructuredBuffer<TransformBufferData_t> ClutterVisible < Attribute( "ClutterVisible" ); >;

	float3 ClutterLodCameraPos     < Attribute( "ClutterLodCameraPos" ); >;
	float  ClutterLodTanHalfFov    < Attribute( "ClutterLodTanHalfFov" ); >;
	float  ClutterLodViewportWidth < Attribute( "ClutterLodViewportWidth" ); >;
	float  ClutterLodOrthoWidth    < Attribute( "ClutterLodOrthoWidth" ); >;

	// Debug: >1 narrows the cull frustum so culling is visible on-screen.
	float ClutterFrustumScale < Attribute( "ClutterFrustumScale" ); >;

	// Instances whose bounds are entirely farther than this from the LOD camera are dropped. 0 disables the limit.
	float ClutterMaxDistance < Attribute( "ClutterMaxDistance" ); >;

	// Shadow views skip batches that don't cast shadows.
	int CullShadowCasters < Attribute( "CullShadowCasters" ); >;

	// Plain float4x4: attribute matrices are stored raw and read column-major, so the CPU uploads
	// the transpose and we consume it as mul( M, pos ). A row_major qualifier here breaks the planes.
	float4x4 ClutterWorldToProjection < Attribute( "ClutterWorldToProjection" ); >;

	static const uint Culled = 0xFFFFFFFF;

	// Bounding-sphere frustum test - cheaper and slightly looser than an 8-corner OBB test.
	bool SphereInFrustum( float3 center, float radius )
	{
		float4x4 vp = ClutterWorldToProjection;

		bool ortho = abs( vp[3].x ) < 1e-5 && abs( vp[3].y ) < 1e-5 && abs( vp[3].z ) < 1e-5;
		float4 keepAll = float4( 0, 0, 0, 1 );

		// Scaling the X/Y rows narrows the cull frustum.
		float frustumScale = max( ClutterFrustumScale, 1e-3 );
		float4 rowX = vp[0] * frustumScale;
		float4 rowY = vp[1] * frustumScale;

		float4 planes[6] =
		{
			vp[3] + rowX, // left
			vp[3] - rowX, // right
			vp[3] + rowY, // bottom
			vp[3] - rowY, // top
			ortho ? keepAll : vp[2],             // near
			ortho ? keepAll : ( vp[3] - vp[2] ), // far
		};

		[unroll]
		for ( int i = 0; i < 6; i++ )
		{
			// Normalize for a true signed distance; the keepAll plane has a zero normal, so skip it.
			float len = length( planes[i].xyz );
			if ( len < 1e-6 )
				continue;

			float dist = ( dot( planes[i].xyz, center ) + planes[i].w ) / len;
			if ( dist < -radius )
				return false;
		}
		return true;
	}

	// Matches the native LOD metric: screen coverage of a 0.5-radius sphere, then walk switch distances.
	uint ComputeLod( float3 worldPos, float scale, ClutterBatch_t batch )
	{
		float dist = length( worldPos - ClutterLodCameraPos );
		float tanHalf = max( ClutterLodTanHalfFov, 1e-5 );
		// Orthographic coverage depends on the view width, not distance from the camera.
		float screen = ClutterLodOrthoWidth > 0.0
			? saturate( 1.0 / ClutterLodOrthoWidth )
			: saturate( 0.5 / max( dist * tanHalf, 1e-5 ) );
		float pixels = screen * ClutterLodViewportWidth;
		float metric = ( pixels > 0.0 ) ? ( 50.0 / pixels ) : 0.0;

		int lod = max( (int)batch.LodCount - 1, 0 );
		[loop]
		while ( lod > 0 )
		{
			float d = batch.LodSwitchDistances[lod] * scale;
			if ( d > 0.0 && d < metric )
				break;
			lod--;
		}
		return (uint)lod;
	}

	uint CullInstance( uint id )
	{
		float4 sphere = AllInstanceSpheres[id]; // xyz = world center, w = world radius
		if ( sphere.w < 0.0 ) // Free slot.
			return Culled;

		uint batchIndex = AllInstanceBatches[id];
		ClutterBatch_t batch = ClutterBatches[batchIndex];

		if ( CullShadowCasters != 0 && batch.CastShadows == 0 )
			return Culled;

		if ( !SphereInFrustum( sphere.xyz, sphere.w ) )
			return Culled;

		if ( ClutterMaxDistance > 0.0 && length( sphere.xyz - ClutterLodCameraPos ) - sphere.w > ClutterMaxDistance )
			return Culled;

		float scale = ( batch.ModelRadius > 1e-6 ) ? ( sphere.w / batch.ModelRadius ) : 1.0;
		return ComputeLod( sphere.xyz, scale, batch );
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		uint id = vThreadId.x;

	#if ( D_PASS == 0 )
		if ( id >= (uint)InstanceCount )
			return;

		uint lod = CullInstance( id );
		ClutterInstanceLods[id] = lod;

		if ( lod != Culled )
			InterlockedAdd( ClutterCounts[AllInstanceBatches[id] * 4 + lod], 1u );

	#elif ( D_PASS == 1 )
		// A few dozen batch LODs: one thread lays them out back to back.
		if ( id != 0 )
			return;

		uint first = 0;
		for ( uint b = 0; b < (uint)BatchCount; b++ )
		{
			ClutterBatch_t batch = ClutterBatches[b];
			for ( uint lod = 0; lod < batch.LodCount; lod++ )
			{
				uint counter = b * 4 + lod;
				uint count = ClutterCounts[counter];

				for ( uint d = 0; d < batch.DrawCount[lod]; d++ )
				{
					uint entry = batch.ArgsBase[lod] + d;
					ClutterArgs[entry].InstanceCount = count;
					ClutterArgs[entry].FirstInstance = first;
				}

				// The scatter pass appends from here.
				ClutterCounts[counter] = first;
				first += count;
			}
		}

	#else
		if ( id >= (uint)InstanceCount )
			return;

		uint lod = ClutterInstanceLods[id];
		if ( lod == Culled )
			return;

		uint slot;
		InterlockedAdd( ClutterCounts[AllInstanceBatches[id] * 4 + lod], 1u, slot );
		ClutterVisible[slot] = AllInstances[id];
	#endif
	}
}
