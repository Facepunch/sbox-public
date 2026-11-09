// Basalt's built-in mesh program on Basalt.Gpu: Depth and Forward modes over what Basalt's frames already provide -
// PerViewConstantBuffer_t, g_TransformBuffer, the instance id stream, DirectionalLightCB and ViewLightingConfigV2 -
// at the bindings vfx gives s&box's shaders: per-stage sets (0 vertex, 1 pixel), b# at 0 + n, t# at 46 + n, and the
// bindless set at 4. Compiled with -fvk-use-dx-layout so constant buffers pack as the C# mirrors of them do.

#ifdef PIXEL
#define STAGE_SET 1
#else
#define STAGE_SET 0
#endif

[[vk::binding( 62, 4 )]] Texture2D g_bindless_Texture2D[];
[[vk::binding( 29, 4 )]] SamplerComparisonState g_bindless_SamplerComparison[];

// Mirrors Basalt.Gpu.ViewConstants (common.fxc PerViewConstantBuffer_t). Row-vector matrices read as their transpose,
// so they multiply on the left
[[vk::binding( 0, STAGE_SET )]] cbuffer PerViewConstantBuffer_t
{
	float4x4 g_matWorldToProjection;
	float4x4 g_matProjectionToWorld;
	float4x4 g_matWorldToView;
	float4x4 g_matViewToProjection;
	float4 g_vInvProjRow3;
	float4 g_vClipPlane0;
	float g_flToneMapScalarLinear;
	float g_flLightMapScalar;
	float g_flEnvMapScalar;
	float g_flToneMapScalarGamma;
	float3 g_vCameraPositionWs;
	float g_flViewportMinZ;
	float3 g_vCameraDirWs;
	float g_flViewportMaxZ;
	float3 g_vCameraUpDirWs;
	float g_flTime;
	float3 g_vDepthPsToVsConversion;
	float g_flNearPlane;
	float g_flFarPlane;
	float g_flCameraFov;
	float2 g_vInvViewportSize;
	float2 g_vViewportToGBufferRatio;
	float2 g_vMorphTextureAtlasSize;
	float4 g_vInvGBufferSize;
	float2 g_vViewportOffset;
	float2 g_vViewportSize;
	float2 g_vRenderTargetSize;
	float g_flFogBlendToBackground;
	float g_flHenyeyGreensteinCoeff;
	float3 g_vFogColor;
	float g_flNegFogStartOverFogRange;
	float g_flInvFogRange;
	float g_flFogMaxDensity;
	float g_flFogExponent;
	float g_flMod2xIdentity;
	float2 g_vRoughnessParams;
	int g_nMsaaSampleCount;
	float g_flStereoCameraIndex;
	float3 g_vMiddleEyePositionWs;
	float g_flPad2;
	float4x4 g_matWorldToProjectionMultiview0;
	float4x4 g_matWorldToProjectionMultiview1;
	float4 g_vCameraPositionWsMultiview0;
	float4 g_vCameraPositionWsMultiview1;
	float4 g_vFrameBufferCopyInvSizeAndUvScale;
	float4 g_vCameraAngles;
	float4 g_vWorldToCameraOffset;
};

struct TransformEntry
{
	float4 Row0;
	float4 Row1;
	float4 Row2;
	float Alpha;
	uint TintRgb888;
	uint VertexCacheOffset;
	uint BlendWeightCount;
};

// Material values: an albedo, and the bindless comparison sampler shadows are read with
struct MaterialConstants
{
	float4 Albedo;
	uint ShadowSampler;
	uint Flags;
};

[[vk::push_constant]] MaterialConstants g_Material;

struct VsOutput
{
	float4 Position : SV_Position;
	float3 WorldPosition : TEXCOORD0;
	float3 Normal : NORMAL;
	float4 Color : COLOR0;
};

#ifndef PIXEL

[[vk::binding( 46, 0 )]] StructuredBuffer<TransformEntry> g_TransformBuffer;

struct VsInput
{
	[[vk::location( 0 )]] float3 Position : POSITION;
	[[vk::location( 1 )]] float3 Normal : NORMAL;
	[[vk::location( 2 )]] float2 Uv : TEXCOORD0;
	[[vk::location( 3 )]] uint InstanceTransformId : TEXCOORD13;
};

VsOutput MainVs( VsInput i )
{
	// Precise: the depth-only and forward pipelines must put every vertex at exactly the same depth, or forward fails
	// the prepass's depth test where the ground is seen at a grazing angle
	TransformEntry entry = g_TransformBuffer[i.InstanceTransformId];
	float4 position = float4( i.Position, 1 );
	precise float3 world = float3( dot( entry.Row0, position ), dot( entry.Row1, position ), dot( entry.Row2, position ) );
	float3 normal = float3( dot( entry.Row0.xyz, i.Normal ), dot( entry.Row1.xyz, i.Normal ), dot( entry.Row2.xyz, i.Normal ) );
	uint tint = entry.TintRgb888;

	VsOutput o;

	// Camera-relative, as native renders: the projection has no translation
	precise float4 clip = mul( g_matWorldToProjection, float4( world + g_vWorldToCameraOffset.xyz, 1 ) );
	o.Position = clip;
	o.WorldPosition = world;
	o.Normal = normal;
	o.Color = float4( ( ( tint >> 16 ) & 0xFF ) / 255.0, ( ( tint >> 8 ) & 0xFF ) / 255.0, ( tint & 0xFF ) / 255.0, entry.Alpha );
	return o;
}

#else

#define MAX_CASCADE_COUNT 4

