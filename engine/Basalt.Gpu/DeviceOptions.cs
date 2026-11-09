namespace Basalt.Gpu;

/// <summary>
/// How to create a <see cref="Device"/>.
/// </summary>
public sealed class DeviceOptions
{
	/// <summary>The application's name, which drivers and tools see.</summary>
	public string ApplicationName { get; init; } = "Basalt";

	/// <summary>
	/// Turn on the Khronos validation layer, when it's installed, and route its messages to <see cref="Message"/>.
	/// </summary>
	public bool Validation { get; init; }

	/// <summary>
	/// With <see cref="Validation"/>, also check synchronisation (barriers, queue ordering). Slower.
	/// </summary>
	public bool SynchronizationValidation { get; init; }

	/// <summary>Frames the CPU may record ahead of the GPU.</summary>
	public int FramesInFlight { get; init; } = 2;

	/// <summary>Part of the adapter's name to prefer, or null for the first discrete GPU.</summary>
	public string PreferredAdapter { get; init; }

	/// <summary>Whether swap chains will be made: enables the surface extensions. Offscreen devices can leave it off.</summary>
	public bool Presentation { get; init; } = true;

	/// <summary>Pipeline cache data saved from <see cref="Device.GetPipelineCacheData"/>, or null.</summary>
	public byte[] PipelineCacheData { get; init; }

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

	/// <summary>Nanoseconds per timestamp tick.</summary>
	public float TimestampPeriod { get; init; }

	/// <summary>The largest 2D texture.</summary>
	public int MaxTextureSize { get; init; }

	/// <summary>Sample counts colour targets support, as a bit mask of 1, 2, 4, 8...</summary>
	public int ColorSampleCounts { get; init; }

	/// <summary>Constant buffer offsets must be multiples of this.</summary>
	public int UniformAlignment { get; init; }

	/// <summary>Storage buffer offsets must be multiples of this.</summary>
	public int StorageAlignment { get; init; }

	/// <summary>How many sampled textures the bindless table holds.</summary>
	public int BindlessTextures { get; init; }

	/// <summary>How many samplers the bindless table holds.</summary>
	public int BindlessSamplers { get; init; }

	/// <summary>How many storage textures the bindless table holds.</summary>
	public int BindlessStorageTextures { get; init; }
}
