using Basalt.Gpu;
using Basalt.Source2.Shaders;
using System.Buffers.Binary;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Basalt.Backend;

/// <summary>
/// Where a draw's geometry is for GPU-driven drawing: its geometry record (byte offset in the geometry table), the index
/// page its indices are in, the page's slot in the raw buffer array, and its first index there; and the geometry record of
/// its positions-only copy (0 when there's none), which depth-only draws read; and the vertices its records hold.
/// </summary>
internal readonly record struct PooledGeometry( uint GeometryRecord, BufferHandle IndexPage, int IndexPageSlot, uint FirstIndex, IndexFormat IndexFormat, uint PositionsRecord = 0, int VertexCount = 0 );

/// <summary>
/// The buffers GPU-driven drawing reads (<c>basalt_gpu_driven.fxc</c>, docs/managed/basalt-gpu-driven.md): meshes'
/// vertices and indices in shared pages, and the geometry, draw and material tables, all reached through the bindless raw
/// buffer array (<c>g_bindless_VertexBuffers</c>, binding 63). Tables only grow: a record, once written, is read by every
/// later frame, so nothing is uploaded per frame but each frame's instance records and indirect commands.
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
	}

	Page vertexPage;
	readonly Dictionary<IndexFormat, Page> indexPages = new();
	long pageBytes;

	/// <summary>
	/// A table in host-visible memory that only grows: records are appended and never change. Growing copies it into a
	/// bigger buffer with a slot of its own; frames still in flight keep reading the old one until it's freed.
	/// </summary>
	sealed class Table( Device gpu, string name, int initial )
	{
		public BufferHandle Buffer;
		public int Slot = -1;
		public long Used;
		long size;

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

		void Grow( long needed )
		{
			var next = Math.Max( initial, size * 2 );
			while ( next < needed ) next *= 2;

			var buffer = gpu.CreateBuffer( new BufferInfo { Size = next, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = name } );
			if ( Buffer.IsValid )
			{
				gpu.Map( Buffer )[..(int)Used].CopyTo( gpu.Map( buffer ) );
				gpu.Destroy( Buffer );
			}

			Buffer = buffer;
			size = next;
			Slot = gpu.BindlessIndex( buffer );
		}

		public void Dispose()
		{
			if ( Buffer.IsValid ) gpu.Destroy( Buffer );
		}
	}

	readonly Table geometry, draws, materials, fragments, lighting;
	readonly Dictionary<(int Page, long Offset, VertexLayout Layout, int Count), uint> geometryRecords = new();
	readonly Dictionary<(uint Geometry, uint Vertex, uint Pixel, uint Flags, uint FirstIndex, uint IndexCount, int BaseVertex, int IndexPage), uint> drawRecords = new();
	readonly Dictionary<MaterialRecordKey, uint> materialRecords = new();

	/// <summary>A material record: per material and its version, stage, and the attribute values its constants read.</summary>
	internal readonly record struct MaterialRecordKey( BackendMaterial Material, int Version, ShaderStage Stage, bool UncompressedTangentFrame, int Attributes );

	public GpuDrivenTables( Device gpu )
	{
		this.gpu = gpu;
		geometry = new Table( gpu, "Basalt GPU-driven geometry", 1 << 20 );
		draws = new Table( gpu, "Basalt GPU-driven draws", 1 << 20 );
		materials = new Table( gpu, "Basalt GPU-driven materials", 1 << 20 );
		fragments = new Table( gpu, "Basalt GPU-driven fragments", 1 << 20 );
		lighting = new Table( gpu, "Basalt GPU-driven lighting", 1 << 16 );

		// Made now, so their slots exist before any frame reads them; lighting record 0 is no lighting
		geometry.Append( new byte[16] );
		draws.Append( new byte[DrawStride] );
		materials.Append( new byte[16] );
		fragments.Append( new byte[FragmentStride] );
		lighting.Append( new byte[LightingStride] );
	}

	/// <summary>The tables' slots in the raw buffer array, as <c>BasaltGpuDriven_t</c> wants them after the instances'.</summary>
	public (int Geometry, int Draws, int Materials, int Lighting) Slots => (geometry.Slot, draws.Slot, materials.Slot, lighting.Slot);

	/// <summary>
	/// A lighting record: <c>BakedLightingConstantBuffer_t</c> (48 bytes), then <c>PerBatchLightProbeVolumeConstantBuffer_t</c>
	/// (112), as the attributes a draw reads hold them, which GPU-driven variants read per draw instead of binding.
	/// </summary>
	public const int LightingStride = 160;

	readonly Dictionary<byte[], uint> lightingRecords = new( LightingComparer.Instance );

	sealed class LightingComparer : IEqualityComparer<byte[]>
	{
		public static readonly LightingComparer Instance = new();
		public bool Equals( byte[] a, byte[] b ) => a.AsSpan().SequenceEqual( b );
		public int GetHashCode( byte[] bytes )
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
		var record = new byte[LightingStride];
		baked?.AsSpan( 0, Math.Min( baked.Length, 48 ) ).CopyTo( record );
		probe?.AsSpan( 0, Math.Min( probe.Length, 112 ) ).CopyTo( record.AsSpan( 48 ) );
		lock ( lightingRecords )
		{
			if ( !lightingRecords.TryGetValue( record, out var index ) ) lightingRecords[record] = index = lighting.Append( record ) / 16;
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

	//
	// Geometry, at load
	//

	/// <summary>Put a mesh's vertex buffer in a page: its page's slot and its byte offset there.</summary>
	public (int Slot, long Offset) AddVertices( ReadOnlySpan<byte> data )
	{
		lock ( pagesLock )
		{
			var page = Place( ref vertexPage, data.Length, VertexPageSize, BufferUsage.Storage | BufferUsage.Vertex, "Basalt vertex page" );
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
			var page = Place( ref vertexPage, data.Length, VertexPageSize, BufferUsage.Storage | BufferUsage.Vertex, "Basalt vertex page" );
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
			page = Place( ref page, data.Length, IndexPageSize, BufferUsage.Index | BufferUsage.Storage, $"Basalt index page {format}" );
			indexPages[format] = page;
			var offset = page.Used;
			if ( data.Length > 0 ) gpu.WriteBuffer( page.Buffer, data, offset );
			page.Used = (offset + data.Length + 15) & ~15;
			return (page.Buffer, page.Slot, (uint)(offset / (format == IndexFormat.UInt32 ? 4 : 2)));
		}
	}

	/// <summary>Room for <paramref name="bytes"/>: the current page, a new one, or a buffer of its own when it's bigger than a page.</summary>
	Page Place( ref Page current, int bytes, int pageSize, BufferUsage usage, string name )
	{
		if ( current is not null && current.Used + bytes <= current.Size ) return current;

		var size = Math.Max( pageSize, (bytes + 15) & ~15 );
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = usage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = name } );
		owned.Add( buffer );
		pageBytes += size;
		var page = new Page { Buffer = buffer, Slot = gpu.BindlessIndex( buffer ), Size = size };

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
		lock ( drawRecords )
		{
			if ( drawRecords.TryGetValue( key, out var found ) ) return found;

			Span<uint> record = [geometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, indexCount, (uint)baseVertex, (uint)indexPage];
			var offset = draws.Append( MemoryMarshal.AsBytes( record ) );
			return drawRecords[key] = offset / DrawStride;
		}
	}

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

	readonly ConditionalWeakTable<object, Dictionary<(uint Geometry, uint Vertex, uint Pixel, uint Flags), uint>> ownerFragments = new();

	/// <summary>
	/// The first of an owner's fragment records for one geometry and pair of material records, made once by
	/// <paramref name="write"/> (which fills <paramref name="count"/> records of <see cref="FragmentStride"/> bytes): the
	/// fragments' bounds, draw records and indices, which culling on the GPU reads every frame.
	/// </summary>
	public uint FragmentsOf<T>( object owner, uint geometry, uint vertexMaterial, uint pixelMaterial, uint flags, int count, T state, Action<T, Span<byte>> write )
	{
		var byKey = ownerFragments.GetValue( owner, static _ => new() );
		lock ( byKey )
		{
			if ( byKey.TryGetValue( (geometry, vertexMaterial, pixelMaterial, flags), out var first ) ) return first;

			var records = new byte[count * FragmentStride];
			write( state, records );
			return byKey[(geometry, vertexMaterial, pixelMaterial, flags)] = fragments.Append( records ) / FragmentStride;
		}
	}

	/// <summary>No visibility fragment, or no vertex bits for an aggregate.</summary>
	public const uint NoVisibility = uint.MaxValue;

	/// <summary>Visibility fragments there can be: a stamp each.</summary>
	public const int MaxVisibilityFragments = 1 << 20;

	/// <summary>Vertex bits the mask holds, a bit for every vertex of every aggregate drawn.</summary>
	public const long MaxVisibilityBits = 1L << 26;

	readonly ConditionalWeakTable<object, StrongBox<(uint First, uint VertexBase)>> visibilityOwners = new();
	long visibilityBits;
	int visibilityFragments;

	/// <summary>
	/// An aggregate's visibility: the index of its first fragment's stamp (so the prepass's and forward's different
	/// buckets name the same fragments) and the first of its vertices' bits in the mask, which the ids name. Made once;
	/// <see cref="NoVisibility"/> when there's no room.
	/// </summary>
	public (uint First, uint VertexBase) Visibility( object owner, int fragments, int vertices )
	{
		if ( visibilityOwners.TryGetValue( owner, out var known ) ) return known.Value;

		lock ( visibilityOwners )
		{
			if ( visibilityOwners.TryGetValue( owner, out known ) ) return known.Value;

			var first = NoVisibility;
			if ( visibilityFragments + fragments <= FrameVisibilityFragmentBase )
			{
				first = (uint)visibilityFragments;
				visibilityFragments += fragments;
			}

			var vertexBase = NoVisibility;
			var bits = (vertices + 31L) & ~31L;
			if ( vertices > 0 && visibilityBits + bits <= FrameVisibilityBitBase )
			{
				vertexBase = (uint)visibilityBits;
				visibilityBits += bits;
			}

			var made = (first, vertexBase);
			visibilityOwners.Add( owner, new StrongBox<(uint, uint)>( made ) );
			return made;
		}
	}

	/// <summary>The vertex bits in use.</summary>
	public long VisibilityBits
	{
		get
		{
			lock ( visibilityOwners ) return visibilityBits;
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

	/// <summary>A stage's material record, made once by <paramref name="write"/> (which fills the bytes it's given).</summary>
	public uint Material<T>( in MaterialRecordKey key, int size, T state, Action<T, Span<byte>> write )
	{
		lock ( materialRecords )
		{
			if ( materialRecords.TryGetValue( key, out var found ) ) return found;

			var record = new byte[Math.Max( size, 16 )];
			write( state, record );
			return materialRecords[key] = materials.Append( record );
		}
	}

	/// <summary>Records in each table, for reports.</summary>
	public (int Geometry, int Draws, int Materials) Records
	{
		get
		{
			lock ( geometryRecords ) lock ( drawRecords ) lock ( materialRecords ) return (geometryRecords.Count, drawRecords.Count, materialRecords.Count);
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

	/// <summary>Start a frame: reuse the chunks of the frame <see cref="FrameSlots"/> back, which the device has finished.</summary>
	public void BeginFrame()
	{
		lock ( frames )
		{
			frame = (frame + 1) % FrameSlots;
			foreach ( var chunk in frames[frame] ) chunk.Used = 0;
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
	/// as storage and indirect arguments; write it through <see cref="Device.Map"/>.
	/// </summary>
	public (BufferHandle Buffer, int Slot, long Offset) Allocate( int bytes )
	{
		lock ( frames )
		{
			var list = frames[frame];
			Chunk chunk = null;
			foreach ( var c in list )
			{
				if ( c.Used + bytes <= c.Size )
				{
					chunk = c;
					break;
				}
			}

			if ( chunk is null )
			{
				var size = Math.Max( 4 << 20, (bytes + 15) & ~15 );
				var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage | BufferUsage.Indirect, Memory = MemoryLocation.Upload, Name = "Basalt GPU-driven frame" } );
				lock ( pagesLock ) owned.Add( buffer );
				chunk = new Chunk { Buffer = buffer, Slot = gpu.BindlessIndex( buffer ), Size = size };
				list.Add( chunk );
			}

			var offset = chunk.Used;
			chunk.Used = (offset + bytes + 15) & ~15;
			return (chunk.Buffer, chunk.Slot, offset);
		}
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