[[vk::binding( 1, 1 )]] cbuffer DirectionalLightCB
{
	float4 g_DirectionalLightColor;
	float4 g_DirectionalLightDirection;
	float4x4 g_DirectionalLightWorldToShadowViewMatrices[MAX_CASCADE_COUNT];
	uint4 g_DirectionalLightShadowMapTextureIndex;
	uint g_DirectionalLightCascadeCount;
	float g_DirectionalLightInverseShadowMapSize;
	uint g_DirectionalLightScreenSpaceShadowIndex;
	uint g_DirectionalLightEnabled;
	float4 g_DirectionalLightCascadeHardness;
	float4 g_DirectionalLightCascadeSpheres[MAX_CASCADE_COUNT];
	float4 g_DirectionalLightShadowBias;
};

[[vk::binding( 2, 1 )]] cbuffer ViewLightingConfigV2
{
	int4 g_nNumLights;
	int4 g_nBakedLightIndexMapping[256];
	float4 g_vAmbientLightColor;
};

// Basalt's packed lights in native's 156-byte GPULight layout (LightBinnerFeature.GpuLight), at t0. Read by offset: a
// structured buffer would pad each to 160
[[vk::binding( 46, 1 )]] ByteAddressBuffer BinnedLightBufferV2;

// Every dynamic light, without clusters (those need the clustered culling compute shader), as native's
// Light::GetLightAttenuation does: cone, then CalculateDistanceFalloff
float3 LocalLights( float3 world, float3 normal, float3 albedo )
{
	float3 sum = 0;
	uint count = min( (uint)g_nNumLights.x, 256u );

	[loop]
	for ( uint i = 0; i < count; i++ )
	{
		uint at = i * 156;
		float4 row0 = asfloat( BinnedLightBufferV2.Load4( at + 12 ) );
		float4 row1 = asfloat( BinnedLightBufferV2.Load4( at + 28 ) );
		float4 row2 = asfloat( BinnedLightBufferV2.Load4( at + 44 ) );
		float3 color = asfloat( BinnedLightBufferV2.Load3( at + 76 ) );
		float3 falloff = asfloat( BinnedLightBufferV2.Load3( at + 88 ) );
		float4 cone = asfloat( BinnedLightBufferV2.Load4( at + 116 ) );

		float3 toLight = float3( row0.w, row1.w, row2.w ) - world;
		float distanceSq = max( dot( toLight, toLight ), 1 );
		float3 l = toLight * rsqrt( distanceSq );

		float coneTerm = dot( l, -float3( row0.x, row1.x, row2.x ) ) - cone.y;
		if ( coneTerm <= 0 ) continue;

		float attenuation = saturate( 1 / dot( float2( sqrt( distanceSq ), distanceSq ), falloff.xy ) - falloff.z );
		sum += albedo * color * saturate( dot( normal, l ) ) * attenuation * coneTerm * cone.z;
	}

	return sum;
}

// Reversed-Z, as native's DirectionalLightShadow.hlsl: lit where the receiver is at least as close to the light
float SunVisibility( float3 world, float3 normal )
{
	[loop]
	for ( uint i = 0; i < g_DirectionalLightCascadeCount && i < MAX_CASCADE_COUNT; i++ )
	{
		float3 toCenter = world - g_DirectionalLightCascadeSpheres[i].xyz;
		if ( dot( toCenter, toCenter ) >= g_DirectionalLightCascadeSpheres[i].w ) continue;

		float4x4 worldToShadow = g_DirectionalLightWorldToShadowViewMatrices[i];
		float3 offset = normal * ( 2 * g_DirectionalLightInverseShadowMapSize / length( worldToShadow[0].xyz ) );
		float3 shadow = mul( worldToShadow, float4( world + offset, 1 ) ).xyz;
		if ( any( shadow.xy < 0 ) || any( shadow.xy > 1 ) ) return 1;

		Texture2D map = g_bindless_Texture2D[NonUniformResourceIndex( g_DirectionalLightShadowMapTextureIndex[i] )];
		SamplerComparisonState compare = g_bindless_SamplerComparison[g_Material.ShadowSampler];
		float reference = shadow.z + g_DirectionalLightShadowBias[i];

		// Four bilinear comparisons around the texel
		float texel = g_DirectionalLightInverseShadowMapSize;
		float sum = 0;
		sum += map.SampleCmpLevelZero( compare, shadow.xy + float2( -0.5, -0.5 ) * texel, reference );
		sum += map.SampleCmpLevelZero( compare, shadow.xy + float2( 0.5, -0.5 ) * texel, reference );
		sum += map.SampleCmpLevelZero( compare, shadow.xy + float2( -0.5, 0.5 ) * texel, reference );
		sum += map.SampleCmpLevelZero( compare, shadow.xy + float2( 0.5, 0.5 ) * texel, reference );
		return sum * 0.25;
	}

	return 1;
}

float4 MainPs( VsOutput i ) : SV_Target0
{
	float3 normal = normalize( i.Normal );
	float3 albedo = g_Material.Albedo.rgb * i.Color.rgb;

	float3 light = g_vAmbientLightColor.rgb * albedo + LocalLights( i.WorldPosition, normal, albedo );
	if ( g_DirectionalLightEnabled != 0 )
	{
		float3 toSun = -normalize( g_DirectionalLightDirection.xyz );
		float lambert = saturate( dot( normal, toSun ) );
		if ( lambert > 0 ) light += albedo * g_DirectionalLightColor.rgb * lambert * SunVisibility( i.WorldPosition, normal );
	}

	return float4( light, g_Material.Albedo.a * i.Color.a );
}

#endif
