#ifndef DECALS_HLSL
#define DECALS_HLSL

#include "common/lightbinner.hlsl"
#include "common/material.hlsl"
#include "common/utils/normal.hlsl"
#include "common/classes/Sheet.hlsl"
#include "common/Bindless.hlsl"
#include "encoded_normals.fxc"

//
// Rotate a vector by a quaternion
//
float3 RotateVector( float3 v, float4 q )
{
    return v + 2.0 * cross( q.xyz, cross( q.xyz, v ) + q.w * v );
}

struct Decal
{
	float3 WorldPosition;
	float4 Quat;
	float3 Scale;
	uint2 PackedTextureID;
	uint SortOrder;
	uint ExclusionBitMask;
	uint ColorTint;
	int ExtraDataOffset;

	float3 GetCenter() { return -WorldPosition; }
	float GetRadius() { return length( 0.5f / Scale ); }
};

StructuredBuffer<Decal> DecalBuffer < Attribute( "DecalsBuffer" ); >;
ByteAddressBuffer DecalsExtraDataBuffer < Attribute( "DecalsExtraDataBuffer" ); >;

float4 UnpackColor(uint packedColor)
{
    float a = ((packedColor >> 24) & 0xFF) / 255.0;
    float b = ((packedColor >> 16) & 0xFF) / 255.0;
    float g = ((packedColor >> 8) & 0xFF) / 255.0;
    float r = (packedColor & 0xFF) / 255.0;
    return float4(r, g, b, a);
}

float3 TransformPoint( Decal decal, float3 position )
{
    position += decal.WorldPosition;
    position = RotateVector(position, decal.Quat);
    position *= decal.Scale;
    return position;
}

float3x3 QuaternionToMatrix(float4 q)
{
    float x = q.x, y = q.y, z = q.z, w = q.w;

    float xx = x * x, yy = y * y, zz = z * z;
    float xy = x * y, xz = x * z, yz = y * z;
    float wx = w * x, wy = w * y, wz = w * z;

    return float3x3(
        1 - 2 * (yy + zz), 2 * (xy - wz),     2 * (xz + wy),
        2 * (xy + wz),     1 - 2 * (xx + zz), 2 * (yz - wx),
        2 * (xz - wy),     2 * (yz + wx),     1 - 2 * (xx + yy)
    );
}

float DecalAttenuation( float3 surfaceNormal, float3 decalOrientation, float attenuationFactor )
{
    const float MinClampAngle = 0.55; // TODO: User configurable?
    float angleDot = dot( surfaceNormal, decalOrientation );
    angleDot = clamp( angleDot, MinClampAngle, 1.0 );
    angleDot = smoothstep( MinClampAngle, 1.0, angleDot );
    
    return lerp( 1, angleDot, attenuationFactor );
}

static const uint NumParallaxOcclusionSteps = 32;
static const float NumParallaxOcclusionStepsRCP = 1.0f / NumParallaxOcclusionSteps;

// Parallax Occlusion Mapping that works per pixel and with gradient derivatives
void ParallaxOcclusion_Grad(
	inout float2 inoutUV,
	float3 vView,
	float3x3 matWorldToTangent,
	float strength,
	float2 uv, float2 uv_dx, float2 uv_dy,
	Texture2D heightTexture, SamplerState heightSampler
)
{
	vView = normalize( mul( matWorldToTangent, vView ) );

	float curLayerHeight = 0;
	float2 dtex = strength * vView.xy * NumParallaxOcclusionStepsRCP;

	float2 currentTextureCoords = uv;
	float heightFromTexture = 1.0f - heightTexture.SampleGrad( heightSampler, currentTextureCoords, uv_dx, uv_dy ).r;

	int nStepIndex = 0;
	while ( heightFromTexture > curLayerHeight && nStepIndex < NumParallaxOcclusionSteps )
	{
		curLayerHeight += NumParallaxOcclusionStepsRCP;
		currentTextureCoords -= dtex;
		heightFromTexture = 1.0f - heightTexture.SampleGrad( heightSampler, currentTextureCoords, uv_dx, uv_dy ).r;
		nStepIndex++;
	}

	float2 prevTCoords = currentTextureCoords + dtex;

	float nextH = heightFromTexture - curLayerHeight;
	float prevH = 1 - heightTexture.SampleGrad( heightSampler, prevTCoords, uv_dx, uv_dy ).r - curLayerHeight + NumParallaxOcclusionStepsRCP;

	float weight = nextH / ( nextH - prevH );

	float2 finalTextureCoords = mad( prevTCoords, weight, currentTextureCoords * (1.0 - weight) );
	inoutUV += finalTextureCoords - uv;
}

