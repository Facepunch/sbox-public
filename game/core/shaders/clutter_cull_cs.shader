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
	StructuredBuffer<TransformBufferData_t> AllInstances < Attribute( "AllInstances" ); >;
	StructuredBuffer<float4> AllInstanceSpheres < Attribute( "AllInstanceSpheres" ); >;
	int InstanceCount < Attribute( "InstanceCount" ); >;

	float ClutterModelRadius < Attribute( "ClutterModelRadius" ); >;

	// Survivors per LOD - each survivor appends its full transform to its bucket.
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod0 < Attribute( "VisibleLod0" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod1 < Attribute( "VisibleLod1" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod2 < Attribute( "VisibleLod2" ); >;
	AppendStructuredBuffer<TransformBufferData_t> VisibleLod3 < Attribute( "VisibleLod3" ); >;

	float3 ClutterLodCameraPos     < Attribute( "ClutterLodCameraPos" ); >;
	float  ClutterLodTanHalfFov    < Attribute( "ClutterLodTanHalfFov" ); >;
	float  ClutterLodViewportWidth < Attribute( "ClutterLodViewportWidth" ); >;
	int    ClutterLodCount         < Attribute( "ClutterLodCount" ); >;
	StructuredBuffer<float> ClutterLodSwitchDistances < Attribute( "ClutterLodSwitchDistances" ); >;

	// Debug: >1 narrows the cull frustum so culling is visible on-screen.
	float ClutterFrustumScale < Attribute( "ClutterFrustumScale" ); >;

	// Plain float4x4: attribute matrices are stored raw and read column-major, so the CPU uploads
	// the transpose and we consume it as mul( M, pos ). A row_major qualifier here breaks the planes.
	float4x4 ClutterWorldToProjection < Attribute( "ClutterWorldToProjection" ); >;

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
	int ComputeLod( float3 worldPos, float scale )
	{
		float dist = length( worldPos - ClutterLodCameraPos );
		float tanHalf = max( ClutterLodTanHalfFov, 1e-5 );
		float screen = saturate( 0.5 / max( dist * tanHalf, 1e-5 ) );
		float pixels = screen * ClutterLodViewportWidth;
		float metric = ( pixels > 0.0 ) ? ( 50.0 / pixels ) : 0.0;

		int lod = max( ClutterLodCount - 1, 0 );
		[loop]
		while ( lod > 0 )
		{
			float d = ClutterLodSwitchDistances[lod] * scale;
			if ( d > 0.0 && d < metric )
				break;
			lod--;
		}
		return lod;
	}

	void AppendLod( int lod, TransformBufferData_t t )
	{
		switch ( lod )
		{
			case 0: VisibleLod0.Append( t ); break;
			case 1: VisibleLod1.Append( t ); break;
			case 2: VisibleLod2.Append( t ); break;
			default: VisibleLod3.Append( t ); break;
		}
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		uint id = vThreadId.x;
		if ( id >= (uint)InstanceCount )
			return;

		float4 sphere = AllInstanceSpheres[id]; // xyz = world center, w = world radius

		if ( !SphereInFrustum( sphere.xyz, sphere.w ) )
			return;

		// Only the full transform is fetched once an instance survives.
		float scale = ( ClutterModelRadius > 1e-6 ) ? ( sphere.w / ClutterModelRadius ) : 1.0;
		int lod = ComputeLod( sphere.xyz, scale );

		AppendLod( lod, AllInstances[id] );
	}
}
