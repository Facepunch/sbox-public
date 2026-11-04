// Common Vertex Shader Attributes

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Geometric
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
float2 vTexCoord : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
float2 vTexCoord2 : TEXCOORD1 < Semantic( LowPrecisionUv1 ); >;	
float4 vNormalOs : NORMAL < Semantic( OptionallyCompressedTangentFrame ); >;	

#if ( VS_INPUT_HAS_TANGENT_BASIS )
float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
#endif

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// SSS Curvature
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( S_USE_PER_VERTEX_CURVATURE )
	float flSSSCurvature : TEXCOORD2 < Semantic( Curvature ); >;
#endif

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Compute Skinning
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( D_CS_VERTEX_ANIMATION )
 	float4 vBlendWeight : BLENDWEIGHT 	< Semantic( BlendWeight ); >;

	float nVertexIndex : TEXCOORD14 < Semantic( MorphIndex ); >;
	float nVertexCacheIndex : TEXCOORD15 < Semantic( MorphIndex ); >;
#endif

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Instancing data
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
uint nBoneIndex 		  : BLENDINDICES < Semantic( BlendIndices ); >; // 1D Blend Index for rigid objects transforms, skinning_cs takes uint4 from mesh

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// Baked lighting
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( D_BAKED_LIGHTING_FROM_LIGHTMAP )	
	float2 vLightmapUV : TEXCOORD3 < Semantic( LightmapUV ); > ;
#endif

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#ifndef COMMON_VS_INPUT_DEFINED
#define COMMON_VS_INPUT_DEFINED
#endif

#ifndef SHARED_STANDARD_VS_INPUT_DEFINED
#define SHARED_STANDARD_VS_INPUT_DEFINED
#endif