#ifndef NORMALS_H
#define NORMALS_H

//-----------------------------------------------------------------------------
// Transform a normal from tangent space to world space
//-----------------------------------------------------------------------------

float3 TransformNormal( float3 vNormalTs, float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
{
    vTangentUWs = normalize( vTangentUWs.xyz );
    vTangentVWs = normalize( vTangentVWs.xyz );

    // HACK: Tools still generate tangent space the inverted Source1 way where positive y is down. Flipping the normal here to compensate.
    vNormalTs.y = -vNormalTs.y;

    // Transform from tangent space into world space
    return Vec3TsToWsNormalized( vNormalTs.xyz, vNormalWs, vTangentUWs.xyz, vTangentVWs.xyz );
}

/// <summary>
/// Reconstructs a normal in the lighting combiner's tangent basis when no geometric normal is available.
/// Assumes an orthonormal basis and a normal in its positive hemisphere. The normal-map Y flip
/// has already been applied by TransformNormal and must not be applied again here.
/// </summary>
float3 NormalWorldToTangent( float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
{
	#if ( PS_INPUT_HAS_TANGENT_BASIS )
		vNormalWs = NormalizeSafe( vNormalWs );
		float2 vNormalXY = float2(
			dot( vNormalWs, NormalizeSafe( vTangentUWs ) ),
			dot( vNormalWs, NormalizeSafe( vTangentVWs ) ) );

		return NormalizeSafe( float3( vNormalXY, sqrt( saturate( 1.0 - dot( vNormalXY, vNormalXY ) ) ) ) );
	#else
		return float3( 0, 0, 1 );
	#endif
}

/// <summary>
/// Converts a world normal into the lighting combiner's tangent basis using the geometric normal
/// preserved before normal mapping. Handles mirrored and nonorthogonal interpolated bases.
/// A zero geometric normal uses the compatibility overload's positive-hemisphere reconstruction.
/// </summary>
float3 NormalWorldToTangent( float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs, float3 vGeometricNormalWs )
{
	#if ( PS_INPUT_HAS_TANGENT_BASIS )
		// Match TransformNormal's basis, including its independently normalized tangents.
		vTangentUWs = NormalizeSafe( vTangentUWs );
		vTangentVWs = NormalizeSafe( vTangentVWs );

		float3 vCofactorU = cross( vTangentVWs, vGeometricNormalWs );
		float3 vCofactorV = cross( vGeometricNormalWs, vTangentUWs );
		float3 vCofactorN = cross( vTangentUWs, vTangentVWs );
		float flDeterminant = dot( vTangentUWs, vCofactorU );

		if ( abs( flDeterminant ) < 0.000001 )
		{
			return NormalWorldToTangent( vNormalWs, vTangentUWs, vTangentVWs );
		}

		float3 vNormalTs = float3(
			dot( vNormalWs, vCofactorU ),
			dot( vNormalWs, vCofactorV ),
			dot( vNormalWs, vCofactorN ) );

		// Normalization cancels the determinant's magnitude, but mirrored UVs still need its sign.
		return NormalizeSafe( vNormalTs * sign( flDeterminant ) );
	#else
		return float3( 0, 0, 1 );
	#endif
}

//-----------------------------------------------------------------------------
// Scales and shifts the value range from [0, 1] to [-1, 1] and normalizes it
//-----------------------------------------------------------------------------

float3 DecodeNormal( float3 vEncodedNormal )
{
    return normalize( 2.0f * vEncodedNormal - 1.0f );
}

#endif
