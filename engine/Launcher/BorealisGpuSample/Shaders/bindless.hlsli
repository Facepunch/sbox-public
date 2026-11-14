// The bindless set as s&box's shaders declare it (common/classes/Bindless.hlsl), with the Vulkan bindings vfx gives it:
// set 4, t16 -> 62, s15 -> 29, u16 -> 190. See docs/managed/borealis-gpu.md.

#ifndef BINDLESS_HLSLI
#define BINDLESS_HLSLI

[[vk::binding( 62, 4 )]] Texture2D g_bindless_Texture2D[];
[[vk::binding( 62, 4 )]] TextureCube g_bindless_TextureCube[];
[[vk::binding( 29, 4 )]] SamplerState g_bindless_Sampler[];

#ifdef COMPUTE
[[vk::binding( 190, 4 )]] [[vk::image_format( "unknown" )]] RWTexture2D<float4> g_bindless_RWTexture2D[];
#endif

#endif
