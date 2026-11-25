namespace Borealis.Gpu;

/// <summary>
/// What a command list's barrier calls held, for profiling (<see cref="CommandList.BarrierDetail"/>,
/// <see cref="Device.RecordedBarriers"/>). An image barrier is counted once, by the first that fits: from
/// <c>Undefined</c> (a first use), after a write, from reads to a write, from reads to reads in the same layout (an
/// execution dependency only), or from reads to reads in another layout (what one read-only layout would remove).
/// </summary>
public struct BarrierCounts
{
	/// <summary><c>vkCmdPipelineBarrier2</c> calls.</summary>
	public long Calls;

	/// <summary>Calls with no work (a scope, dispatch, copy or clear) since the call before, in the same list.</summary>
	public long SameGap;

	/// <summary>Calls flushed early because a second move of an image already moving covered other subresources.</summary>
	public long SameImageSplits;

	/// <summary>Calls with a global memory barrier.</summary>
	public long Memory;

	/// <summary>Image barriers.</summary>
	public long Images;

	/// <summary>Buffer barriers.</summary>
	public long Buffers;

	/// <summary>Image barriers from <c>Undefined</c>: a first use, discarding what the image held.</summary>
	public long ImagesFromUndefined;

	/// <summary>Image barriers after a write.</summary>
	public long ImagesAfterWrite;

	/// <summary>Image barriers from reads to a write.</summary>
	public long ImagesReadToWrite;

	/// <summary>Image barriers from reads to reads in the same layout: an execution dependency only.</summary>
	public long ImagesReadToReadSameLayout;

	/// <summary>Image barriers from reads to reads in another layout, which one read-only layout would remove.</summary>
	public long ImagesReadToRead;

	/// <summary>Buffer barriers after a write.</summary>
	public long BuffersAfterWrite;

	/// <summary>Buffer barriers from reads to reads.</summary>
	public long BuffersReadToRead;

	/// <summary>Add another list's counts.</summary>
	public void Add( in BarrierCounts c )
	{
		Calls += c.Calls;
		SameGap += c.SameGap;
		SameImageSplits += c.SameImageSplits;
		Memory += c.Memory;
		Images += c.Images;
		Buffers += c.Buffers;
		ImagesFromUndefined += c.ImagesFromUndefined;
		ImagesAfterWrite += c.ImagesAfterWrite;
		ImagesReadToWrite += c.ImagesReadToWrite;
		ImagesReadToReadSameLayout += c.ImagesReadToReadSameLayout;
		ImagesReadToRead += c.ImagesReadToRead;
		BuffersAfterWrite += c.BuffersAfterWrite;
		BuffersReadToRead += c.BuffersReadToRead;
	}
}
