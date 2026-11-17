namespace Borealis.Gpu;

/// <summary>
/// How to create a <see cref="Device"/>.
/// </summary>
public sealed class DeviceOptions
{
	/// <summary>The application's name, which drivers and tools see.</summary>
	public string ApplicationName { get; init; } = "Borealis";

	/// <summary>
	/// The engine's name, which drivers and tools see. Drivers choose per-application profiles by these two names, and
	/// a profile can change results: AMD's does trilinear filtering differently for Source 2's names (<c>Sbox</c>,
	/// <c>Source2</c>) than for others.
	/// </summary>
	public string EngineName { get; init; } = "Borealis";

	/// <summary>
	/// Turn on the Khronos validation layer, when it's installed, and route its messages to <see cref="Message"/>.
	/// </summary>
	public bool Validation { get; init; }

	/// <summary>
	/// With <see cref="Validation"/>, also check synchronisation (barriers, queue ordering). Slower.
	/// </summary>
	public bool SynchronizationValidation { get; init; }

	/// <summary>
	/// With <see cref="Validation"/>, also instrument shaders to check on the GPU what they read: descriptors indexed out of
	/// range, never written or whose resource is gone, and out-of-bounds buffer accesses. Much slower; for tests.
	/// </summary>
	public bool GpuAssistedValidation { get; init; }

	/// <summary>
	/// Write progress markers (VK_AMD_buffer_marker, when the device has it) into host memory at each list's start and end
	/// and at every label, so a report after the device is lost shows how far the GPU got in each list
	/// (<see cref="Device.DescribeRecentSubmissions"/>).
	/// </summary>
	public bool ProgressMarkers { get; init; }

	/// <summary>
	/// Keep the driver's statistics for each pipeline's shaders (VK_KHR_pipeline_executable_properties, when the device
	/// has it): registers, spills, wave size and the like, in <see cref="Device.ShaderStatistics"/>. For measuring.
	/// </summary>
	public bool ShaderStatistics { get; init; }

	/// <summary>Frames the CPU may record ahead of the GPU.</summary>
	public int FramesInFlight { get; init; } = 2;

	/// <summary>Enable task and mesh shaders (VK_EXT_mesh_shader) where the device has them; off leaves them out.</summary>
	public bool MeshShaders { get; init; } = true;

	/// <summary>Part of the adapter's name to prefer, or null for the first discrete GPU.</summary>
	public string PreferredAdapter { get; init; }

	/// <summary>Whether swap chains will be made: enables the surface extensions. Offscreen devices can leave it off.</summary>
	public bool Presentation { get; init; } = true;

	/// <summary>
	/// More instance extensions to enable: the ones a window system asks for to make its own surfaces
	/// (<see cref="SurfaceSource.Custom"/>, such as SDL's <c>SDL_Vulkan_GetInstanceExtensions</c>). Creating the device
	/// fails if one isn't available.
	/// </summary>
	public IReadOnlyList<string> InstanceExtensions { get; init; }

	/// <summary>Pipeline cache data saved from <see cref="Device.GetPipelineCacheData"/>, or null.</summary>
	public byte[] PipelineCacheData { get; init; }

	/// <summary>
	/// A folder to keep the pipeline cache in, or null for none: the file for this GPU and driver is loaded when the device
	/// is made (in place of <see cref="PipelineCacheData"/>), and saved by <see cref="Device.SavePipelineCache"/> and when
	/// the device is disposed, if pipelines were made since.
	/// </summary>
	public string PipelineCacheFolder { get; init; }

	/// <summary>Validation and driver messages. Called on any thread. Errors are also counted in <see cref="Device.ValidationErrors"/>.</summary>
	public Action<DeviceMessage> Message { get; init; }
}

/// <summary>
/// How serious a <see cref="DeviceMessage"/> is.
/// </summary>
public enum MessageSeverity
{
	/// <summary>Diagnostic detail.</summary>
	Verbose,

	/// <summary>Information.</summary>
	Info,

	/// <summary>Probably a mistake, or slow.</summary>
	Warning,

	/// <summary>Invalid use of the API.</summary>
	Error,
}

/// <summary>
/// A message from the validation layer or the driver.
/// </summary>
public readonly record struct DeviceMessage( MessageSeverity Severity, string Id, string Text );

/// <summary>
/// The GPU a device runs on.
/// </summary>
public sealed class AdapterInfo
{
	/// <summary>Its name.</summary>
	public string Name { get; init; }

	/// <summary>PCI vendor id: 0x10DE NVIDIA, 0x1002 AMD, 0x8086 Intel...</summary>
	public uint VendorId { get; init; }

