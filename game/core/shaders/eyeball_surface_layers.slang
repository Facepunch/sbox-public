// eyeball_surface's helpers: its sampler, the eye-occlusion projection and the layers Evaluate and Shade share
import sbox.surface;

// eyeball.shader's g_sAnisoClamp (the eye occlusion); the rest use g_sAniso, the surfaces' standard filtering
[Sampler( "Anisotropic 8", "Clamp" )] SamplerState g_EyeballAnisoClamp;

// The eye occlusion projection, from the object's attributes
[Engine( "EyeOcclusionViewProj" )] float4x4 g_matEyeballOcclusionWorldToTexture;

/// What Evaluate and Shade both work out: the surface, and the iris layer and eye occlusion only Shade lights with.
struct EyeballLayers
{
	Material Surface;          // the cornea and sclera's normal, the albedo, self-illumination and opacity
	float3 IrisNormal;
	float2 IrisRoughness;
	float IrisReflectance;
	float IrisMask;
	float Occlusion;
}
