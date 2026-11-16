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

	/// <summary>Byte offsets in its plan's records of its entry records (<see cref="ResidentCullEntryRecord"/>) and its batches' headers.</summary>
	public uint EntryRecords, Headers;
}

/// <summary>
/// An entry of an item: its batch record and its place in the batch's fragments or meshes, 8 bytes. A forward entry whose
/// prepass draws ids for it has <see cref="PrepassIds"/> set in its batch word.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullEntry
{
	public uint Batch, Element;

	/// <summary>Set in <see cref="Batch"/>: the view's prepass draws ids for it, so a view listing from the prepass's records lists it.</summary>
	public const uint PrepassIds = 0x80000000u;
}

/// <summary>
/// What the resident cull reads of an entry's fragment or resident mesh and draw, 48 bytes in its plan's records after the
/// moved words: copied there from the fragment and draw tables (host memory) by the setup that writes the plan's records,
/// in an expand dispatch (<see cref="ResidentCullConstants.Expand"/>). Each resident mesh batch has a 32-byte header after
/// them, its group's header record (LOD count and switch distances), at its batch's index.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullEntryRecord
{
	/// <summary>Bounds centre and transform slot.</summary>
	public float CenterX, CenterY, CenterZ;
	public uint Transform;

	/// <summary>Bounds extents and draw record.</summary>
	public float ExtentsX, ExtentsY, ExtentsZ;
	public uint Draw;

	/// <summary>The draw record's first index, index count and base vertex; a resident mesh's LOD scale, or 1 for a fragment that casts no shadow.</summary>
	public uint FirstIndex, IndexCount, BaseVertex, Last;
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

	/// <summary>
	/// 1: it lists what its prepass draws ids for (<see cref="ResidentCullEntry.PrepassIds"/>), whose culling is in the same
	/// dispatch, instead of what the prepass's culling stamped. 2: a prepass that stamps what it keeps, for its forward pass.
	/// </summary>
	public uint Flags;
}

/// <summary><c>borealis_resident_cull_cs</c>'s <c>BorealisResidentCull_t</c> (<see cref="HandFilledConstants"/>).</summary>
[StructLayout( LayoutKind.Sequential )]
internal struct ResidentCullConstants
{
	/// <summary>Byte offset of the items, how many, byte offset of the views, and threads (every item's entries).</summary>
	public uint Items, ItemCount, Views, Entries;

	/// <summary>
	/// Bindless slots of the fragment and draw tables, the frame's stamp, and the bindless slot of the frame's region (read
	/// there where a whole wave reads the same words).
	/// </summary>
	public uint Fragments, Draws, Frame, FrameSlot;

	/// <summary>
	/// Not 0: an expand dispatch over one plan's entries, with its records buffer bound as the frame's region: the byte
	/// offsets of its entry records (<see cref="ResidentCullEntryRecord"/>), its entry map and its batches' headers.
	/// <see cref="Entries"/> is its entries; the other fields aren't read.
	/// </summary>
	public uint Expand, ExpandMap, ExpandHeaders, MeshletRuns;

	/// <summary>The generated LOD table's bindless slot (none when there's none) and the draw records it covers (<see cref="Lods.LodStore.Table"/>).</summary>
	public uint LodTable, LodCount, Pad0, Pad1;
}
