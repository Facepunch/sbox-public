namespace Basalt.Gpu;

/// <summary>
/// Where s&amp;box's compiled shaders expect their resources (vfx: <c>src/vfx/vfx_vulkan_common.cpp</c>,
/// <c>slang_shader_compiler.cpp</c>; the native device: <c>pipelinemgrvulkan.cpp</c>). Pipeline layouts here follow it,
/// so SPIR-V compiled for the native renderer binds unchanged. See docs/managed/basalt-gpu.md.
/// </summary>
public static class ShaderAbi
{
	/// <summary>Vertex (and compute) shader resources: each stage's resources without an explicit space.</summary>
	public const int VertexSet = 0;

	/// <summary>Compute shader resources.</summary>
	public const int ComputeSet = 0;

	/// <summary>Pixel shader resources.</summary>
	public const int FragmentSet = 1;

	/// <summary>Geometry shader resources.</summary>
	public const int GeometrySet = 2;

	/// <summary>The ray tracing set (<c>RaytracingDescriptorSet</c>, <c>Slot 0</c>); empty here.</summary>
	public const int RaytracingSet = 3;

	/// <summary><c>g_globalLateBoundBindlessSet</c> (<c>Slot 1</c>, after three reserved sets).</summary>
	public const int BindlessSet = 4;

	/// <summary>
	/// <c>g_viewSet</c> (<c>Slot 2</c>): a view's fog, lighting config, sun, clusters, lights, decals, shadows, volumes and
	/// light modulators, bound once per view (<see cref="ViewSetBindings"/>, <c>scenesystem/viewdescriptorset.h</c>).
	/// </summary>
	public const int ViewSet = 5;

	/// <summary>How many sets a pipeline layout has.</summary>
	public const int SetCount = 6;

	/// <summary>
	/// The view set's bindings, as <c>g_ViewDescriptorSetEntries</c> (<c>scenesystem/viewdescriptorset.h</c>) lays them out:
	/// each binding and the render attribute it's filled from. Constant buffers at <c>b#</c>, read-only buffers at
	/// <c>t#</c>.
	/// </summary>
	public static readonly (int Binding, BindingType Type, string Attribute)[] ViewSetBindings =
	[
		(ConstantBuffer( 0 ), BindingType.UniformBuffer, "PerViewConstantBufferVR_t"),
		(ConstantBuffer( 1 ), BindingType.UniformBuffer, "ViewLightingConfigV2"),
		(ConstantBuffer( 2 ), BindingType.UniformBuffer, "DirectionalLightCB"),
		(ConstantBuffer( 3 ), BindingType.UniformBuffer, "ClusteredLightingConstants"),
		(ConstantBuffer( 4 ), BindingType.UniformBuffer, "FogConstants"),
		(Texture( 0 ), BindingType.StorageBuffer, "BinnedLightBufferV2"),
		(Texture( 1 ), BindingType.StorageBuffer, "BinnedEnvMapBuffer"),
		(Texture( 2 ), BindingType.StorageBuffer, "DecalsBuffer"),
		(Texture( 3 ), BindingType.StorageBuffer, "DecalsExtraDataBuffer"),
		(Texture( 4 ), BindingType.StorageBuffer, "ProjectedShadows"),
		(Texture( 5 ), BindingType.StorageBuffer, "ProjectedCubeShadows"),
		(Texture( 6 ), BindingType.StorageBuffer, "ClusterItemCounts"),
		(Texture( 7 ), BindingType.StorageBuffer, "ClusterItemIndices"),
		(Texture( 8 ), BindingType.StorageBuffer, "DDGI_Volumes"),
		(Texture( 9 ), BindingType.StorageBuffer, "LightModulatorRectangles"),
		(Texture( 10 ), BindingType.StorageBuffer, "LightModulatorCandidates"),
	];

	/// <summary>Where <c>b#</c> registers start.</summary>
	public const int ConstantBufferBase = 0;

	/// <summary>Where <c>s#</c> registers start.</summary>
	public const int SamplerBase = 14;

	/// <summary>Where <c>t#</c> registers start: textures and read-only storage buffers.</summary>
	public const int TextureBase = 46;

	/// <summary>Where <c>u#</c> registers start: read-write textures and buffers.</summary>
	public const int StorageBase = 174;

	/// <summary>The binding of <c>b<paramref name="register"/></c>.</summary>
	public static int ConstantBuffer( int register ) => ConstantBufferBase + register;

	/// <summary>The binding of <c>s<paramref name="register"/></c>.</summary>
	public static int Sampler( int register ) => SamplerBase + register;

	/// <summary>The binding of <c>t<paramref name="register"/></c>.</summary>
	public static int Texture( int register ) => TextureBase + register;

	/// <summary>The binding of <c>u<paramref name="register"/></c>.</summary>
	public static int Storage( int register ) => StorageBase + register;

	/// <summary>Bindless samplers, <c>g_bindless_Sampler</c> at <c>s15</c>.</summary>
	public const int BindlessSamplers = SamplerBase + 15;

	/// <summary>The animated vertex cache, <c>g_animatedVertexCache</c> at <c>t3</c> (vertex and compute only).</summary>
	public const int BindlessVertexCache = TextureBase + 3;

	/// <summary>Pipeline texture slots, <c>g_PipelineTextureIndices</c> at <c>t14</c>.</summary>
	public const int BindlessPipelineTextures = TextureBase + 14;

	/// <summary>Bindless sampled textures of every dimension, <c>g_bindless_Texture2D</c> and friends at <c>t16</c>.</summary>
	public const int BindlessTextures = TextureBase + 16;

	/// <summary>
	/// Bindless raw buffers, <c>g_bindless_VertexBuffers</c> (<c>ByteAddressBuffer[]</c>) at <c>t17</c>: geometry pages and
	/// GPU-driven tables (<c>basalt_gpu_driven.fxc</c>). Native's <c>RENDER_GLOBAL_BINDING_VERTEX_BUFFERS</c> in PR #6511.
	/// </summary>
	public const int BindlessBuffers = TextureBase + 17;

	/// <summary>Bindless storage textures, <c>g_bindless_RWTexture2D</c> and friends at <c>u16</c> (compute only).</summary>
	public const int BindlessStorageTextures = StorageBase + 16;

	/// <summary>The one push constant range every layout has, at offset 0, visible to every stage.</summary>
	public const int PushConstantSize = 128;
}
