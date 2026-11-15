using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Borealis.Gpu;

/// <summary>
/// Transforms and per-view instance IDs (<c>transform_buffer.fxc</c>). Views reference entries through <c>ids[first + i]</c>.
/// See <c>docs/native/instancing.md</c>.
/// <para>
/// Rigid meshes keep an entry between frames, in a slot that stays theirs while they're drawn, rewritten only when the world's
/// entry version for them changes (<see cref="KeptSlotFor"/>), and aggregates a run of them, one per fragment
/// (<see cref="KeptRunFor"/>); the GPU buffers get only the kept entries written since each was last current. Everything
/// else - skinned bones, custom objects, draw tints, debug tints - is written each frame, into the frame's entries after
/// the kept ones (<see cref="FrameBase"/>).
/// </para>
/// </summary>
internal sealed class TransformBuffer : IDisposable
{
	/// <summary>
	/// Native <c>SceneSystemTransformEntry_t</c>: row-major 3x4 transform and instance data.
	/// </summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct Entry
	{
		public Vector4 Row0;
		public Vector4 Row1;
		public Vector4 Row2;
		public float Alpha;
		public uint TintRgb888;
		public uint VertexCacheOffset;

		/// <summary>
		/// Nonzero enables skinning with this many weights; bone matrices start two entries later.
		/// </summary>
		public uint BlendWeightCount;
	}

	const int FramesInFlight = 3;
	static readonly int EntrySize = Marshal.SizeOf<Entry>();

	readonly GpuBuffer<Entry>[] buffers = new GpuBuffer<Entry>[FramesInFlight];

	/// <summary>
	/// The frame each of <see cref="buffers"/> was last current for.
	/// </summary>
	readonly int[] bufferFrames = new int[FramesInFlight];
	int ring;

	readonly UploadRing<uint> idRing = new( "Borealis instance ids", GpuBuffer.UsageFlags.Vertex );
	Entry[] entries = new Entry[256];
	uint[] ids = new uint[256];

	/// <summary>
	/// Uploaded instance IDs. Owned here to avoid the scenesystem's fixed capacity.
	/// </summary>
	public GpuBuffer<uint> InstanceIds => idRing.Current;

	/// <summary>
	/// Frame entries written this frame.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Instance ids written this frame.
	/// </summary>
	public int InstanceCount { get; private set; }

	/// <summary>
	/// The buffer this frame's entries went into, after <see cref="Upload"/>.
	/// </summary>
	public GpuBuffer<Entry> Current { get; private set; }

	/// <summary>
	/// Globally unique frame generation, preventing slot reuse across cameras' buffers.
	/// </summary>
	public int Generation { get; private set; }

	static int lastGeneration;

	/// <summary>
	/// The first frame entry's slot: the kept entries come first, and their number only changes between frames.
	/// </summary>
	public int FrameBase => keptCapacity;

	public void Clear()
	{
		Count = 0;
		InstanceCount = 0;
		Generation = Interlocked.Increment( ref lastGeneration );

		frame++;
		dirty[frame % FramesInFlight].Clear();
		if ( wanted > 0 ) GrowKept( wanted );
		wanted = 0;
		if ( wantedRuns.Count > 0 ) MakeRuns();
		if ( frame % SweepInterval == 0 ) Sweep();
	}

	// Kept entries, by slot, and whose each is, and the frame it was last drawn in
	Entry[] kept = [];
	RenderObject[] owners = [];
	int[] used = [];
	int keptCapacity;

	/// <summary>
	/// By slot: the length of the run that starts there (<see cref="KeptRunFor"/>), -1 for a run's later slots, 0 for a
	/// slot of its own. A run's first slot says when it was last drawn for all of it.
	/// </summary>
	int[] runs = [];

	/// <summary>
	/// Objects drawn this frame that wanted a run of kept slots: made before the next frame.
	/// </summary>
	readonly List<(RenderObject Owner, int Index, int Count)> wantedRuns = new();

	/// <summary>Runs given back, by length, for another of the same length.</summary>
	readonly Dictionary<int, Stack<int>> freeRuns = new();

	/// <summary>Kept slots made for runs and not yet taken by one: [reserve, reserveEnd).</summary>
	int reserve, reserveEnd;

	// Slots no one has, as a stack
	int[] free = [];
	int freeCount;

	/// <summary>
	/// Each object's kept slot and the entry version it holds, by world index. The slot is the object's while
	/// <see cref="owners"/> says so.
	/// </summary>
	(int Slot, int Version)[] keptByIndex = [];

	/// <summary>
	/// Objects drawn this frame that wanted a kept slot when none was free: the slots made before the next frame.
	/// </summary>
	int wanted;

