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
	/// With <see cref="Validation"/>, also report the layer's best practices checks (as warnings), for
	/// tests that hold what a device makes to them.
	/// </summary>
	internal bool BestPracticesValidation { get; init; }

	/// <summary>
	/// With <see cref="Validation"/>, also instrument shaders to check on the GPU what they read: descriptors indexed out of
	/// range, never written or whose resource is gone, and out-of-bounds buffer accesses. Much slower; for tests.
	/// </summary>
	public bool GpuAssistedValidation { get; init; }

	/// <summary>
	/// With <see cref="GpuAssistedValidation"/>, check each descriptor a shader reads in the shader itself (index in range,
	/// written, resource alive) and buffer accesses through descriptors. Off, the descriptors shaders read are still noted on
	/// the GPU and checked on the CPU after the submission (never written, resource gone), with no lookups of the layer's own
	/// in the shader: on RDNA4 (driver 26.11) those lookups make the GPU reset, so tests of sampled bindless reads turn
	/// them off. On by default.
	/// </summary>
	public bool GpuAssistedDescriptorChecks { get; init; } = true;

	/// <summary>
	/// Write progress markers (VK_AMD_buffer_marker, when the device has it) into host memory at each list's start and end
	/// and at every label, so a report after the device is lost shows how far the GPU got in each list
	/// (<see cref="Device.DescribeRecentSubmissions"/>).
	/// </summary>
	public bool ProgressMarkers { get; init; }

	/// <summary>
	/// Watch the frames from a thread of the device's own: one that goes on over a second is noted (where the frame thread
	/// was in the device, how far each queue had got) among a loss report's events and in a warning when it ends, so a GPU
	/// timeout that comes while the frame thread waits on the driver says where it waited.
	/// </summary>
	public bool HangWatch { get; init; }

	/// <summary>
	/// Keep the driver's statistics for each pipeline's shaders (VK_KHR_pipeline_executable_properties, when the device
	/// has it): registers, spills, wave size and the like, in <see cref="Device.ShaderStatistics"/>. For measuring.
	/// </summary>
	public bool ShaderStatistics { get; init; }

	/// <summary>
	/// An artificial video memory budget in bytes, or 0 for the driver's: device-local allocations past it fail as they would
	/// when video memory is full, so a run can be tried out of memory on any machine (textures and geometry go to system
	/// memory and streaming drops detail). From <c>-borealisgpu-vrambudget &lt;MiB&gt;</c> or <c>BOREALIS_GPU_MEMORY_BUDGET</c> (MiB).
	/// </summary>
	public long MemoryBudgetBytes { get; init; } = MemoryBudgetSwitch();

	/// <summary>
	/// Note each buffer's GPU address range (Vulkan 1.2's buffer device addresses), so the device-loss report names the
	/// buffer a fault address lies in, or lay in before it was freed: drivers' address binding reports name only images.
	/// For device-loss hunts. From <c>-borealisgpu-bufferaddresses</c> or <c>BOREALIS_GPU_BUFFER_ADDRESSES=1</c>.
	/// </summary>
	public bool BufferAddresses { get; init; } = Environment.GetCommandLineArgs().Contains( "-borealisgpu-bufferaddresses", StringComparer.OrdinalIgnoreCase )
		|| Environment.GetEnvironmentVariable( "BOREALIS_GPU_BUFFER_ADDRESSES" ) == "1";

	static long MemoryBudgetSwitch()
	{
		var args = Environment.GetCommandLineArgs();
		var at = Array.FindIndex( args, a => a.Equals( "-borealisgpu-vrambudget", StringComparison.OrdinalIgnoreCase ) );
		var text = at >= 0 && at + 1 < args.Length ? args[at + 1] : Environment.GetEnvironmentVariable( "BOREALIS_GPU_MEMORY_BUDGET" );
		return long.TryParse( text, out var mebibytes ) && mebibytes > 0 ? mebibytes << 20 : 0;
	}

	/// <summary>
	/// Hardware performance counters per GPU scope (<see cref="Device.Counters"/>, AMD's GPUPerfAPI on AMD GPUs): a preset
	/// (<see cref="GpuCounters.Presets"/>), counter names, or both, separated by commas; <c>list</c> also lists every counter
	/// the device has. Null leaves them off: no library is loaded and nothing is added to the device. From
	/// <c>-gpucounters [selection]</c> or <c>BOREALIS_GPU_COUNTERS</c>. See docs/reference/gpu-counters.md.
	/// </summary>
	public string GpuCounters { get; init; } = GpuCountersSwitch();

	static string GpuCountersSwitch()
	{
		var args = Environment.GetCommandLineArgs();
		var at = Array.FindIndex( args, a => a.Equals( "-gpucounters", StringComparison.OrdinalIgnoreCase ) );
		if ( at < 0 ) return Environment.GetEnvironmentVariable( "BOREALIS_GPU_COUNTERS" ) is { Length: > 0 } text && text != "0" ? text : null;
		return at + 1 < args.Length && !args[at + 1].StartsWith( '-' ) && !args[at + 1].StartsWith( '+' ) ? args[at + 1] : "default";
	}

	/// <summary>Frames the CPU may record ahead of the GPU.</summary>
	public int FramesInFlight { get; init; } = 2;

	/// <summary>Enable task and mesh shaders (VK_EXT_mesh_shader) where the device has them; off leaves them out.</summary>
	public bool MeshShaders { get; init; } = true;

	/// <summary>
	/// Link graphics pipelines from parts (VK_EXT_graphics_pipeline_library) where the device links them fast; off makes
	/// each pipeline whole. See <see cref="DeviceCapabilities.PipelineLibraries"/>. Off by default when the process has the
	/// <c>-no-pipeline-library</c> switch or <c>BOREALIS_PIPELINE_LIBRARY=0</c>, for comparisons.
	/// Off on NVIDIA until its linked pipelines draw (<c>BOREALIS_PIPELINE_LIBRARY=1</c> turns them on there).
	/// </summary>
	public bool PipelineLibraries { get; init; } = !Environment.GetCommandLineArgs().Contains( "-no-pipeline-library", StringComparer.OrdinalIgnoreCase )
		&& Environment.GetEnvironmentVariable( "BOREALIS_PIPELINE_LIBRARY" ) != "0";

	/// <summary>
	/// Keep the pipeline parts pipelines linked in <c>pipeline-parts.txt</c> in <see cref="PipelineCacheFolder"/>, and make
	/// them on the builder threads in the next run as soon as their shaders' modules are made (vertex input and output parts
	/// when the device is). On unless <c>-no-part-manifest</c> or <c>BOREALIS_PART_MANIFEST=0</c>. Needs pipeline libraries.
	/// </summary>
	public bool PipelinePartManifest { get; init; } = !Environment.GetCommandLineArgs().Contains( "-no-part-manifest", StringComparer.OrdinalIgnoreCase )
		&& Environment.GetEnvironmentVariable( "BOREALIS_PART_MANIFEST" ) != "0";

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

	/// <summary>
	/// Instance extensions to enable where the loader has them, and leave out where it doesn't: what a library working on the
	/// device would like (a vendor upscaler, <see cref="Device.Native"/>).
	/// </summary>
	public IReadOnlyList<string> OptionalInstanceExtensions { get; init; }

	/// <summary>
	/// Device extensions to enable where the device has them, asked for once the adapter is chosen: given the
	/// <c>VkInstance</c> and <c>VkPhysicalDevice</c>, as a vendor upscaler asks for its own. Null for none.
	/// </summary>
	public Func<nint, nint, IReadOnlyList<string>> OptionalDeviceExtensions { get; init; }

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

	/// <summary>
	/// Called with a module's SPIR-V hash on the thread that made it, before it queues the part manifest's parts for it. For
	/// tests: holding the loader of the manifest's kept SPIR-V here opens the window between making a module and queuing.
	/// </summary>
	internal Action<UInt128> QueueingRecordedParts { get; init; }
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
	/// Whether graphics pipelines are linked from parts made once each and shared (VK_EXT_graphics_pipeline_library with
	/// fast linking): creating one is then a fast link, and an optimised link replaces it in the background
	/// (<see cref="Device.CreateGraphicsPipeline"/>, <see cref="Device.PreparePipelineParts"/>).
	/// </summary>
	public bool PipelineLibraries { get; init; }

	/// <summary>
	/// Whether vertex shaders can use wave (subgroup) vote and ballot operations. Most GPUs can; Lavapipe and some mobile
	/// GPUs only have them in fragment, compute, task and mesh shaders, and reject vertex modules that use them.
	/// </summary>
	public bool VertexWaveOps { get; init; }
}
