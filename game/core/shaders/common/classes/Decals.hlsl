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

// The view's decals, in its descriptor set (common.fxc)
StructuredBuffer<Decal> DecalBuffer EXTERNAL_DESC_SET( t, g_viewSet, 2 );
ByteAddressBuffer DecalsExtraDataBuffer EXTERNAL_DESC_SET( t, g_viewSet, 3 );

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
	// A loop, not unrolled: unrolled, its 32 steps were most of a lit shader's decal code
	[loop]
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

// A decal's rotation, as QuaternionToMatrix. A decal's are the same for the whole wave (ClusterForEach), but there is no
// scalar float maths: computed in vector registers, they are read back to scalar ones, so none are held. Each use takes
// its own from the quaternion read again, which keeps the compiler from sharing one and holding it between them
float3x3 DecalRotation( float4 quat )
{
	float3x3 rot = QuaternionToMatrix( WaveReadLaneFirst( quat ) );
	rot[0] = WaveReadLaneFirst( rot[0] );
	rot[1] = WaveReadLaneFirst( rot[1] );
	rot[2] = WaveReadLaneFirst( rot[2] );
	return rot;
}

// A decal's sheet frames, as Sheet::Blended takes them before the decal's UV: frame 0 and 1 as offset and size, and the
// blend between them. A decal's sheet data and sequence are the wave's own (ClusterForEach) and the time is the frame's,
// so every lookup is the same for the whole wave: each is read back to scalar registers as it lands, rather than four
// held in vector ones at once. Same lookups and the same sums as SampleSheetData
bool DecalSheetFrames( float4 data, float sequence, float time, out float4 frame0, out float4 frame1, out float blend )
{
	frame0 = float4( 0, 0, 1, 1 );
	frame1 = float4( 0, 0, 1, 1 );
	blend = 0;

	if ( data.w == 0 )
		return false;

	SheetDataSamplerParams_t params;
	params.m_flSheetTextureBaseV = data.x;
	params.m_flOOSheetTextureWidth = 1.0f / data.y;
	params.m_flOOSheetTextureHeight = data.z;
	params.m_flSheetTextureWidth = data.y;
	params.m_flSheetSequenceCount = data.w;
	params.m_flSequenceAnimationTimescale = 1.0f;
	params.m_flSequenceIndex = fmod( sequence, data.w );
	params.m_flSequenceAnimationTime = time;

	float flV = SheetTextureVCoord( params );
	float flClamp = WaveReadLaneFirst( g_SheetTexture.SampleLevel( g_sPointWrap, float2( 0, flV ), 0 ).z );

	float flSequenceIndexingTime = 1.0 * params.m_flSheetTextureWidth * 0.5 * params.m_flSequenceAnimationTime * params.m_flSequenceAnimationTimescale;
	float flFractionalFrameIndex = frac( flSequenceIndexingTime );
	float flTexFrameIndex = 2.0 * floor( flSequenceIndexingTime );
	if ( flClamp > 0 )
		flTexFrameIndex = min( params.m_flSheetTextureWidth - 2, flTexFrameIndex );
	else
		flTexFrameIndex = fmod( flTexFrameIndex, params.m_flSheetTextureWidth );
	flTexFrameIndex = flTexFrameIndex * params.m_flOOSheetTextureWidth;

	float2 vControl = WaveReadLaneFirst( g_SheetTexture.SampleLevel( g_sPointWrap, float2( flTexFrameIndex, flV ), 0 ).xw );
	blend = WaveReadLaneFirst( lerp( vControl.x, vControl.y, flFractionalFrameIndex ) );

	float4 vCrop = WaveReadLaneFirst( g_SheetTexture.SampleLevel( g_sPointWrap, float2( flTexFrameIndex + params.m_flOOSheetTextureWidth, flV + params.m_flOOSheetTextureHeight ), 0 ) );
	float2 vCroppedSize = vCrop.zw - vCrop.xy;

	float4 vBounds = WaveReadLaneFirst( g_SheetTexture.SampleLevel( g_sPointWrap, float2( flTexFrameIndex, flV + params.m_flOOSheetTextureHeight ), 0 ) );
	float2 vUncroppedSize = ( vBounds.zw - vBounds.xy ) / vCroppedSize.xy;
	vBounds.xy -= vCrop.xy * vUncroppedSize.xy;
	vBounds.zw = vBounds.xy + vUncroppedSize.xy;
	frame0 = WaveReadLaneFirst( float4( vBounds.xy, vBounds.zw - vBounds.xy ) );

	vBounds = WaveReadLaneFirst( g_SheetTexture.SampleLevel( g_sPointWrap, float2( flTexFrameIndex, flV + 2.0 * params.m_flOOSheetTextureHeight ), 0 ) );
	vUncroppedSize = ( vBounds.zw - vBounds.xy ) / vCroppedSize.xy;
	vBounds.xy -= vCrop.xy * vUncroppedSize.xy;
	vBounds.zw = vBounds.xy + vUncroppedSize.xy;
	frame1 = WaveReadLaneFirst( float4( vBounds.xy, vBounds.zw - vBounds.xy ) );

	return true;
}

