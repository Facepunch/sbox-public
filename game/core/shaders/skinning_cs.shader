HEADER
{
	DevShader = true;
	Description = "Vertex Skinning CS";
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
	#include "system.fxc" // This should always be the first include in COMMON
	#include "common.fxc"
	#include "vr_common.fxc"
	#include "math_general.fxc"
	#include "transform_buffer.fxc"
}

CS
{
	#include "vs_decompress.fxc"
	#include "octohedral_encoding.fxc"

	struct VS_INPUT // A bogus dummy struct required to use includes below
	{
		uint4 vBlendIndices;
		float4 vBlendWeight;
		float nVertexIndex;
		uint nInstanceTransformID;
	};
	
	uint g_nMorphTextureAtlasWidth < Attribute( "MorphTextureAtlasWidth" ); >;

	#include "instancing.fxc"
	#include "morph.fxc"

	DynamicCombo( D_MORPH, 0..1, Sys( ALL ) );

	ByteAddressBuffer g_inputVB < Attribute( "InputVB" ); >;
	RWByteAddressBuffer g_outputVB < Attribute( "OutputVB" ); >;

	uint g_nVertexCount < Attribute( "VertexCount" ); >;
	uint g_nBoneIdxOffset < Attribute( "BoneIdxOffset" ); >;			// 8 bit?
	uint g_nVertexSizeInBytes < Attribute( "VertexSize" ); >;			// 8 bit?
	uint g_nBoneIdxBits < Attribute( "BoneIdxBits" ); >;				// 4 bits? Options are 8bit, 10bit, 16bit, 32bit bone indices.
	uint g_nBoneWeightOffset < Attribute( "BoneWeightOffset" ); >;		// 8 bit
	uint g_nNormalOffset < Attribute( "NormalOffset" ); >; 				// 8 bit
	uint g_nTangentSpaceOffset < Attribute( "TangentSpaceOffset" ); >; 	// 8 bit
	uint g_nInstanceCount < Attribute( "InstanceCount" ); >;

	struct InstanceParams_t
	{
		uint nSrcBufferOffset;
		uint nDestBufferOffset;
		uint nTransformBufferOffset_BlendWeightCount;
		uint nMorphOffset;
	};

	cbuffer Instances_t
	{
		InstanceParams_t g_instances[256];
	};

	float3x4 CalculateInstancingObjectToWorldMatrix( int nTransformBufferOffset, uint nBlendWeightCount, float4 vBlendWeight, uint4 nBlendIndices )
	{
		float3x4 vMatObjectToWorld;

		if ( nBlendWeightCount == 0 )
		{
			vMatObjectToWorld = GetTransformMatrix( nTransformBufferOffset );
		}
		else if ( nBlendWeightCount == 1 )
		{
			int nBoneIndex = nTransformBufferOffset + 2 + int( nBlendIndices[ 0 ] ); // Skip translucency and morph matrices
			vMatObjectToWorld = GetTransformMatrix( nBoneIndex );
		}
		else
		{
			int nBoneIndex = nTransformBufferOffset + 2 + int( nBlendIndices[ 0 ] ); // Skip translucency and morph matrices
			vMatObjectToWorld = GetTransformMatrix( nBoneIndex );
			vMatObjectToWorld *= vBlendWeight.x;

			for ( uint nBone = 1; nBone < nBlendWeightCount; nBone++ )
			{
				nBoneIndex = nTransformBufferOffset + 2 + int( nBlendIndices[ nBone ] );
				vMatObjectToWorld += vBlendWeight[ nBone ] * GetTransformMatrix( nBoneIndex );
			}
		}

		return vMatObjectToWorld;
	}

	void CS_DecodeObjectSpaceNormalAndTangent( uint nBaseVertexOffset, out float3 vNormalOs, out float4 vTangentUOs_flTangentVSign )
	{
		if ( g_nTangentSpaceOffset == 0xFFFFFFFF && !g_bUncompressedTangentFrame )
		{
			uint nPacked = g_inputVB.Load( nBaseVertexOffset + g_nNormalOffset );
			float4 vCompressedNormalOs = float4(
				( nPacked >> 0 )  & 0x3FF,
				( nPacked >> 10 ) & 0x3FF,
				( nPacked >> 20 ) & 0x3FF,
				( nPacked >> 30 ) & 0x3 );

			_DecompressNormalTangent( vCompressedNormalOs, vNormalOs, vTangentUOs_flTangentVSign );
		}
		else
		{
			vNormalOs = asfloat( g_inputVB.Load3( nBaseVertexOffset + g_nNormalOffset ) );

			if ( g_nTangentSpaceOffset < 0xFFFFFFFF )
			{
				vTangentUOs_flTangentVSign.xyzw = asfloat( g_inputVB.Load4( nBaseVertexOffset + g_nTangentSpaceOffset ) );
			}
			else
			{
				vTangentUOs_flTangentVSign.xyzw = float4( 0.0, 0.0, 1.0, 1.0 );
			}
		}
	}

	uint QuantizeFloatToUnorm( float fValue, uint N )
	{
		float fScale = float( ( 1u << N ) - 1 );
		return uint( clamp( fValue, 0.f, 1.f ) * fScale + 0.5f );
	}

	void EncodeTangentSpace( float3 vNormal, float3 vTangent, float flTangentSign, inout CachedAnimatedVertex_t vert )
	{
		float2 vOctNormal = OctohedralEncode( vNormal );
		float2 vOctTangent = OctohedralEncode( vTangent );

		// Encode to 16,16 for normal
		uint nOctNormal =
			( QuantizeFloatToUnorm( saturate( vOctNormal.x * .5 + .5 ), 16 ) << 0 ) |
			( QuantizeFloatToUnorm( saturate( vOctNormal.y * .5 + .5 ), 16 ) << 16);

		// Encode to 15,15,1 for tangent
		uint nOctTangent =
			( flTangentSign < 0.0 ? 0 : 1 ) |
			( QuantizeFloatToUnorm( saturate( vOctTangent.x * .5 + .5 ), 15 ) << 1 ) |
			( QuantizeFloatToUnorm( saturate( vOctTangent.y * .5 + .5 ), 15 ) << 16);

		vert.vPackedNormalTangentWs.x = nOctNormal;
		vert.vPackedNormalTangentWs.y = nOctTangent;
	}

	[numthreads( 64, 1, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID, uint3 vGroupThreadId : SV_GroupThreadID, uint3 vGroupId: SV_GroupID )
	{
		uint nVertexId = vThreadId.x;
		uint nInstanceId = vThreadId.y;

		if ( nVertexId >= g_nVertexCount )
		{
			return;
		}

		if ( nInstanceId >= g_nInstanceCount )
		{
			return;
		}

		InstanceParams_t inst = g_instances[ nInstanceId ];

		// fetch input pos. Assume it's always 1st in VB
		uint nVertexBaseOffset = (inst.nSrcBufferOffset + nVertexId) * g_nVertexSizeInBytes;
		float3 vPosOs = asfloat( g_inputVB.Load3( nVertexBaseOffset ) );

		// fetch input tangent space
		float3 vNormalOs;
		float4 vTangentUOs_flTangentVSign;

		CS_DecodeObjectSpaceNormalAndTangent( nVertexBaseOffset, vNormalOs, vTangentUOs_flTangentVSign );

		float flWrinkle = 0.0;
		uint nTransformBufferOffset = inst.nTransformBufferOffset_BlendWeightCount >> 4;

		#if ( D_MORPH )
		{
			MorphSubrectData_t morphSubrect = CalculateMorphSubrectData( nTransformBufferOffset );
			Morph( vPosOs, vNormalOs.xyz, flWrinkle, nVertexId + inst.nMorphOffset, morphSubrect );
		}
		#endif

		// fetch input indices
		uint4 nBoneIndices = uint4( 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF );
		if ( g_nBoneIdxOffset < 0xFFFFFFFF )
		{
			if ( g_nBoneIdxBits == 16 )
			{
				uint2 nPacked = g_inputVB.Load2( nVertexBaseOffset + g_nBoneIdxOffset );
				nBoneIndices.x = nPacked.x & 0xFFFF;
				nBoneIndices.y = nPacked.x >> 16;
				nBoneIndices.z = nPacked.y & 0xFFFF;
				nBoneIndices.w = nPacked.y >> 16;
			}
			else if ( g_nBoneIdxBits == 10 )
			{
				uint nPacked = g_inputVB.Load( nVertexBaseOffset + g_nBoneIdxOffset );
				nBoneIndices.x = nPacked & 0x3FF;
				nBoneIndices.y = ( nPacked >> 10 ) & 0x3FF;
				nBoneIndices.z = ( nPacked >> 20 ) & 0x3FF;
			}
			else
			{
				// 8 bit
				uint nPacked = g_inputVB.Load( nVertexBaseOffset + g_nBoneIdxOffset );
				nBoneIndices.x = nPacked & 0xFF;
				nBoneIndices.y = ( nPacked >> 8 ) & 0xFF;
				nBoneIndices.z = ( nPacked >> 16 ) & 0xFF;
				nBoneIndices.w = ( nPacked >> 24 ) & 0xFF;
			}
		}

		float4 vBoneWeights;
		vBoneWeights.x = 1.0;	// in case there are no weights
		if ( g_nBoneWeightOffset < 0xFFFFFFFF )
		{
			// fetch input weights (ubyte4)	// FIXME: We also support two 16 bit unorms for only two bones.
			uint nPackedWeights = g_inputVB.Load( nVertexBaseOffset + g_nBoneWeightOffset );
			vBoneWeights.x = float( nPackedWeights & 0xFF );
			vBoneWeights.y = float( ( nPackedWeights >> 8 ) & 0xFF );
			vBoneWeights.z = float( ( nPackedWeights >> 16 ) & 0xFF );
			vBoneWeights.w = float( ( nPackedWeights >> 24 ) & 0xFF );
			vBoneWeights *= 1.0f/255.0f;
		}

		CachedAnimatedVertex_t vert;

		// Fetch transforms & apply
		uint nBlendWeightCount = inst.nTransformBufferOffset_BlendWeightCount & 0xF;
		float3x4 mObjToWorld = CalculateInstancingObjectToWorldMatrix( nTransformBufferOffset, nBlendWeightCount, vBoneWeights, nBoneIndices );
		
		vert.vPosWs = mul( mObjToWorld, float4( vPosOs, 1.0f ) );
		vert.flMorphWrinkle = flWrinkle;

		float3 vNormalWs = normalize( mul( mObjToWorld, float4( vNormalOs.xyz, 0.0 ) ) );
		float3 vTangentUWs = mul( mObjToWorld, float4( vTangentUOs_flTangentVSign.xyz, 0.0 ) );
		EncodeTangentSpace( vNormalWs, vTangentUWs, vTangentUOs_flTangentVSign.w, vert );

		uint nBufferAddress = 24 * ( nVertexId + inst.nDestBufferOffset );
		g_outputVB.Store3( nBufferAddress, asuint( vert.vPosWs ) );
		g_outputVB.Store2( nBufferAddress + 12, vert.vPackedNormalTangentWs );
		g_outputVB.Store( nBufferAddress + 20, asuint( vert.flMorphWrinkle ) );
	}
}
