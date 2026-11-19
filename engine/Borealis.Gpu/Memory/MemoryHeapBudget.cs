namespace Borealis.Gpu;

/// <summary>
/// A memory heap's budget and use (<see cref="Device.GetMemoryHeaps"/>). <see cref="Usage"/> and <see cref="Budget"/> come
/// from the driver (VK_EXT_memory_budget) every <see cref="Device.MemoryBudgetFrames"/> frames, with what this device's
/// allocator reserved since added in between; without the extension, usage is this device's alone and the budget 80% of
/// the heap.
/// </summary>
public readonly record struct MemoryHeapBudget
{
	/// <summary>The heap's index among the device's.</summary>
	public int Index { get; init; }

	/// <summary>Whether it's video memory.</summary>
	public bool DeviceLocal { get; init; }

	/// <summary>The heap's size in bytes.</summary>
	public long Size { get; init; }

	/// <summary>
	/// Bytes the process may use before the OS starts moving its memory out: the driver's budget, or less with
	/// <see cref="DeviceOptions.MemoryBudgetBytes"/> on a device-local heap.
	/// </summary>
	public long Budget { get; init; }

	/// <summary>Bytes the process uses (every device it has, Source 2's too), estimated since the driver last said.</summary>
	public long Usage { get; init; }

	/// <summary>Bytes this device's allocator has from the driver in it.</summary>
	public long Reserved { get; init; }

	/// <summary>Bytes of <see cref="Reserved"/> handed out to buffers and textures.</summary>
	public long Used { get; init; }

	/// <summary>Bytes reserved but not handed out: free room in part-used blocks, and empty blocks kept as spares.</summary>
	public long Stranded => Reserved - Used;
}
