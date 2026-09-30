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

//-----------------------------------------------------------------------------
// Reconstruct normals from world normal, we discard the one from the normal map because
// it's easier as an API to just pass the world normal.
//-----------------------------------------------------------------------------
float3 NormalWorldToTangent( float3 vNormalWs, float3 vTangentUWs, float3 vTangentVWs )
{
	#if ( PS_INPUT_HAS_TANGENT_BASIS )
		return Vec3WsToTs( vNormalWs.xyz, vNormalWs.xyz, -vTangentUWs.xyz, -vTangentVWs.xyz ) * float3( 1, -1, 1 );
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