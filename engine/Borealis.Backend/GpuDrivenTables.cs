using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// Where a draw's geometry is for GPU-driven drawing: its geometry record (byte offset in the geometry table), the index
/// page its indices are in, the page's slot in the raw buffer array, and its first index there; and the geometry record of
/// its positions-only copy (0 when there's none), which depth-only draws read; and the vertices its records hold.
/// </summary>
internal readonly record struct PooledGeometry( uint GeometryRecord, BufferHandle IndexPage, int IndexPageSlot, uint FirstIndex, IndexFormat IndexFormat, uint PositionsRecord = 0, int VertexCount = 0 );

/// <summary>
/// The buffers GPU-driven drawing reads (<c>borealis_gpu_driven.fxc</c>, docs/managed/borealis-gpu-driven.md): meshes'
/// vertices and indices in shared pages, and the geometry, draw and material tables, all reached through the bindless raw
/// buffer array (<c>g_bindless_VertexBuffers</c>, binding 63). Tables only grow: a record, once written, is read by every
/// later frame, so nothing is uploaded per frame but each frame's instance records and indirect commands.
/// <para>
/// Segments record on several threads at once. Locks are taken in one order: an owner's draws, then the draw records,
/// then a table, then the device's own. Callbacks that write records (<see cref="Material"/>, <see cref="FragmentsOf"/>)
/// run outside every lock here, since they read materials and attributes and make draw records.
/// </para>
/// </summary>
internal sealed class GpuDrivenTables : IDisposable
{
	/// <summary>Vertex pages: within every device's storage buffer range; larger buffers get one of their own.</summary>
	public const int VertexPageSize = 8 << 20;

	/// <summary>Index pages: one multi-draw per page per bucket, so few pages means few draws.</summary>
	public const int IndexPageSize = 32 << 20;

	public const int InstanceStride = 8;
	public const int DrawStride = 32;
	public const int CommandStride = 20;

	/// <summary>Bytes of a geometry record before its attributes: one bit per semantic.</summary>
	const int MaskBytes = 32;

	readonly Device gpu;
	readonly object pagesLock = new();
	readonly List<BufferHandle> owned = new();

	sealed class Page
	{
		public BufferHandle Buffer;
		public int Slot;
		public long Size;
		public long Used;

		/// <summary>Where meshes' own data stops: an index page's room for generated levels follows (<see cref="LevelRoom"/>).</summary>
		public long Limit;
		public long LevelUsed;
	}

	Page vertexPage;
	readonly Dictionary<IndexFormat, Page> indexPages = new();
	readonly Dictionary<int, Page> indexPagesBySlot = new();
	long pageBytes, levelBytes;

	/// <summary>
	/// A table in host-visible memory that only grows: records are appended and never change. Growing copies it into a
	/// bigger buffer with a slot of its own. The old buffer is kept until the tables are disposed: draws recorded earlier
	/// in the frame being recorded read it at its slot, and a destroy only waits for what's been submitted. It only doubles a
	/// few times, so that's at most its size again.
	/// </summary>
	sealed class Table( Device gpu, string name, int initial )
	{
		public BufferHandle Buffer;
		public int Slot = -1;
		public long Used;
		long size;
		readonly List<BufferHandle> retired = new();

		public uint Append( ReadOnlySpan<byte> record )
		{
			lock ( this )
			{
				var aligned = (record.Length + 15) & ~15;
				if ( Buffer.IsValid == false || Used + aligned > size ) Grow( Used + aligned );
				var offset = Used;
				record.CopyTo( gpu.Map( Buffer )[(int)offset..] );
				Used += aligned;
				return (uint)offset;
			}
		}

		/// <summary>Read back records appended earlier, at <paramref name="offset"/>.</summary>
		public void Read( uint offset, Span<byte> record )
		{
			lock ( this ) gpu.Map( Buffer ).Slice( (int)offset, record.Length ).CopyTo( record );
		}

		/// <summary>Write over records appended earlier, at <paramref name="offset"/> (a released run, reused).</summary>
		public void Write( uint offset, ReadOnlySpan<byte> record )
		{
			lock ( this ) record.CopyTo( gpu.Map( Buffer )[(int)offset..] );
		}

		void Grow( long needed )
		{
			var next = Math.Max( initial, size * 2 );
			while ( next < needed ) next *= 2;

			var buffer = gpu.CreateBuffer( new BufferInfo { Size = next, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = name } );
			if ( Buffer.IsValid )
			{
				gpu.Map( Buffer )[..(int)Used].CopyTo( gpu.Map( buffer ) );
				retired.Add( Buffer );
			}

			Buffer = buffer;
			size = next;
			Slot = gpu.BindlessIndex( buffer );
		}

		public void Dispose()
		{
			if ( Buffer.IsValid ) gpu.Destroy( Buffer );
			foreach ( var old in retired ) gpu.Destroy( old );
			retired.Clear();
		}
	}