	/// <summary>
	/// This buffer's frames, and the kept slots written in each of the last <see cref="FramesInFlight"/>: a GPU buffer last
	/// current three frames ago needs those.
	/// </summary>
	int frame;
	readonly List<int>[] dirty = [new(), new(), new()];

	/// <summary>
	/// Frames a kept slot can go undrawn before it's given back, and how often to look for those.
	/// </summary>
	internal const int SlotLifetime = 256;
	const int SweepInterval = 64;

	/// <summary>
	/// Kept slots made so far and kept entries written, for the tests.
	/// </summary>
	internal int KeptCapacity => keptCapacity;
	internal int KeptWrites { get; private set; }

	/// <summary>
	/// A rigid object's kept entry, by its world index: its slot, rewritten only when <paramref name="version"/> isn't the one it
	/// holds. An object without one takes a free slot, or this frame a frame entry when there's none, and a slot is made for it
	/// before the next frame. Reads nothing of the object while its entry is current.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public int KeptSlotFor( int index, MeshObject obj, in Matrix m, int version )
	{
		if ( (uint)index >= (uint)keptByIndex.Length ) Array.Resize( ref keptByIndex, Math.Max( index + 1, keptByIndex.Length * 2 ) );

		ref var keep = ref keptByIndex[index];
		var slot = keep.Slot;
		if ( (uint)slot < (uint)keptCapacity && owners[slot] == obj )
		{
			if ( keep.Version != version )
			{
				Write( slot, m, obj.Tint );
				keep.Version = version;
			}

			used[slot] = frame;
			return slot;
		}

		if ( freeCount == 0 )
		{
			if ( ObjectSlot( obj ).Generation != Generation ) wanted++;
			return SlotFor( obj, m, obj.Tint );
		}

		slot = free[--freeCount];
		owners[slot] = obj;
		used[slot] = frame;
		keep = (slot, version);
		Write( slot, m, obj.Tint );
		return slot;
	}

	void Write( int slot, in Matrix m, Color tint )
	{
		kept[slot] = MakeEntry( m, tint );
		dirty[frame % FramesInFlight].Add( slot );
		KeptWrites++;
	}

	/// <summary>
	/// An object's run of <paramref name="count"/> kept entries, by its world index (an aggregate's, a slot per fragment in
	/// order): its first slot, with <paramref name="write"/> set when <paramref name="version"/> isn't the one the entries
	/// hold, for the caller to rewrite them (<see cref="WriteKept(int, in Matrix3x4, Color)"/>). -1 when it has none yet: the caller writes frame
	/// entries this frame, and the run is made before the next.
	/// </summary>
	public int KeptRunFor( int index, RenderObject obj, int count, int version, out bool write )
	{
		if ( (uint)index >= (uint)keptByIndex.Length ) Array.Resize( ref keptByIndex, Math.Max( index + 1, keptByIndex.Length * 2 ) );

		ref var keep = ref keptByIndex[index];
		var slot = keep.Slot;
		if ( count > 0 && (uint)slot < (uint)keptCapacity && owners[slot] == obj && runs[slot] == count )
		{
			write = keep.Version != version;
			keep.Version = version;
			used[slot] = frame;
			return slot;
		}

		write = false;
		if ( count > 0 ) wantedRuns.Add( (obj, index, count) );
		return -1;
	}

	/// <summary>Write a kept entry of a run from a world transform and a tint.</summary>
	public void WriteKept( int slot, in Matrix m, Color tint ) => Write( slot, m, tint );

	/// <summary>Write a kept entry of a run from a transform in native's row layout and a tint.</summary>
	public void WriteKept( int slot, in Matrix3x4 m, Color tint )
	{
		kept[slot] = new Entry { Row0 = m.Row0, Row1 = m.Row1, Row2 = m.Row2, Alpha = tint.a, TintRgb888 = PackRgb888( tint ) };
		dirty[frame % FramesInFlight].Add( slot );
		KeptWrites++;
	}

