using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// <see cref="ResidentCullItem"/>, <see cref="ResidentCullEntry"/>, <see cref="ResidentCullEntryRecord"/> and
/// <see cref="ResidentCullView"/>, the resident cull's records, are generated from <c>sbox/records.slangh</c> (Records.g.cs).
/// </summary>
internal partial struct ResidentCullEntry
{
	/// <summary>Set in <see cref="Batch"/>: the view's prepass draws ids for it, so a view listing from the prepass's records lists it.</summary>
	public const uint PrepassIds = 0x80000000u;
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