	readonly Table geometry, draws, materials, fragments, lighting;
	readonly Dictionary<(int Page, long Offset, VertexLayout Layout, int Count), uint> geometryRecords = new();
	// Draw and material records are found without a lock by every recording thread, many times a frame (a lock per draw
	// kept recording threads waiting on each other); added to under a lock of their own
	readonly System.Collections.Concurrent.ConcurrentDictionary<(uint Geometry, uint Vertex, uint Pixel, uint Flags, uint FirstIndex, uint IndexCount, int BaseVertex, int IndexPage), uint> drawRecords = new();
	readonly object drawLock = new();
	readonly System.Collections.Concurrent.ConcurrentDictionary<MaterialRecordKey, uint> materialRecords = new();

	/// <summary>A material record: per material and its version, stage, and the attribute values its constants read.</summary>
	internal readonly record struct MaterialRecordKey( GpuMaterial Material, int Version, ShaderStage Stage, bool UncompressedTangentFrame, int Attributes );

	public GpuDrivenTables( Device gpu )
	{
		this.gpu = gpu;
		geometry = new Table( gpu, "Borealis GPU-driven geometry", 1 << 20 );
		draws = new Table( gpu, "Borealis GPU-driven draws", 1 << 20 );
		materials = new Table( gpu, "Borealis GPU-driven materials", 1 << 20 );
		fragments = new Table( gpu, "Borealis GPU-driven fragments", 1 << 20 );
		lighting = new Table( gpu, "Borealis GPU-driven lighting", 1 << 16 );

		// Made now, so their slots exist before any frame reads them; lighting record 0 is no lighting
		geometry.Append( new byte[16] );
		draws.Append( new byte[DrawStride] );
		materials.Append( new byte[16] );
		fragments.Append( new byte[FragmentStride] );
		lighting.Append( new byte[LightingStride] );
	}

	/// <summary>The lighting table's buffer now, for tests.</summary>
	internal BufferHandle LightingBuffer => lighting.Buffer;

	/// <summary>The tables' slots in the raw buffer array, as <c>BorealisGpuDriven_t</c> wants them after the instances'.</summary>
	public (int Geometry, int Draws, int Materials, int Lighting) Slots => (geometry.Slot, draws.Slot, materials.Slot, lighting.Slot);

	/// <summary>
	/// A lighting record: <c>BakedLightingConstantBuffer_t</c> (48 bytes), then <c>PerBatchLightProbeVolumeConstantBuffer_t</c>
	/// (112), as the attributes a draw reads hold them, which GPU-driven variants read per draw instead of binding.
	/// </summary>
	public const int LightingStride = 160;

	// Found without a lock, as draw records are; added to under lightingLock
	readonly System.Collections.Concurrent.ConcurrentDictionary<byte[], uint> lightingRecords = new( LightingComparer.Instance );
	readonly object lightingLock = new();

	// By contents, also looked up by a span (a record built on the stack), so finding a known record allocates nothing
	sealed class LightingComparer : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
	{
		public static readonly LightingComparer Instance = new();
		public bool Equals( byte[] a, byte[] b ) => a.AsSpan().SequenceEqual( b );
		public int GetHashCode( byte[] bytes ) => GetHashCode( bytes.AsSpan() );
		public bool Equals( ReadOnlySpan<byte> a, byte[] b ) => a.SequenceEqual( b );
		public byte[] Create( ReadOnlySpan<byte> bytes ) => bytes.ToArray();

		public int GetHashCode( ReadOnlySpan<byte> bytes )
		{
			var hash = new HashCode();
			hash.AddBytes( bytes );
			return hash.ToHashCode();
		}
	}

	/// <summary>
	/// The lighting record holding these constant buffers' bytes as a draw reads them (a missing one zero), in 16-byte units
	/// (a draw record's flags hold it from bit 8): made once per distinct contents; 0, all zeros, for none. Callers look it
	/// up once per run of draws with the same attributes.
	/// </summary>
	public uint Lighting( byte[] baked, byte[] probe )
	{
		if ( baked is null && probe is null ) return 0;
		Span<byte> record = stackalloc byte[LightingStride];
		record.Clear();
		baked?.AsSpan( 0, Math.Min( baked.Length, 48 ) ).CopyTo( record );
		probe?.AsSpan( 0, Math.Min( probe.Length, 112 ) ).CopyTo( record[48..] );
		var known = lightingRecords.GetAlternateLookup<ReadOnlySpan<byte>>();
		if ( known.TryGetValue( record, out var found ) ) return found;
		lock ( lightingLock )
		{
			if ( !known.TryGetValue( record, out var index ) ) known[record] = index = lighting.Append( record ) / 16;
			return index;
		}
	}
	/// <summary>Bytes of the tables, for reports.</summary>
	public (long Geometry, long Draws, long Materials, long Pages) Bytes
	{
		get
		{
			lock ( pagesLock ) return (geometry.Used, draws.Used, materials.Used, pageBytes);
		}
	}

