#ifndef COMMON_CLASSES_DEFORMATION_HLSL
#define COMMON_CLASSES_DEFORMATION_HLSL

DynamicCombo( D_DEFORMATION_VOLUME, 0..1, Sys( ALL ) );

#if D_DEFORMATION_VOLUME
#include "common/classes/DeformationVolume.hlsl"

StructuredBuffer<DeformationVolume> g_deformationVolumes < Attribute( "DeformationVolumes" ); >;

// Ordered model-space stack, evaluated after morphs and before bone skinning.
struct Deformation
{
	static void TransformFrame( float3x3 jacobian, inout float3 normal, inout float4 tangent )
	{
		float3x3 cofactor = float3x3( cross( jacobian[1], jacobian[2] ), cross( jacobian[2], jacobian[0] ), cross( jacobian[0], jacobian[1] ) );
		float3 transformedNormal = mul( cofactor, normal );
		if ( dot( transformedNormal, transformedNormal ) > 0.00000001 )
		{
			normal = normalize( transformedNormal );
			float3 transformedTangent = mul( jacobian, tangent.xyz );
			transformedTangent -= normal * dot( normal, transformedTangent );
			if ( dot( transformedTangent, transformedTangent ) > 0.00000001 )
			{
				tangent.xyz = normalize( transformedTangent );
			}
			else
			{
				float3 axis = abs( normal.z ) < 0.9 ? float3( 0, 0, 1 ) : float3( 0, 1, 0 );
				tangent.xyz = normalize( cross( axis, normal ) );
			}
			// Cofactors preserve the oriented tangent frame, including through folds.
		}
	}

	static void Apply( uint volumeOffset, uint volumeCount,
		inout float3 position, inout float3 normal, inout float4 tangent )
	{
		for ( uint index = 0; index < volumeCount; ++index )
		{
			DeformationVolume volume = g_deformationVolumes[volumeOffset + index];
			float3x4 toLocal = float3x4( volume.modelToVolume[0], volume.modelToVolume[1], volume.modelToVolume[2] );
			float3x3 toModel = float3x3( volume.volumeToModel[0].xyz, volume.volumeToModel[1].xyz, volume.volumeToModel[2].xyz );
			float3 localPosition = mul( toLocal, float4( position, 1 ) );
			uint normalMode = uint( volume.weightOperation.z );

			float3 delta;
			float3x3 derivative;
			float distance;
			volume.Evaluate( localPosition, delta, derivative, distance );
			if ( normalMode != DEFORMATION_NORMAL_PRESERVE )
			{
				// All operations have analytic derivatives. Smooth samples a wider neighbourhood.
				float h = volume.weightOperation.w;
				if ( normalMode == DEFORMATION_NORMAL_SMOOTH && distance < h )
				{
					float3 dx = volume.SampleDisplacement( localPosition + float3( h, 0, 0 ) )
						- volume.SampleDisplacement( localPosition - float3( h, 0, 0 ) );
					float3 dy = volume.SampleDisplacement( localPosition + float3( 0, h, 0 ) )
						- volume.SampleDisplacement( localPosition - float3( 0, h, 0 ) );
					float3 dz = volume.SampleDisplacement( localPosition + float3( 0, 0, h ) )
						- volume.SampleDisplacement( localPosition - float3( 0, 0, h ) );
					derivative = transpose( float3x3( dx, dy, dz ) ) / (2 * h);
				}

				float3x3 jacobian = float3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 ) + mul( toModel, mul( derivative, (float3x3)toLocal ) );
				TransformFrame( jacobian, normal, tangent );
			}

			// The next operation evaluates both its mask and its shape at this new position.
			position += mul( toModel, delta );
		}
	}
};
#endif

#endif