	/// <summary>PCI device id.</summary>
	public uint DeviceId { get; init; }

	/// <summary>Whether it's a discrete GPU.</summary>
	public bool Discrete { get; init; }

	/// <summary>Whether it shares memory with the CPU (integrated, or the Deck's APU).</summary>
	public bool Integrated { get; init; }

	/// <summary>Whether it's a software rasteriser (Lavapipe, SwiftShader).</summary>
	public bool Software { get; init; }

	/// <summary>The Vulkan version it supports, as major.minor.patch.</summary>
	public string ApiVersion { get; init; }

	/// <summary>The driver's version, decoded as Source 2 reports it.</summary>
	public string DriverVersion { get; init; }

	/// <summary>The size of its device-local memory heaps together, in bytes.</summary>
	public ulong DeviceLocalBytes { get; init; }

	/// <summary>
	/// A Vulkan driver version as Source 2 writes it: NVIDIA packs 10.8.8.6 bits, everyone else 10.10.12.
	/// </summary>
	public static string DecodeDriverVersion( uint vendorId, uint driverVersion ) => vendorId == 0x10DE
		? $"{(driverVersion >> 22) & 0x3ff}.{(driverVersion >> 14) & 0xff}.{(driverVersion >> 6) & 0xff}.{driverVersion & 0x3f}"
		: $"{driverVersion >> 22}.{(driverVersion >> 12) & 0x3ff}.{driverVersion & 0xfff}";

	/// <inheritdoc/>
	public override string ToString() => $"{Name} (Vulkan {ApiVersion})";
}

/// <summary>
/// What a device can do beyond the baseline it requires.
/// </summary>
public sealed class DeviceCapabilities
{
	/// <summary>Whether compute work can run on its own queue beside graphics.</summary>
	public bool AsyncCompute { get; init; }

	/// <summary>Whether indirect draws can take their count from a buffer (always, on Vulkan 1.3 devices that report it).</summary>
	public bool DrawIndirectCount { get; init; }

	/// <summary>Whether BC compressed textures can be sampled.</summary>
	public bool TextureCompressionBC { get; init; }

	/// <summary>The most anisotropy a sampler can use; 1 for none.</summary>
	public float MaxAnisotropy { get; init; }

	/// <summary>Whether the graphics queue can write timestamps, and nanoseconds per tick.</summary>
	public bool Timestamps { get; init; }

	/// <summary>Whether occlusion queries count every sample that passes (<see cref="OcclusionQueries"/>), not just whether any did.</summary>
	public bool PreciseOcclusion { get; init; }

	/// <summary>Whether pipeline statistics queries (<see cref="PipelineStatistics"/>) can be made.</summary>
	public bool PipelineStatistics { get; init; }

	/// <summary>Whether queries can be reset on the CPU (<see cref="TimestampQueries.ResetOnHost"/>), not only in a command list.</summary>
	public bool HostQueryReset { get; init; }

	/// <summary>Nanoseconds per timestamp tick.</summary>
	public float TimestampPeriod { get; init; }

	/// <summary>The largest 2D texture.</summary>
	public int MaxTextureSize { get; init; }

	/// <summary>Sample counts colour targets support, as a bit mask of 1, 2, 4, 8...</summary>
	public int ColorSampleCounts { get; init; }

	/// <summary>Constant buffer offsets must be multiples of this.</summary>
	public int UniformAlignment { get; init; }

	/// <summary>How many <see cref="BindingType.UniformBufferDynamic"/> bindings a pipeline layout may have, over all its sets.</summary>
	public int MaxDynamicUniformBuffers { get; init; }

	/// <summary>Storage buffer offsets must be multiples of this.</summary>
	public int StorageAlignment { get; init; }

	/// <summary>How many sampled textures the bindless table holds.</summary>
	public int BindlessTextures { get; init; }

	/// <summary>How many samplers the bindless table holds.</summary>
	public int BindlessSamplers { get; init; }

	/// <summary>How many storage textures the bindless table holds.</summary>
	public int BindlessStorageTextures { get; init; }

	/// <summary>
	/// Whether pipelines can have task and mesh shaders (VK_EXT_mesh_shader) in place of vertex input and a vertex shader,
	/// drawn with <see cref="CommandList.DrawMeshTasksIndirect"/>.
	/// </summary>
	public bool MeshShaders { get; init; }

	/// <summary>
	/// Whether vertex shaders can use wave (subgroup) vote and ballot operations. Most GPUs can; Lavapipe and some mobile
	/// GPUs only have them in fragment, compute, task and mesh shaders, and reject vertex modules that use them.
	/// </summary>
	public bool VertexWaveOps { get; init; }
}
