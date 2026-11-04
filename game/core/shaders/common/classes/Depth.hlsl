#ifndef DEPTH_HLSL
#define DEPTH_HLSL

//
// garry: I don't want people having to define this shit in their shaeders when it's a backend, built in thing.
//		  My assumption is that if this isn't used, it'll get compiled out and no harm done.
//		  If I broke shaders that DO define this then I fucked it sorry - remove the definition in their file
//		   whtever you do, don't start a fucking DEPTH_TEXTYRE_DEFINED thing
//

// Create a texture without a sampler for the depth chain.
// This texture includes min/max coordinates of the depth in every mip level
Texture2D g_tDepthChain < Attribute( "DepthChainDownsample" ); SrgbRead( false ); >;

//
// Public Depth API
//
struct Depth
{
	// Returns the depth value at the given screen position from the depth.
	static float Get( float2 screenPosition )
	{
		return g_tDepthChain.Load( int3( screenPosition, 0 ) ).r;
	}

	// Normalizes a depth value to the range [0, 1] based on the viewport's min and max Z values.
	static float Normalize( float depth )
	{
		return RemapValClamped( depth, g_flViewportMinZ, g_flViewportMaxZ, 0.0, 1.0 );
	}

	// Returns the normalized depth value at the given screen position.
	static float GetNormalized( float2 screenPosition )
	{
		return Depth::Normalize( Depth::Get( screenPosition ) );
	}

	// Linearizes to view-space depth (absolute coordinates) a given normalized depth value.
	// Optionally, you can pass in the screen position to calculate linearity in oblique projection viewports.
	static float Linearize( float depth, float2 screenPosition = -1 )
    {

		// If default, center it
		if( screenPosition.x == -1 )
			screenPosition = float2( 0.5f, 0.5f );
		else
			screenPosition *= g_vInvViewportSize;

        // Create normalized device coordinates (NDC) for curvature
        float3 vNdc = float3(screenPosition, depth);
        vNdc.y = 1.0 - vNdc.y;
        vNdc.xy = 2.0f * vNdc.xy - 1.0f;

		float4 vViewPos = mul(g_matProjectionToView, float4(vNdc , 1.0f));
		
		// View-space depth is negative in the Z axis, so we need to negate it
		return -( vViewPos.z / vViewPos.w );
    }

	// Returns the linear depth value at the given screen position.
    static float GetLinear(float2 screenPosition)
    {
        return Depth::Linearize(Depth::GetNormalized(screenPosition));
    }

	// Returns the world position corresponding to a given depth and direction.
	static float3 WorldPosition( float depth, float3 direction )
	{
		float3 pos = RecoverWorldPosFromProjectedDepthAndRay( Depth::Normalize( depth ), normalize( direction ) ).xyz;
		return pos;
	}

	// Returns the world position corresponding to a given screen position.
	static float3 GetWorldPosition( float2 screenPosition )
	{
		float flDepth = Depth::GetNormalized(screenPosition);

		float3 vRay = float3(screenPosition * g_vInvViewportSize, flDepth);
		vRay.y = 1.0 - vRay.y;
		vRay.xy = 2.0f * vRay.xy - 1.0f;

        float4 vWorldPos = mul(g_matProjectionToWorld, float4(vRay, 1.0f));
        vWorldPos.xyz /= max( vWorldPos.w, 1e-10 );
		
		return vWorldPos.xyz + g_vCameraPositionWs;
	}
};
#endif