	/// <summary>
	/// Room each index page keeps after meshes' indices for their generated levels (<see cref="Lods.LodStore"/>), as a
	/// fraction of what meshes fill: a level must be in its range's page, as a multi-draw binds one page. Set before any
	/// index page is made; 0 keeps none.
	/// </summary>
	public float LevelRoom { get; set; }

	/// <summary>Bytes of index pages' level room, and how much of it levels use, for reports.</summary>
	public (long Room, long Used) LevelBytes
	{
		get
		{
			lock ( pagesLock )
			{
				long room = 0;
				foreach ( var page in indexPagesBySlot.Values ) room += page.Size - page.Limit;
				return (room, levelBytes);
			}
		}
	}

	/// <summary>
	/// Room for <paramref name="bytes"/> of a generated level's indices in the index page at <paramref name="slot"/>, after
	/// its meshes' own: the page and the level's first index there; false when the page has no room left. The caller
	/// writes them, in a command list ordered before any draw of them.
	/// </summary>
	public bool TryAddLevelIndices( int slot, IndexFormat format, int bytes, out BufferHandle page, out uint firstIndex )
	{
		lock ( pagesLock )
		{
			page = default;
			firstIndex = 0;
			if ( !indexPagesBySlot.TryGetValue( slot, out var found ) ) return false;
			var offset = found.LevelUsed;
			var aligned = (bytes + 15) & ~15;
			if ( offset + aligned > found.Size ) return false;
			found.LevelUsed = offset + aligned;
			levelBytes += aligned;
			page = found.Buffer;
			firstIndex = (uint)(offset / (format == IndexFormat.UInt32 ? 4 : 2));
			return true;
		}
	}

	//
	// Geometry, at load
	//

	/// <summary>Put a mesh's vertex buffer in a page: its page's slot and its byte offset there.</summary>
	public (int Slot, long Offset) AddVertices( ReadOnlySpan<byte> data )
	{
		lock ( pagesLock )
		{
			var page = Place( ref vertexPage, data.Length, VertexPageSize, BufferUsage.Storage | BufferUsage.Vertex, "Borealis vertex page" );
			var offset = page.Used;
			if ( data.Length > 0 ) gpu.WriteBuffer( page.Buffer, data, offset );
			page.Used = (offset + data.Length + 15) & ~15;
			return (page.Slot, offset);
		}
	}

	/// <summary>
	/// Vertices in a page that's also bindable as a vertex buffer (the positions-only copies, which the classic path binds
	/// and GPU-driven draws pull): the page, where they start in it, and its slot.
	/// </summary>
	public (BufferHandle Buffer, long Offset, int Slot) AddVertexStream( ReadOnlySpan<byte> data )
	{
		lock ( pagesLock )
		{
			var page = Place( ref vertexPage, data.Length, VertexPageSize, BufferUsage.Storage | BufferUsage.Vertex, "Borealis vertex page" );
			var offset = page.Used;
			if ( data.Length > 0 ) gpu.WriteBuffer( page.Buffer, data, offset );
			page.Used = (offset + data.Length + 15) & ~15;
			return (page.Buffer, offset, page.Slot);
		}
	}

	/// <summary>Put a mesh's index buffer in a page of its index size: the page, its slot and its first index there.</summary>
	public (BufferHandle Page, int Slot, uint FirstIndex) AddIndices( ReadOnlySpan<byte> data, IndexFormat format )
	{
		lock ( pagesLock )
		{
			indexPages.TryGetValue( format, out var page );
			page = Place( ref page, data.Length, IndexPageSize, BufferUsage.Index | BufferUsage.Storage, $"Borealis index page {format}", LevelRoom );
			indexPages[format] = page;
			indexPagesBySlot[page.Slot] = page;
			var offset = page.Used;
			if ( data.Length > 0 ) gpu.WriteBuffer( page.Buffer, data, offset );
			page.Used = (offset + data.Length + 15) & ~15;
			return (page.Buffer, page.Slot, (uint)(offset / (format == IndexFormat.UInt32 ? 4 : 2)));
		}
	}