	/// <summary>
	/// The runs asked for last frame, between frames, each in one piece with no entry written yet: one given back of the
	/// same length, else from the slots kept for runs, which grow by half at least when they run out.
	/// </summary>
	void MakeRuns()
	{
		foreach ( var (owner, index, count) in wantedRuns )
		{
			int start;
			if ( freeRuns.TryGetValue( count, out var given ) && given.Count > 0 )
			{
				start = given.Pop();
			}
			else
			{
				if ( reserveEnd - reserve < count )
				{
					// What's left of the old reserve becomes slots of their own
					for ( int slot = reserveEnd - 1; slot >= reserve; slot-- ) free[freeCount++] = slot;
					reserve = keptCapacity;
					GrowKept( count, keepFirst: int.MaxValue );
					reserveEnd = keptCapacity;
				}

				start = reserve;
				reserve += count;
			}

			if ( (uint)index >= (uint)keptByIndex.Length ) Array.Resize( ref keptByIndex, Math.Max( index + 1, keptByIndex.Length * 2 ) );
			for ( int i = 0; i < count; i++ )
			{
				owners[start + i] = owner;
				runs[start + i] = i == 0 ? count : -1;
			}

			used[start] = frame;
			keptByIndex[index] = (start, int.MinValue);
		}

		wantedRuns.Clear();
	}

	/// <summary>
	/// Make <paramref name="count"/> or more kept slots, between frames. The kept entries grow by half at least, so growing
	/// slot by slot doesn't copy them every frame.
	/// </summary>
	void GrowKept( int count, int keepFirst = 0 )
	{
		var capacity = keptCapacity + Math.Max( count, Math.Max( keptCapacity / 2, 64 ) );
		Array.Resize( ref kept, capacity );
		Array.Resize( ref owners, capacity );
		Array.Resize( ref used, capacity );
		Array.Resize( ref runs, capacity );
		Array.Resize( ref free, capacity );

		// Lowest first, but for the first keepFirst new slots, which runs take
		for ( int slot = capacity - 1; slot >= keptCapacity + (long)keepFirst; slot-- )
			free[freeCount++] = slot;

		keptCapacity = capacity;
	}

	/// <summary>
	/// Give back the kept slots of objects not drawn for <see cref="SlotLifetime"/> frames: removed, or out of view.
	/// </summary>
	void Sweep()
	{
		for ( int slot = 0; slot < keptCapacity; slot++ )
		{
			// A run's later slots go with its first, and it's kept whole for another run as long
			if ( runs[slot] < 0 || owners[slot] is null || frame - used[slot] < SlotLifetime ) continue;

			if ( runs[slot] > 0 )
			{
				for ( int i = 0; i < runs[slot]; i++ ) owners[slot + i] = null;
				if ( !freeRuns.TryGetValue( runs[slot], out var given ) ) freeRuns[runs[slot]] = given = new();
				given.Push( slot );
				continue;
			}

			owners[slot] = null;
			free[freeCount++] = slot;
		}
	}

	/// <summary>
	/// Each object's frame entry this frame, by its world index, and the <see cref="Generation"/> it was written in. Kept
	/// here rather than on the objects: writing an object while workers read it to cull shadow views makes the cores fight
	/// over its memory.
	/// </summary>
	(int Generation, int Slot)[] objectSlots = [];