struct Decals
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

			// Only reached from ClusterForEach, so the decal and every index read from it are wave-uniform.
			// Slang can't prove it, hence asDynamicUniform
			int samplerIndex = 0;

			if ( color > 0 )
			{
				float colorMix = 1.0f;
				uint blendMode = 0;
				float4 decal_albedo = float4( 0, 0, 0, 0 );
				float flHeightCoverageMask = 1;

				Texture2D texture = Bindless::GetTexture2D( asDynamicUniform( UniformIndex( color ) ) );

				if ( decal.ExtraDataOffset >= 0 )
				{
					float4 sheetData = asfloat( DecalsExtraDataBuffer.Load4( decal.ExtraDataOffset ) );
					uint sequence = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 16 );
					colorMix = asfloat( DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 20 ) );
					blendMode = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 24 ) & 3;
					uint heightIdx = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 28 );
					samplerIndex = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 36 );
					uint emissionIdx = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 40 );

					SamplerState textureSampler = Bindless::GetSampler( asDynamicUniform( samplerIndex ) );

					if ( heightIdx > 0 )
					{
						float strength = asfloat( DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 32 ) );
						
						// get the heightmap texture 2D from bindless and sample it 
						Texture2D tHeightmap = Bindless::GetTexture2D( asDynamicUniform( UniformIndex( heightIdx ) ) );
	
						float3x3 rot = DecalRotation( decal.Quat );
						ParallaxOcclusion_Grad(
							decalUV,
							normalize(g_vCameraPositionWs - worldPosition),
							float3x3(-rot[1], -rot[2], -rot[0]),
							strength,
							decalUV.xy, gradUV.xy, gradUV.zw,
							tHeightmap,
							textureSampler
						);

						// Sanity
						decalUV = saturate( decalUV );

						// Unpack height coverage settings, just amount & range for now 
						uint heightCoverageSettings = DecalsExtraDataBuffer.Load( decal.ExtraDataOffset + 48 );
						float flCoverageAmount = 1.0f - (( heightCoverageSettings & 0xFF ) / 255.0);
						float flCoverageRange = ( ( heightCoverageSettings >> 8 ) & 0xFF ) / 255.0;
						
						// Compute the height coverage mask

						float flHeightmap = tHeightmap.SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw ).r;
						if ( flCoverageAmount > 0.0f )
						{
							float flCoverageRangeClamped = min( flCoverageRange, min( flCoverageAmount, 1.0f - flCoverageAmount ) );
							flHeightCoverageMask = smoothstep( flCoverageAmount - flCoverageRangeClamped, flCoverageAmount + 0.001, flHeightmap );
						}
					}

					if ( sequence != 0xFFFFFF )
					{
						// As Sheet::Blended
						float4 frame0, frame1;
						float blend;
						DecalSheetFrames( sheetData, sequence, g_flTime, frame0, frame1, blend );
						float2 a = frame0.xy + ( decalUV * frame0.zw );
						float2 b = frame1.xy + ( decalUV * frame1.zw );

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
						float4 decal_emission = Bindless::GetTexture2D( asDynamicUniform( UniformIndex( emissionIdx ) ) ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw );
						material.Emission = lerp( material.Emission, decal_emission.rgb * strength, decal_albedo.a );
					}
				}
				else
				{
					decal_albedo = texture.SampleGrad( g_sTrilinearClamp, decalUV.xy, gradUV.xy, gradUV.zw );
				}

				// The tint where it's used, rather than held through the samples above
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

				decal_albedo *= colorTint;
				if ( blendMode == 0 )
					decal_albedo.rgb *= decalAttenuation;
				decal_albedo.a *= decalAttenuation;
				decal_albedo.a *= flHeightCoverageMask;

				if ( blendMode == 1 )
					material.Albedo *= lerp( 1, decal_albedo.rgb, colorMix * decal_albedo.a );
				else if ( blendMode == 2 )
					material.Emission += decal_albedo.rgb * colorMix * decal_albedo.a;
				else
					material.Albedo = lerp( material.Albedo, decal_albedo.rgb, colorMix * decal_albedo.a );

				// blend everything else in from the color transparency
				if ( normal > 0 )
				{
					SamplerState textureSampler = Bindless::GetSampler( asDynamicUniform( samplerIndex ) );
					float3 normalts = DecodeNormal( Bindless::GetTexture2D( asDynamicUniform( UniformIndex( normal ) ) ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw ).xyz );

					float3x3 rot = DecalRotation( decal.Quat );
					float3 normal = TransformNormal( normalts, -rot[0], -rot[1], -rot[2] );

					material.Normal = normalize( lerp( material.Normal, normal, decal_albedo.a ) );
				}

				if ( rma > 0 )
				{
					SamplerState textureSampler = Bindless::GetSampler( asDynamicUniform( samplerIndex ) );
					float4 decal_rma = Bindless::GetTexture2D( asDynamicUniform( UniformIndex( rma ) ) ).SampleGrad( textureSampler, decalUV.xy, gradUV.xy, gradUV.zw );
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
		// ddx/ddy expensive; compute world position derivatives once and pass them in.
		// we use these to compute decal UV gradients for proper mipmapping for decals, also cheaper than doing .Sample separately.
		// Taken before the early out: a quad neighbour's cluster can be empty, and derivatives need the whole quad
		float3 ddxWorldPos = ddx( worldPos );
		float3 ddyWorldPos = ddy( worldPos );

		ClusterRange range = Cluster::Query( ClusterItemType_Decal, material.ScreenPosition );
		if ( range.Count == 0 )
		{
			return;
		}

		ClusterForEach( range )
			Decals::Resolve( DecalBuffer[ range.Item ], worldPos, ddxWorldPos, ddyWorldPos, material );
	}

};

#endif