class Decals
{
	static bool Resolve(
		Decal decal,
		float3 worldPosition, float3 ddxWorldPos, float3 ddyWorldPos,
		in out Material material
	)
	{
		float3 decalPosition = TransformPoint( decal, worldPosition );
	    [branch] if (all(decalPosition.xyz >= -0.5.xxx) && all(decalPosition.xyz <= 0.5.xxx))
        {
			float2 decalUV = 0.5.xx - decalPosition.yz;

			// Compute gradients properly from world position derivatives.
			// TransformPoint performed: p' = (Rotate( worldPos + WorldPosition ) * Scale)
			// So derivatives: d(p') = Rotate( d(worldPos) ) * Scale
			float3 ddxDecalPos = RotateVector( ddxWorldPos, decal.Quat ) * decal.Scale;
			float3 ddyDecalPos = RotateVector( ddyWorldPos, decal.Quat ) * decal.Scale;
			// decalUV = 0.5 - (decalPosition.yz)
			float4 gradUV = float4( -ddxDecalPos.y, -ddxDecalPos.z, -ddyDecalPos.y, -ddyDecalPos.z );

			uint color = decal.PackedTextureID.x & 0xFFFF;
			uint normal = (decal.PackedTextureID.x >> 16) & 0xFFFF;
			uint rma = decal.PackedTextureID.y & 0xFFFF;

			uint att = (decal.PackedTextureID.y >> 24) & 0xFF;
			uint exp = (decal.PackedTextureID.y >> 16) & 0xFF;
			float fAtt = (float)att / 255.0f;

			float3 decalXLocal = float3(1.0f, 0.0f, 0.0f);
			float4 local2WorldQuat = float4(-decal.Quat.xyz, decal.Quat.w); // Conjugate for local-to-world
			float3 decalProjectionAxisWorld = RotateVector(decalXLocal, local2WorldQuat);

			const float decalAttenuation = DecalAttenuation( material.Normal, -decalProjectionAxisWorld, fAtt );

			int samplerIndex = 0;

			if ( color > 0 )
			{
				float4 colorTint = UnpackColor( decal.ColorTint );
				if ( exp == 0 )
				{
					colorTint.rgb = 0.0;
				}
				else
				{
					float3 signedRGB = colorTint.rgb * 255.0 - 128.0;
					colorTint.rgb = signedRGB * exp2( float( exp ) - 128.0 - 7.0 );
				}

				float colorMix = 1.0f;
				float4 decal_albedo = float4( 0, 0, 0, 0 );
				
				Texture2D texture = GetBindlessTexture2D( NonUniformResourceIndex( color ) );

				if ( decal.ExtraDataOffset >= 0 )
				{
					float4 sheetData = asfloat( DecalsExtraDataBuffer.Load4( decal.ExtraDataOffset ) );
					uint sequence = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 16 );
					colorMix = asfloat( DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 20 ) );
					uint heightIdx = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 28 );
					samplerIndex = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 36 );
					uint emissionIdx = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 40 );

					SamplerState textureSampler = Bindless::GetSampler( NonUniformResourceIndex( samplerIndex ) );

					if ( heightIdx > 0 )
					{
						float strength = asfloat( DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 32 ) );

						// maybe there's a way to do it wih the quat directly, but i don't fucking know
						float3x3 rot = QuaternionToMatrix(decal.Quat);

						ParallaxOcclusion_Grad(
							decalUV,
							normalize(g_vCameraPositionWs - worldPosition),
							float3x3(-rot[1], -rot[2], -rot[0]),
							strength,
							decalUV.xy, gradUV.xy, gradUV.zw,
							GetBindlessTexture2D( NonUniformResourceIndex( heightIdx ) ),
							textureSampler
						);

						// Sanity
						decalUV = saturate( decalUV );
					}

					if ( sequence != 0xFFFFFF )
					{
						float2 a;
						float2 b;
						float blend;
						Sheet::Blended( sheetData, sequence, g_flTime, decalUV, a, b, blend );

						decal_albedo = texture.SampleGrad( textureSampler, a.xy, gradUV.xy, gradUV.zw );

						if ( blend > 0 )
						{
							float4 col2 = texture.SampleGrad( textureSampler, b.xy, gradUV.xy, gradUV.zw );
							decal_albedo = lerp( decal_albedo, col2, blend );
						}
					}
					else
					{
						decal_albedo = texture.SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw );
					}
					
					if ( emissionIdx > 0 )
					{
						float strength = asfloat( DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 44 ) );
						float4 decal_emission = GetBindlessTexture2D( emissionIdx ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw );
						material.Emission = lerp( material.Emission, decal_emission.rgb, decal_albedo.a );
						material.Emission *= strength;
					}
				}
				else
				{
					decal_albedo = texture.SampleGrad( g_sTrilinearClamp, decalUV.xy, gradUV.xy, gradUV.zw );
				}

				decal_albedo *= colorTint;
				decal_albedo *= decalAttenuation;

				material.Albedo = lerp( material.Albedo, decal_albedo.rgb, colorMix * decal_albedo.a );

				// blend everything else in from the color transparency
				if ( normal > 0 )
				{
					SamplerState textureSampler = Bindless::GetSampler( NonUniformResourceIndex( samplerIndex ) );
					float3 normalts = DecodeNormal( GetBindlessTexture2D( normal ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw ).xyz );

					// maybe there's a way to do it wih the quat directly, but i don't fucking know
					float3x3 rot = QuaternionToMatrix(decal.Quat);
					float3 normal = TransformNormal( normalts, -rot[0], -rot[1], -rot[2] );

                    material.Normal = normalize( lerp( material.Normal, normal, decal_albedo.a ) );
				}

				if ( rma > 0 )
				{
					SamplerState textureSampler = Bindless::GetSampler( NonUniformResourceIndex( samplerIndex ) );
					float4 decal_rma = GetBindlessTexture2D( rma ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw );
					material.Roughness = lerp( material.Roughness, decal_rma.r, decal_albedo.a );
					material.Metalness = lerp( material.Metalness, decal_rma.g, decal_albedo.a );
					material.AmbientOcclusion = lerp( material.AmbientOcclusion, decal_rma.b, decal_albedo.a );
				}
			}

			// we can do masks here, we just need to pass them through instance data per object
			// if ( decal.Mask & instance.mask == 0 )
			// {
			// 		continue;
			// }

			return true;
		}

		return false;
	}

	static void Apply( float3 worldPos, in out Material material )
	{
		ClusterRange range = Cluster::Query( ClusterItemType_Decal, worldPos );
		if ( range.Count == 0 )
		{
			return;
		}

		// ddx/ddy expensive; compute world position derivatives once and pass them in.
		// we use these to compute decal UV gradients for proper mipmapping for decals, also cheaper than doing .Sample separately
		float3 ddxWorldPos = ddx( worldPos );
		float3 ddyWorldPos = ddy( worldPos );

		for ( uint j = 0; j < range.Count; j++ )
		{
			Decal decal = DecalBuffer[ Cluster::LoadItem( range, j ) ];
			Decals::Resolve( decal, worldPos, ddxWorldPos, ddyWorldPos, material );
		}
	}

};

#endif