using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// One plan's batches in the resident cull (<c>borealis_resident_cull_cs</c>), 32 bytes in the frame's region: a thread per
/// entry (an element of one of its batch records) loops over its views. Items are in order of <see cref="FirstEntry"/>,
/// each starting on a multiple of 64 threads.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullItem
{
	/// <summary>Its first entry's thread in the dispatch, and its entries: every element of its batch records.</summary>
	public uint FirstEntry, Entries;

	/// <summary>
	/// The bindless slot of its plan's records (its batch records, 32 bytes each, then its entry map of
	/// <see cref="ResidentCullEntry"/>, then a resident filter's moved words, which a record's sixth word finds), and the
	/// entry map's byte offset in them.
	/// </summary>
	public uint Records, EntryMap;

	/// <summary>Its first view (<see cref="ResidentCullView"/>) and how many views cull it.</summary>
	public uint FirstView, Views;

	public uint Pad0, Pad1;
}

/// <summary>An entry of an item: its batch record and its place in the batch's fragments or meshes, 8 bytes.</summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullEntry
{
	public uint Batch, Element;
}

/// <summary>
/// One view that culls an item, 32 bytes: where its view record is, and where the call that draws it reads what the cull
/// writes (as a call's own region in <c>borealis_gpu_driven_cull_cs</c>). Byte offsets are in the frame's region.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullView
{
	/// <summary>The 160-byte view record: planes, camera and size threshold, flags, LOD camera and width.</summary>
	public uint View;

	/// <summary>Its instance records (8 bytes a slot), commands (20 bytes a slot) and multi-draws (first slot and count).</summary>
	public uint Instances, Commands, Runs;

	/// <summary>Its first instance record's index in the region, and the byte offset of instance 0's fragment word (ids).</summary>
	public uint FirstInstance, FragmentWords;

	/// <summary>Its listed fragments for forward's compaction: dispatch arguments, then 32 bytes each.</summary>
	public uint Listed;

	public uint Pad0;
}

/// <summary><c>borealis_resident_cull_cs</c>'s <c>BorealisResidentCull_t</c> (<see cref="HandFilledConstants"/>).</summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullConstants
{
	/// <summary>Byte offset of the items, how many, byte offset of the views, and threads (every item's entries).</summary>
	public uint Items, ItemCount, Views, Entries;

	/// <summary>Bindless slots of the fragment and draw tables, and the frame's stamp.</summary>
	public uint Fragments, Draws, Frame, Pad0;
}