	/// <summary>
	/// Room for <paramref name="bytes"/>: the current page, a new one, or a buffer of its own when it's bigger than a page;
	/// a new one with <paramref name="room"/> of its meshes' size again after them.
	/// </summary>
	Page Place( ref Page current, int bytes, int pageSize, BufferUsage usage, string name, float room = 0 )
	{
		if ( current is not null && current.Used + bytes <= current.Limit ) return current;

		var limit = Math.Max( pageSize, (bytes + 15) & ~15 );
		var size = (limit + (long)(limit * Math.Max( room, 0 )) + 15) & ~15L;
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = usage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = name } );
		owned.Add( buffer );
		pageBytes += size;
		var page = new Page { Buffer = buffer, Slot = gpu.BindlessIndex( buffer ), Size = size, Limit = limit, LevelUsed = limit };

		// A buffer bigger than a page stands alone; the page being filled stays current
		if ( bytes > pageSize ) return page;
		current = page;
		return page;
	}

	/// <summary>
	/// The geometry record for vertices laid out as <paramref name="layout"/> at <paramref name="offset"/> in a page: one bit
	/// per semantic the layout has, then per semantic in order its page, element 0's offset, format and stride, and count.
	/// </summary>
	public uint Geometry( int slot, long offset, VertexLayout layout, int count )
	{
		lock ( geometryRecords )
		{
			if ( geometryRecords.TryGetValue( (slot, offset, layout, count), out var found ) ) return found;

			var fields = layout.Fields.Where( f => Semantic( f ) is >= 0 and < 256 && Encode( f.Format ) != 0 ).OrderBy( Semantic ).ToArray();
			var record = new byte[MaskBytes + fields.Length * 16];
			foreach ( var field in fields )
			{
				var semantic = Semantic( field );
				var word = BinaryPrimitives.ReadUInt32LittleEndian( record.AsSpan( semantic / 32 * 4 ) );
				BinaryPrimitives.WriteUInt32LittleEndian( record.AsSpan( semantic / 32 * 4 ), word | 1u << (semantic % 32) );
			}

			for ( int i = 0; i < fields.Length; i++ )
			{
				var attribute = record.AsSpan( MaskBytes + i * 16 );
				BinaryPrimitives.WriteUInt32LittleEndian( attribute, (uint)slot + 1 );
				BinaryPrimitives.WriteUInt32LittleEndian( attribute[4..], (uint)(offset + fields[i].Offset) );
				BinaryPrimitives.WriteUInt32LittleEndian( attribute[8..], Encode( fields[i].Format ) | (uint)layout.Stride << 16 );
				BinaryPrimitives.WriteUInt32LittleEndian( attribute[12..], (uint)count << 1 );
			}

			return geometryRecords[(slot, offset, layout, count)] = geometry.Append( record );
		}
	}

	/// <summary>A field's semantic as shaders number them (<c>usage * 16 + index</c>, the attribute map's numbering).</summary>
	static int Semantic( VertexLayout.Field field ) => field.Index < 16 ? (int)field.Semantic * 16 + field.Index : -1;

	/// <summary>
	/// A vertex format as the decoder reads it: bits 0-5 component width, 6-8 count, 9-11 conversion (0 float, 1 unorm,
	/// 2 snorm, 3 uint, 4 sint), 12 BGRA; 0 for one it can't read.
	/// </summary>
	static uint Encode( VertexFormat format )
	{
		static uint Make( uint bits, uint count, uint conversion ) => bits | count << 6 | conversion << 9;
		return format switch
		{
			VertexFormat.Float => Make( 32, 1, 0 ),
			VertexFormat.Float2 => Make( 32, 2, 0 ),
			VertexFormat.Float3 => Make( 32, 3, 0 ),
			VertexFormat.Float4 => Make( 32, 4, 0 ),
			VertexFormat.Half2 => Make( 16, 2, 0 ),
			VertexFormat.Half4 => Make( 16, 4, 0 ),
			VertexFormat.UByte4Norm => Make( 8, 4, 1 ),
			VertexFormat.Byte4Norm => Make( 8, 4, 2 ),
			VertexFormat.UByte4 => Make( 8, 4, 3 ),
			VertexFormat.UInt => Make( 32, 1, 3 ),
			VertexFormat.UInt2 => Make( 32, 2, 3 ),
			VertexFormat.UInt4 => Make( 32, 4, 3 ),
			VertexFormat.UInt1010102Norm => Make( 10, 4, 1 ),
			VertexFormat.UShort2Norm => Make( 16, 2, 1 ),
			_ => 0,
		};
	}

	//
	// Draws and materials, as they're first drawn
	//

	/// <summary>
	/// The draw record for a fragment: its geometry and material records, flags, its index range and base vertex, and its
	/// index page; made once.
	/// </summary>
	public uint Draw( uint geometryRecord, uint vertexMaterial, uint pixelMaterial, uint flags, uint firstIndex, uint indexCount, int baseVertex, int indexPage )
	{
		var key = (geometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, indexCount, baseVertex, indexPage);
		if ( drawRecords.TryGetValue( key, out var known ) ) return known;
		lock ( drawLock )
		{
			if ( drawRecords.TryGetValue( key, out var found ) ) return found;

			Span<uint> record = [geometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, indexCount, (uint)baseVertex, (uint)indexPage];
			var offset = draws.Append( MemoryMarshal.AsBytes( record ) );
			var index = offset / DrawStride;
			var range = new Borealis.Backend.Meshlets.MeshletStore.Range( indexPage, firstIndex, indexCount, baseVertex );
			Meshlets?.RegisterDraw( index, range );
			drawRecords[key] = index;
			Lods?.RegisterDraw( index, range );
			return index;
		}
	}

	/// <summary>Where draw records find their ranges' generated levels (<see cref="BackendDevice.GeneratedLods"/>), or null.</summary>
	public Lods.LodStore Lods { get; set; }

	/// <summary>A draw record's geometry and material records, flags and base vertex, as <see cref="Draw"/> made it.</summary>
	public (uint Geometry, uint Vertex, uint Pixel, uint Flags, int BaseVertex) DrawFields( uint index )
	{
		Span<uint> record = stackalloc uint[DrawStride / 4];
		draws.Read( index * (uint)DrawStride, MemoryMarshal.AsBytes( record ) );
		return (record[0], record[1], record[2], record[3], (int)record[6]);
	}

	/// <summary>Where draw records find their ranges' meshlets (<see cref="BackendDevice.Meshlets"/>), or null.</summary>
	public Borealis.Backend.Meshlets.MeshletStore Meshlets { get; set; }

	/// <summary>A fragment's draw record, remembered with the arguments it was made for.</summary>
	public record struct OwnerDraw( uint FirstIndex, uint IndexCount, int BaseVertex, uint Draw );

	/// <summary>An owner's draw records for one geometry and pair of material records, by fragment instance.</summary>
	public sealed class OwnerDraws
	{
		public OwnerDraw[] Draws = [];
	}

	readonly ConditionalWeakTable<object, Dictionary<(uint Geometry, uint Vertex, uint Pixel, uint Flags), OwnerDraws>> ownerDraws = new();

	/// <summary>
	/// Where an owner (an aggregate, whose fragments don't change) remembers its fragments' draw records, so a frame finds
	/// them by index rather than by record key. Lock it while reading or writing.
	/// </summary>
	public OwnerDraws DrawsOf( object owner, uint geometry, uint vertexMaterial, uint pixelMaterial, uint flags )
	{
		var byKey = ownerDraws.GetValue( owner, static _ => new() );
		lock ( byKey )
		{
			if ( !byKey.TryGetValue( (geometry, vertexMaterial, pixelMaterial, flags), out var draws ) ) byKey[(geometry, vertexMaterial, pixelMaterial, flags)] = draws = new();
			return draws;
		}
	}

	public const int FragmentStride = 32;

	readonly ConditionalWeakTable<object, System.Collections.Concurrent.ConcurrentDictionary<(uint Geometry, uint Vertex, uint Pixel, uint Flags), uint>> ownerFragments = new();

	/// <summary>
	/// The first of an owner's fragment records for one geometry and pair of material records, made once by
	/// <paramref name="write"/> (which fills <paramref name="count"/> records of <see cref="FragmentStride"/> bytes): the
	/// fragments' bounds, draw records and indices, which culling on the GPU reads every frame. A
	/// <paramref name="releasable"/> owner's runs are a power of two records long, and <see cref="ReleaseFragments"/> gives
	/// them back for another owner's.
	/// </summary>
	public uint FragmentsOf<T>( object owner, uint geometry, uint vertexMaterial, uint pixelMaterial, uint flags, int count, T state, Action<T, Span<byte>> write, bool releasable = false )
	{
		// Found without a lock: the views recording on other workers draw the same owners
		var byKey = ownerFragments.GetValue( owner, static _ => new() );
		if ( byKey.TryGetValue( (geometry, vertexMaterial, pixelMaterial, flags), out var known ) ) return known;

		// Written outside the lock: writing makes draw records, under the draw table's lock
		var sizeClass = releasable ? SizeClass( count ) : -1;
		var records = new byte[(releasable ? 1 << sizeClass : count) * FragmentStride];
		write( state, records.AsSpan( 0, count * FragmentStride ) );
		lock ( byKey )
		{
			if ( byKey.TryGetValue( (geometry, vertexMaterial, pixelMaterial, flags), out var first ) ) return first;
			return byKey[(geometry, vertexMaterial, pixelMaterial, flags)] = releasable ? AppendReleasable( records, sizeClass ) : fragments.Append( records ) / FragmentStride;
		}
	}

	/// <summary>Size classes of releasable fragment runs: <c>1 &lt;&lt; class</c> records each.</summary>
	static int SizeClass( int count ) => count <= 1 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount( (uint)count - 1 );

	/// <summary>Released runs that no frame can read any more, by size class; and each releasable run's class, by its first record.</summary>
	readonly Stack<uint>[] freeFragments = new Stack<uint>[32];
	readonly Dictionary<uint, int> releasableFragments = new();

	/// <summary>Runs released, by the frame they were released in: reused once the frames that could read them are done.</summary>
	readonly List<(long Frame, uint First, int Class)> releasedFragments = new();
	long frameNumber;

	/// <summary>Releasable runs reused and released, for the tests.</summary>
	internal int FragmentRunsReused { get; private set; }
	internal int FragmentRunsReleased { get; private set; }

	uint AppendReleasable( byte[] records, int sizeClass )
	{
		lock ( releasableFragments )
		{
			if ( freeFragments[sizeClass] is { Count: > 0 } free )
			{
				var reused = free.Pop();
				fragments.Write( reused * FragmentStride, records );
				releasableFragments[reused] = sizeClass;
				FragmentRunsReused++;
				return reused;
			}

			var first = fragments.Append( records ) / FragmentStride;
			releasableFragments[first] = sizeClass;
			return first;
		}
	}

	/// <summary>
	/// An owner no list draws any more: its releasable fragment runs and its visibility (<see cref="Visibility"/>) are reused
	/// for others once the frames that could still read them are done; its other runs stay, and asking for its records
	/// again makes new ones.
	/// </summary>
	public void ReleaseFragments( object owner )
	{
		ReleaseVisibility( owner );
		if ( !ownerFragments.TryGetValue( owner, out var byKey ) ) return;
		ownerFragments.Remove( owner );
		lock ( releasableFragments )
		{
			foreach ( var first in byKey.Values )
			{
				if ( !releasableFragments.Remove( first, out var sizeClass ) ) continue;
				releasedFragments.Add( (frameNumber, first, sizeClass) );
				FragmentRunsReleased++;
			}
		}
	}

	/// <summary>No visibility fragment, or no vertex bits for an aggregate.</summary>
	public const uint NoVisibility = uint.MaxValue;

	/// <summary>Visibility fragments there can be: a stamp each.</summary>
	public const int MaxVisibilityFragments = 1 << 20;

	/// <summary>Vertex bits the mask holds, a bit for every vertex of every aggregate drawn.</summary>
	public const long MaxVisibilityBits = 1L << 26;

	/// <summary>
	/// An owner's visibility: its stamps and its vertex bits (in 32-bit words). One that found no room has
	/// <see cref="NoVisibility"/> and the free generation it looked in; <see cref="Final"/> when it's made or never can be.
	/// </summary>
	sealed class VisibilityRun
	{
		public uint First = NoVisibility, VertexBase = NoVisibility;
		public int Fragments;
		public long Words;
		public bool Final;
		public int Generation;
	}

	readonly ConditionalWeakTable<object, VisibilityRun> visibilityOwners = new();

	/// <summary>Stamps below <see cref="FrameVisibilityFragmentBase"/> and words of vertex bits below <see cref="FrameVisibilityBitBase"/>, which owners' runs take.</summary>
	readonly RangeAllocator visibilityStamps = new( FrameVisibilityFragmentBase ), visibilityWords = new( FrameVisibilityBitBase / 32 );

	/// <summary>Made runs whose owners haven't released them; runs released, by the frame they were released in.</summary>
	readonly HashSet<VisibilityRun> visibilityRuns = new( ReferenceEqualityComparer.Instance );
	readonly List<(long Frame, VisibilityRun Run)> releasedVisibility = new();
	long visibilityFrame;

	/// <summary>Moves when released runs are free again: an owner that found no room looks again in the next.</summary>
	int visibilityGeneration;
	int visibilitySwept = -1;

	/// <summary>Visibility runs released, for the tests.</summary>
	internal int VisibilityRunsReleased { get; private set; }

	/// <summary>
	/// An owner's visibility: the index of its first fragment's stamp (so the prepass's and forward's different buckets name
	/// the same fragments) and the first of its vertices' bits in the mask, which the ids name. Made once and kept until
	/// <see cref="ReleaseFragments"/> or the owner is collected; <see cref="NoVisibility"/> for both when there's no room,
	/// looked for again once released runs are free.
	/// </summary>
	public (uint First, uint VertexBase) Visibility( object owner, int fragments, int vertices )
	{
		if ( visibilityOwners.TryGetValue( owner, out var known ) && (known.Final || known.Generation == Volatile.Read( ref visibilityGeneration )) ) return (known.First, known.VertexBase);

		lock ( visibilityOwners )
		{
			if ( visibilityOwners.TryGetValue( owner, out known ) && (known.Final || known.Generation == visibilityGeneration) ) return (known.First, known.VertexBase);

			var run = new VisibilityRun { Fragments = fragments, Words = (vertices + 31L) / 32, Generation = visibilityGeneration };
			if ( fragments <= 0 || vertices <= 0 || fragments > visibilityStamps.Capacity || run.Words > visibilityWords.Capacity ) run.Final = true;
			else if ( !TryPlace( run ) )
			{
				// Owners collected without a release hold runs no frame can draw any more: they're free in a few frames
				SweepVisibility();
			}

			visibilityOwners.AddOrUpdate( owner, run );
			return (run.First, run.VertexBase);
		}
	}

	bool TryPlace( VisibilityRun run )
	{
		if ( !visibilityStamps.TryAllocate( run.Fragments, out var first ) ) return false;
		if ( !visibilityWords.TryAllocate( run.Words, out var word ) )
		{
			visibilityStamps.Free( first, run.Fragments );
			return false;
		}

		run.First = (uint)first;
		run.VertexBase = (uint)(word * 32);
		run.Final = true;
		visibilityRuns.Add( run );
		return true;
	}

	/// <summary>Release the runs of owners that were collected without a release, once a generation: only when room runs out.</summary>
	void SweepVisibility()
	{
		if ( visibilitySwept == visibilityGeneration ) return;
		visibilitySwept = visibilityGeneration;

		var live = new HashSet<VisibilityRun>( ReferenceEqualityComparer.Instance );
		foreach ( var (_, run) in visibilityOwners ) live.Add( run );
		foreach ( var run in visibilityRuns )
		{
			if ( !live.Contains( run ) ) releasedVisibility.Add( (visibilityFrame, run) );
		}

		visibilityRuns.RemoveWhere( run => !live.Contains( run ) );
	}

	/// <summary>An owner's visibility run, free for another's once the frames that could still read it are done.</summary>
	void ReleaseVisibility( object owner )
	{
		if ( !visibilityOwners.TryGetValue( owner, out _ ) ) return;
		lock ( visibilityOwners )
		{
			if ( !visibilityOwners.TryGetValue( owner, out var run ) ) return;
			visibilityOwners.Remove( owner );
			if ( !visibilityRuns.Remove( run ) ) return;
			releasedVisibility.Add( (visibilityFrame, run) );
			VisibilityRunsReleased++;
		}
	}

	/// <summary>One past the highest vertex bit an owner holds: what a view's mask clears of them each frame.</summary>
	public long VisibilityBits
	{
		get
		{
			lock ( visibilityOwners ) return visibilityWords.Top * 32;
		}
	}

	/// <summary>The vertex bits and stamps owners hold, the vertex bits' gaps, and runs placed in a gap, for reports and the tests.</summary>
	internal (long Bits, long Stamps, int Gaps, int Reused) VisibilityUse
	{
		get
		{
			lock ( visibilityOwners ) return (visibilityWords.Used * 32, visibilityStamps.Used, visibilityWords.Gaps, visibilityWords.Reused);
		}
	}

	/// <summary>Where this frame's visibility starts: aggregates' runs, which last, are below it, mesh draws' above.</summary>
	public const int FrameVisibilityFragmentBase = MaxVisibilityFragments / 2;
	public const long FrameVisibilityBitBase = MaxVisibilityBits / 2;

	readonly Dictionary<(int FirstInstance, object Mesh, int ModelMesh, int DrawCall), (uint First, uint VertexBase)> frameVisibility = new();
	int frameVisibilityFragments;
	long frameVisibilityBits;

	/// <summary>
	/// This frame's visibility for a mesh draw's <paramref name="instances"/> from an instance (the same key in its view's
	/// prepass and forward pass): the first of their stamps, and the first of their vertex bits, each instance's
	/// <paramref name="vertices"/> after the last's. Made again each frame (<see cref="BeginFrame"/>);
	/// <see cref="NoVisibility"/> when there's no room.
	/// </summary>
	public (uint First, uint VertexBase) FrameVisibility( (int FirstInstance, object Mesh, int ModelMesh, int DrawCall) key, int instances, int vertices )
	{
		lock ( frameVisibility )
		{
			if ( frameVisibility.TryGetValue( key, out var known ) ) return known;

			var made = (NoVisibility, NoVisibility);
			var bits = (long)instances * vertices;
			if ( vertices > 0 && frameVisibilityFragments + instances <= MaxVisibilityFragments - FrameVisibilityFragmentBase && frameVisibilityBits + bits <= MaxVisibilityBits - FrameVisibilityBitBase )
			{
				made = ((uint)(FrameVisibilityFragmentBase + frameVisibilityFragments), (uint)(FrameVisibilityBitBase + frameVisibilityBits));
				frameVisibilityFragments += instances;
				frameVisibilityBits += (bits + 31) & ~31L;
			}

			frameVisibility[key] = made;
			return made;
		}
	}

	/// <summary>The vertex bits the last frame's mesh draws used: what a view's mask clears of them before this frame's.</summary>
	public long LastFrameVisibilityBits { get; private set; }

	/// <summary>The vertex bits this frame's mesh draws use, from <see cref="FrameVisibilityBitBase"/>.</summary>
	public long FrameVisibilityBits
	{
		get
		{
			lock ( frameVisibility ) return frameVisibilityBits;
		}
	}
	/// <summary>The fragment table's slot in the raw buffer array.</summary>
	public int FragmentSlot => fragments.Slot;

	/// <summary>A stage's material record when it's made.</summary>
	public bool TryMaterial( in MaterialRecordKey key, out uint record ) => materialRecords.TryGetValue( key, out record );

	/// <summary>A stage's material record, made once by <paramref name="write"/> (which fills the bytes it's given).</summary>
	public uint Material<T>( in MaterialRecordKey key, int size, T state, Action<T, Span<byte>> write )
	{
		if ( materialRecords.TryGetValue( key, out var known ) ) return known;

		// Written outside the lock: writing reads the material's and attributes' values, which take their own locks
		var record = new byte[Math.Max( size, 16 )];
		write( state, record );
		lock ( materialRecords )
		{
			if ( materialRecords.TryGetValue( key, out var found ) ) return found;
			return materialRecords[key] = materials.Append( record );
		}
	}

	/// <summary>Records in each table, for reports.</summary>
	public (int Geometry, int Draws, int Materials) Records
	{
		get
		{
			lock ( geometryRecords ) return (geometryRecords.Count, drawRecords.Count, materialRecords.Count);
		}
	}

	//
	// Per frame: instance records and commands
	//

	sealed class Chunk
	{
		public BufferHandle Buffer;
		public int Slot;
		public long Size;
		public long Used;
	}

	const int FrameSlots = 3;
	readonly List<Chunk>[] frames = [new(), new(), new()];
	int frame;

	/// <summary>The chunk this frame's calls take room from without a lock, until it's full.</summary>
	Chunk current;

	/// <summary>Start a frame: reuse the chunks of the frame <see cref="FrameSlots"/> back, which the device has finished.</summary>
	public void BeginFrame()
	{
		// Released fragment runs the frames still in flight can't read any more
		lock ( releasableFragments )
		{
			frameNumber++;
			var kept = 0;
			for ( int i = 0; i < releasedFragments.Count; i++ )
			{
				var (released, first, sizeClass) = releasedFragments[i];
				if ( frameNumber - released > FrameSlots ) (freeFragments[sizeClass] ??= new()).Push( first );
				else releasedFragments[kept++] = (released, first, sizeClass);
			}

			releasedFragments.RemoveRange( kept, releasedFragments.Count - kept );
		}

		// Released visibility the same way: free once no frame in flight reads it, and owners without room look again
		lock ( visibilityOwners )
		{
			visibilityFrame++;
			if ( releasedVisibility.Count > 0 )
			{
				var kept = 0;
				for ( int i = 0; i < releasedVisibility.Count; i++ )
				{
					var (released, run) = releasedVisibility[i];
					if ( visibilityFrame - released > FrameSlots )
					{
						visibilityStamps.Free( run.First, run.Fragments );
						visibilityWords.Free( run.VertexBase / 32, run.Words );
					}
					else releasedVisibility[kept++] = (released, run);
				}

				if ( kept < releasedVisibility.Count ) visibilityGeneration++;
				releasedVisibility.RemoveRange( kept, releasedVisibility.Count - kept );
			}
		}

		lock ( frames )
		{
			frame = (frame + 1) % FrameSlots;
			foreach ( var chunk in frames[frame] ) chunk.Used = 0;
			Volatile.Write( ref current, frames[frame].Count > 0 ? frames[frame][0] : null );
		}

		lock ( frameVisibility )
		{
			frameVisibility.Clear();
			frameVisibilityFragments = 0;
			LastFrameVisibilityBits = frameVisibilityBits;
			frameVisibilityBits = 0;
		}
	}

	/// <summary>
	/// Room this frame for <paramref name="bytes"/> of instance records and commands, in one host-visible buffer readable
	/// as storage and indirect arguments; write it through <see cref="Device.Map"/>. Every recording thread asks, so room
	/// comes from the current chunk by an atomic add, and only a full chunk takes the lock to move on.
	/// </summary>
	public (BufferHandle Buffer, int Slot, long Offset) Allocate( int bytes )
	{
		var aligned = (bytes + 15L) & ~15L;
		long offset;
		if ( Volatile.Read( ref current ) is { } chunk && Take( chunk, aligned, out offset ) ) return (chunk.Buffer, chunk.Slot, offset);

		lock ( frames )
		{
			// Another chunk of the frame's with room, else a new one; it becomes the current one
			Chunk found = null;
			offset = 0;
			foreach ( var c in frames[frame] )
			{
				if ( Take( c, aligned, out offset ) )
				{
					found = c;
					break;
				}
			}

			if ( found is null )
			{
				var size = Math.Max( 4L << 20, aligned );
				var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage | BufferUsage.Indirect, Memory = MemoryLocation.Upload, Name = "Borealis GPU-driven frame" } );
				lock ( pagesLock ) owned.Add( buffer );
				found = new Chunk { Buffer = buffer, Slot = gpu.BindlessIndex( buffer ), Size = size };
				frames[frame].Add( found );
				Take( found, aligned, out offset );
			}

			Volatile.Write( ref current, found );
			return (found.Buffer, found.Slot, offset);
		}
	}

	/// <summary>Take room in a chunk by an atomic add; false when it's full (what was added past its end is never used).</summary>
	static bool Take( Chunk chunk, long bytes, out long offset )
	{
		offset = 0;
		if ( Volatile.Read( ref chunk.Used ) + bytes > chunk.Size ) return false;

		var end = Interlocked.Add( ref chunk.Used, bytes );
		offset = end - bytes;
		return end <= chunk.Size;
	}

	public void Dispose()
	{
		geometry.Dispose();
		draws.Dispose();
		materials.Dispose();
		fragments.Dispose();
		lighting.Dispose();
		lock ( pagesLock )
		{
			foreach ( var buffer in owned ) gpu.Destroy( buffer );
			owned.Clear();
		}
	}
}
