namespace Borealis.Gpu;

/// <summary>
/// One of the allocator's memory blocks as it was when <see cref="Device.MemoryMap"/> was asked: its size, what's allocated in
/// it (offset and size, in address order) and its largest free range. A dedicated block holds one resource's memory alone.
/// </summary>
public sealed class MemoryMapBlock
{
	/// <summary>The block's size in bytes.</summary>
	public long Size { get; init; }

	/// <summary>Bytes allocated in it.</summary>
	public long Used { get; init; }

	/// <summary>Its largest free range in bytes.</summary>
	public long LargestFree { get; init; }

	/// <summary>Whether it holds images (textures, render targets) rather than buffers.</summary>
	public bool Images { get; init; }

	/// <summary>Whether it's one resource's memory of its own.</summary>
	public bool Dedicated { get; init; }

	/// <summary>Whether it holds content (a map's textures and geometry), which is moved out of emptying blocks to free them.</summary>
	public bool Content { get; init; }

	/// <summary>Whether it's in system memory because video memory was full or over budget.</summary>
	public bool Fallback { get; init; }

	/// <summary>What's allocated in it: offset and size in bytes, in address order.</summary>
	public IReadOnlyList<(long Offset, long Size)> Allocations { get; init; } = [];
}