	/// <summary>
	/// An object's frame entry this frame: written the first time a view asks for it, the same slot after that.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public int SlotFor( RenderObject obj, in Matrix m, Color tint )
	{
		ref var entry = ref ObjectSlot( obj );
		if ( entry.Generation == Generation ) return entry.Slot;

		entry = (Generation, Add( m, tint ));
		return entry.Slot;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	ref (int Generation, int Slot) ObjectSlot( RenderObject obj )
	{
		var index = obj.Index;
		if ( (uint)index >= (uint)objectSlots.Length ) Array.Resize( ref objectSlots, Math.Max( index + 1, objectSlots.Length * 2 ) );
		return ref objectSlots[index];
	}

	/// <summary>
	/// The entry an object was given this frame, kept or not, or -1 - for the tests.
	/// </summary>
	internal int SlotOf( RenderObject obj )
	{
		var index = obj.Index;
		if ( (uint)index < (uint)keptByIndex.Length && keptByIndex[index].Slot is var slot && (uint)slot < (uint)keptCapacity && owners[slot] == obj && used[slot] == frame )
			return slot;

		return (uint)index < (uint)objectSlots.Length && objectSlots[index].Generation == Generation ? objectSlots[index].Slot : -1;
	}

	/// <summary>
	/// Write or reuse rigid per-draw entries across all LODs, combining object and draw tints
	/// (<c>CBaseSceneObjectDesc</c>). Returns the first slot.
	/// </summary>
	public int TintedSlotsFor( RenderObject obj, in Matrix m, Color tint, RenderMesh mesh )
	{
		ref var slot = ref ObjectSlot( obj );
		if ( slot.Generation == Generation ) return slot.Slot;

		slot = (Generation, FrameBase + Count);
		for ( int lod = 0; lod < mesh.LodCount; lod++ )
		{
			foreach ( var draw in mesh.DrawsForLod( lod ) )
			{
				var drawTint = draw.Tint ?? Vector3.One;
				Add( m, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
			}
		}

		return slot.Slot;
	}

	/// <summary>
	/// Append a transform slot to the instance-ID stream and return its index.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public int AddInstance( int slot )
	{
		if ( InstanceCount == ids.Length ) Array.Resize( ref ids, ids.Length * 2 );

		ids[InstanceCount] = (uint)slot;
		return InstanceCount++;
	}

	/// <summary>
	/// Write a frame entry: a transform and tint, returning its slot.
	/// </summary>
	public int Add( in Matrix m, Color tint )
	{
		if ( Count == entries.Length ) Array.Resize( ref entries, entries.Length * 2 );

		// Most objects share a tint: pack it once
		if ( tint.r != packedTint.r || tint.g != packedTint.g || tint.b != packedTint.b )
		{
			packedTint = tint;
			packedRgb = PackRgb888( tint );
		}

		ref var entry = ref entries[Count];
		entry = MakeEntry( m, packedRgb );
		entry.Alpha = tint.a;
		return FrameBase + Count++;
	}

	Color packedTint = Color.White;
	uint packedRgb = PackRgb888( Color.White );

	/// <summary>
	/// Write a frame entry from a transform already in native's row layout (<c>matrix3x4_t</c>) and a tint, returning its slot.
	/// </summary>
	public int Add( in Matrix3x4 m, Color tint )
	{
		if ( Count == entries.Length ) Array.Resize( ref entries, entries.Length * 2 );

		entries[Count] = new Entry { Row0 = m.Row0, Row1 = m.Row1, Row2 = m.Row2, Alpha = tint.a, TintRgb888 = PackRgb888( tint ) };
		return FrameBase + Count++;
	}

	/// <summary>
	/// A rigid object's entry: its matrix and tint, with no skinning.
	/// </summary>
	internal static Entry MakeEntry( in Matrix m, Color tint )
	{
		var entry = MakeEntry( m, PackRgb888( tint ) );
		entry.Alpha = tint.a;
		return entry;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	static Entry MakeEntry( in Matrix m, uint tintRgb888 )
	{
		// Transpose into native column-vector layout.
		return new Entry
		{
			Row0 = new( m.M11, m.M21, m.M31, m.M41 ),
			Row1 = new( m.M12, m.M22, m.M32, m.M42 ),
			Row2 = new( m.M13, m.M23, m.M33, m.M43 ),
			TintRgb888 = tintRgb888,
		};
	}

	/// <summary>
	/// Append base, morph and bone frame entries; return the base slot
	/// (<c>CAnimatableSceneObjectDesc::AllocateTransforms</c>, <c>SetupBones</c>). Vertex-cache offset is assigned later.
	/// </summary>
	public int AddSkinned( Color tint, int blendWeights, ReadOnlySpan<Matrix3x4> bones )
	{
		var count = 2 + bones.Length;
		while ( Count + count > entries.Length ) Array.Resize( ref entries, entries.Length * 2 );

		var local = Count;
		ref var first = ref entries[local];
		first = default;

		// Match native's base-entry animation fields.
		first.Row0 = new( 0, 0, 1, 1 );
		first.Alpha = tint.a;
		first.TintRgb888 = PackRgb888( tint );
		first.VertexCacheOffset = uint.MaxValue;
		first.BlendWeightCount = (uint)blendWeights;

		entries[local + 1] = default;

		for ( int i = 0; i < bones.Length; i++ )
		{
			ref var bone = ref entries[local + 2 + i];
			bone = default;
			bone.Row0 = bones[i].Row0;
			bone.Row1 = bones[i].Row1;
			bone.Row2 = bones[i].Row2;
		}

		// SetupBones copies the extra data into the first bone too
		if ( bones.Length > 0 )
		{
			ref var bone = ref entries[local + 2];
			bone.Alpha = first.Alpha;
			bone.TintRgb888 = first.TintRgb888;
			bone.VertexCacheOffset = first.VertexCacheOffset;
			bone.BlendWeightCount = first.BlendWeightCount;
		}

		Count += count;
		return FrameBase + local;
	}

	/// <summary>
	/// Store atlas U/V offsets and ranges in the morph entry (<c>CalculateMorphSubrectData</c>).
	/// </summary>
	public void SetMorphSubrect( int baseSlot, Vector4 subrect )
	{
		ref var entry = ref entries[baseSlot - FrameBase + 1];
		entry.Row0.z = subrect.x;
		entry.Row0.w = subrect.y;
		entry.Row1.w = subrect.z;
		entry.Row2.w = subrect.w;
	}

	/// <summary>
	/// Assign a skinned entry's vertex-cache block before upload.
	/// </summary>
	public void SetVertexCacheOffset( int slot, int offset ) => entries[slot - FrameBase].VertexCacheOffset = (uint)offset;

	/// <summary>
	/// An entry as written - for the tests.
	/// </summary>
	internal Entry this[int slot] => slot < keptCapacity ? kept[slot] : entries[slot - FrameBase];

	/// <summary>
	/// The entry an instance reads - for the tests.
	/// </summary>
	internal int InstanceSlot( int instance ) => (int)ids[instance];

	int[] dirtyScratch = [];

	/// <summary>
	/// Kept slots uploaded in the last <see cref="Upload"/>, for the tests: all of them for a buffer that's new or missed frames,
	/// else the ones written since it was last current.
	/// </summary>
	internal int KeptUploaded { get; private set; }

	/// <summary>
	/// Copy this frame's entries and instance ids to the GPU: into the next of the buffers frames in flight read, the kept
	/// entries written since it was last current, coalesced into ranges, then the frame entries after them.
	/// </summary>
	public void Upload( GpuContext context )
	{
		ring = (ring + 1) % FramesInFlight;
		ref var buffer = ref buffers[ring];

		var needed = keptCapacity + Count;
		var full = false;
		if ( buffer is null || buffer.ElementCount < needed )
		{
			// The device keeps a buffer in-flight frames use until they're done
			var size = Math.Max( Math.Max( needed, 256 ), (buffer?.ElementCount ?? 0) * 2 );
			buffer?.Dispose();
			buffer = new GpuBuffer<Entry>( size, GpuBuffer.UsageFlags.Structured, "Borealis transforms" );
			full = true;
		}

		// The writes of the frames since it was last current are all it lacks, if they're still known
		full |= frame - bufferFrames[ring] > FramesInFlight;
		bufferFrames[ring] = frame;

		KeptUploaded = 0;
		if ( keptCapacity > 0 )
		{
			if ( full ) UploadKept( context, buffer, 0, keptCapacity );
			else UploadDirty( context, buffer );
		}

		if ( Count > 0 ) context.UploadBuffer( buffer, entries.AsSpan( 0, Count ), keptCapacity * EntrySize );
		Current = buffer;

		idRing.Upload( context, ids.AsSpan( 0, InstanceCount ), ids.Length );
	}

	void UploadKept( GpuContext context, GpuBuffer<Entry> buffer, int start, int count )
	{
		context.UploadBuffer( buffer, kept.AsSpan( start, count ), start * EntrySize );
		KeptUploaded += count;
	}

	/// <summary>
	/// The kept slots written in the frames a buffer missed, sorted, in runs: slots a few apart go up as one copy.
	/// </summary>
	void UploadDirty( GpuContext context, GpuBuffer<Entry> buffer )
	{
		var total = dirty[0].Count + dirty[1].Count + dirty[2].Count;
		if ( total == 0 ) return;

		// Most of them: one copy
		if ( total > keptCapacity / 4 )
		{
			UploadKept( context, buffer, 0, keptCapacity );
			return;
		}

		if ( dirtyScratch.Length < total ) dirtyScratch = new int[Math.Max( total, dirtyScratch.Length * 2 )];
		var slots = dirtyScratch.AsSpan( 0, total );
		var at = 0;
		foreach ( var list in dirty )
		{
			CollectionsMarshal.AsSpan( list ).CopyTo( slots[at..] );
			at += list.Count;
		}

		slots.Sort();

		const int Gap = 8;
		var first = slots[0];
		var last = first;
		for ( int i = 1; i < slots.Length; i++ )
		{
			if ( slots[i] - last <= Gap )
			{
				last = slots[i];
				continue;
			}

			UploadKept( context, buffer, first, last - first + 1 );
			first = last = slots[i];
		}

		UploadKept( context, buffer, first, last - first + 1 );
	}

	/// <summary>
	/// Pack RGB with red in bits 16–23; clamp and round ties to even (<c>CreateExtraShaderData_TintRGBA32</c>, <c>RoundFloatToByte</c>).
	/// </summary>
	internal static uint PackRgb888( Color color ) => Channel( color.r ) << 16 | Channel( color.g ) << 8 | Channel( color.b );

	static uint Channel( float value ) => (uint)MathF.Round( Math.Clamp( value, 0, 1 ) * 255.0f );

	public void Dispose()
	{
		for ( int i = 0; i < buffers.Length; i++ )
		{
			buffers[i]?.Dispose();
			buffers[i] = null;
		}

		Current = null;
		idRing.Dispose();
	}
}
