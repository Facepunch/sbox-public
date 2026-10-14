#ifndef NORMAL_TRANSFORM_H
#define NORMAL_TRANSFORM_H

/// <summary>
/// Transforms a normal by the inverse transpose, including nonuniform scale and shear.
/// Returns the transform's handedness for reconstructing the tangent frame.
/// Singular transforms fall back to a finite forward-transformed or object-space normal.
/// </summary>
float3 TransformNormalInverseTranspose( float3 vNormal, float3x3 transform, out float flTransformSign )
{
    float3x3 cofactor = float3x3(
        cross( transform[1], transform[2] ),
        cross( transform[2], transform[0] ),
        cross( transform[0], transform[1] ) );

    float flDeterminant = dot( transform[0], cofactor[0] );
    flTransformSign = flDeterminant < 0.0f ? -1.0f : 1.0f;

    // Normalization cancels the determinant's magnitude; its sign must survive reflections.
    float3 vTransformedNormal = mul( cofactor, vNormal ) * flTransformSign;
    if ( dot( vTransformedNormal, vTransformedNormal ) > 0.0f )
    {
        return normalize( vTransformedNormal );
    }

    vTransformedNormal = mul( transform, vNormal );
    if ( dot( vTransformedNormal, vTransformedNormal ) > 0.0f )
    {
        return normalize( vTransformedNormal );
    }

    return dot( vNormal, vNormal ) > 0.0f ? normalize( vNormal ) : float3( 0, 0, 1 );
}

#endif
