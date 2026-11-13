using Basalt.Gpu;
using Basalt.Source2.Shaders;

namespace Basalt.Backend;

/// <summary>
/// A view's GPU-driven calls' record memos (<see cref="BackendDevice.GpuDrivenRecordMemo"/>), one per kind of call: its
/// shader mode, whether it draws colour, and whether it draws ids or lists.
/// </summary>
internal sealed class GpuDrivenRecordMemos
{
	readonly List<(uint Mode, bool NoColor, bool Ids, bool Lists, GpuDrivenRecordMemo Memo)> calls = new();

	public GpuDrivenRecordMemo For( uint mode, bool noColor, bool ids, bool lists )
	{
		lock ( calls )
		{
			foreach ( var call in calls )
			{
				if ( call.Mode == mode && call.NoColor == noColor && call.Ids == ids && call.Lists == lists ) return call.Memo;
			}

			var made = new GpuDrivenRecordMemo();
			calls.Add( (mode, noColor, ids, lists, made) );
			return made;
		}
	}
}

/// <summary>
/// What a view's GPU-culled call looked up for each batch last time, by the batch's place in the call: its material
/// records, the aggregate's fragment records and its visibility, beside everything those lookups were keyed by.
/// A batch that matches all of it takes them from here rather than from the tables' dictionaries; any difference looks
/// them up again. The tables never drop a record, so a record found for the same keys is still the one they'd give.
/// Locked by the call using it; a call that finds it taken looks everything up.
/// </summary>
internal sealed class GpuDrivenRecordMemo
{
	public struct Slot
	{
		// What the lookups were keyed by
		public GpuMaterial Material;
		public int Version;

		/// <summary>The bucket's variant, or its surface pass: the stages whose material records these are.</summary>
		public object Stages;
		public bool DepthOnly;

		/// <summary>The bucket's attribute hashes for its vertex and pixel stage (<see cref="BackendContext"/>'s AttributeHash).</summary>
		public int VertexAttributes, PixelAttributes;
		public object Owner;
		public int Fragments;
		public PooledGeometry Pooled;
		public VertexLayout Layout;
		public ShaderVariant Classic;
		public bool Positions, Visibility;
		public uint Flags;

		// What they found
		public uint Vertex, Pixel, FragmentRecords, VisibilityFirst, VertexBase;
	}

	Slot[] slots = [];

	/// <summary>The slot of the call's <paramref name="index"/>th batch record.</summary>
	public ref Slot At( int index )
	{
		if ( index >= slots.Length ) Array.Resize( ref slots, Math.Max( index + 1, slots.Length * 2 ) );
		return ref slots[index];
	}
}
