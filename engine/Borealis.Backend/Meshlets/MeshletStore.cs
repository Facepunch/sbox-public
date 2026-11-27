using Borealis.Gpu;
using Borealis.Source2.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Meshlets;

/// <summary>
/// The meshlets of GPU-driven index ranges (docs/reference/borealis-gpu-driven.md, "Meshlets"). A range is asked for as its
/// draw records are written; it's built off the frame on workers (<see cref="MeshletBuilder"/>), its indices rewritten in
/// its index page in meshlet order and its records put in a meshlet page at a frame's start, and only once the frames that
/// could still be reading its old order are done does every draw record over it get its run: until then it draws whole.
/// A run (<see cref="RunBytes"/> per draw record, in a host-visible table the culls read) is its meshlet page's bindless
/// slot, its first meshlet in the page, how many (0 for none) and the first's index among every meshlet (for per-view
/// meshlet bits).
/// </summary>
internal sealed class MeshletStore : IDisposable
{
	/// <summary>An index range in a GPU-driven index page, as draw records name it.</summary>
	public readonly record struct Range( int PageSlot, uint FirstIndex, uint IndexCount, int BaseVertex );

	/// <summary>Where a range's indices and vertices are on the CPU, to build from.</summary>
	public sealed record Source( BufferHandle IndexPage, IndexFormat Format, MeshIndexBuffer Indices, int IndexBase, MeshVertexBuffer Vertices, Borealis.Source2.Models.VertexField Position, int VertexBase );

	/// <summary>Records each meshlet page holds.</summary>
	public const int PageRecords = 1 << 17;

	/// <summary>Meshlets a run can name: a group of the meshlet pass keeps a bit per meshlet.</summary>
	public const int MaxRunMeshlets = 4096;

	/// <summary>Bytes of a run in the run table: page slot, first, count, first's index among every meshlet.</summary>
	public const int RunBytes = MeshletRun.Size;

	/// <summary>
	/// Bytes of a meshlet's local record, after a page's <see cref="PageRecords"/> records when <see cref="LocalData"/> is on:
	/// its data page's bindless slot, where its triangles and its vertices start there (bytes), and its vertex count.
	/// </summary>
	public const int LocalRecordBytes = MeshletLocalRecord.Size;

	/// <summary>Bytes of a data page, which holds ranges' local triangles and vertices and their corner tables (a range's in one page).</summary>
	public const int DataPageBytes = 32 << 20;

	/// <summary>
	/// Bytes of a corner run in the corner table, per draw record: the data page's bindless slot plus 1 (0 for none: no
	/// <see cref="CornerData"/>), the byte offset of the range's first-corner table there (<see cref="MeshletSet.Corners"/>),
	/// its lowest first corner and how many values from it (0 until its run is written). Compaction reads the last two
	/// whatever <see cref="CornerData"/> is: the vertex bits a mark can set for the draw.
	/// </summary>
	public const int CornerRunBytes = 16;

	/// <summary>Frames a rewrite waits before its runs are written: frames in flight may read the range's old order.</summary>
	const int PublishDelay = 4;

	readonly Device gpu;

	public MeshletConfig Config { get; }

	/// <summary>
	/// Whether each meshlet also gets its own vertex list and triangles in local numbers (<see cref="MeshletSet.Local"/>), for
	/// the mesh shaders: a local record per meshlet after a page's records and the lists in data pages. Set before the first
	/// request.
	/// </summary>
	public bool LocalData { get; init; }

	/// <summary>
	/// Whether each range also gets its triangles by first corner (<see cref="MeshletSet.Corners"/>), for normals from the
	/// visibility ids (<see cref="BackendDevice.IdsNormals"/>): uploaded into data pages, and a corner run per draw record
	/// beside its run. Set before the first request.
	/// </summary>
	public bool CornerData { get; init; }

	/// <summary>
	/// Whether a built range's bytes are written on its build worker into a staging buffer of its own (on), so the upload on
	/// the frame's thread only records copies; off writes them there (narrowing 16-bit indices too). For comparisons.
	/// </summary>
	public static bool StageOnWorkers { get; set; } = true;

	/// <summary>
	/// Whether ranges the resource compiler cooked (<see cref="Borealis.Source2.Models.CookedMeshlets"/>) take its meshlets
	/// rather than being built (on; <c>-no-cooked-meshlets</c> builds every range, for comparisons).
	/// </summary>
	public static bool UseCooked { get; set; } = true;

	/// <summary>Bytes a frame uploads at most (indices and records); the rest waits for the next frame.</summary>
	public long FrameUploadBytes { get; set; } = 16 << 20;

	/// <summary>Whether a loading screen hides the frames (<see cref="BackendDevice.Loading"/>): a frame uploads <see cref="LoadingUploadBytes"/>.</summary>
	public bool Loading { get; set; }

	/// <summary>Bytes a frame behind a loading screen uploads at most.</summary>
	public long LoadingUploadBytes { get; set; } = 128 << 20;

	long UploadBudget => Loading ? Math.Max( LoadingUploadBytes, FrameUploadBytes ) : FrameUploadBytes;

	enum State { Building, Built, Uploaded, Published, None }

	sealed class Entry
	{
		public Range Range;
		public Source Source;
		public State State;
		public MeshletSet Set;
		public MeshletLocal Local;
		public MeshletCorners Corners;
		public long CornersAt;
		public uint CornerSlot, CornerAt, CornerLow, CornerSpan;

		/// <summary>
		/// What its upload copies, written on the build worker (<see cref="Stage"/>) in the range's index size: its indices, its
		/// records, then its local triangles and vertices; null when the upload writes them itself.
		/// </summary>
		public BufferHandle Staging;
		public long IndexBytes, LocalTrianglesAt, LocalVerticesAt, LocalVertexBytes;
		public long UploadedFrame;
		public uint PageSlot, First, Count, Global;
		public readonly List<uint> Draws = new();
	}

	readonly object gate = new();
	readonly Dictionary<Range, Entry> entries = new();

	// Draw records over ranges not asked for yet: a range's prepass and forward records can come before its request
	readonly Dictionary<Range, List<uint>> waitingDraws = new();

	// Requested ranges by page and first index, to refuse a range that overlaps another without being it
	readonly Dictionary<int, SortedList<uint, uint>> pageRanges = new();

	readonly ConcurrentQueue<Entry> built = new();
	readonly List<Entry> uploaded = new();

	// Where ranges build: at most the store's threads at once, the rest queued here rather than each holding a thread pool
	// thread while it waits (a map's thousands of ranges starved the pool, so the load's own work on it waited seconds)
	readonly TaskScheduler workers;
	readonly Action<object> build;

	// Builds in flight, so Dispose waits for them before destroying what they write to: a build still making its staging
	// buffer as the device went crashed the GPU tests' host (2026-11-25). Monitor.Wait gives the lock up while it waits
	readonly object flight = new();
	int building;
	volatile bool disposed;
	readonly List<BufferHandle> pages = new();

	// Records of ranges forgotten with their geometry page (page index, first, count), sorted and never adjacent: taken before
	// the last page fills, so maps loaded one after another reuse them rather than adding pages
	readonly List<(int Page, int First, int Count)> freeRecords = new();

	// Records in use in each page (by index, beside pages): a page none are in goes (ReleasePage), and its index is the next
	// new page's. A map's pages stayed after it had gone (12 MiB of video memory after Construct)
	readonly List<int> pageLive = new();

	/// <summary>Meshlet pages let go once nothing used their records, for soaks and tests.</summary>
	public long PagesReleased { get; private set; }

	/// <summary>Meshlet pages there are, for tests.</summary>
	public int PageCount
	{
		get
		{
			lock ( gate ) return pages.Count( p => p.IsValid );
		}
	}

	/// <summary>Meshlet records reused from forgotten ranges, for soaks and tests.</summary>
	public long ReusedRecords { get; private set; }
	BufferHandle page;
	int pageIndex = -1, pageSlot = -1, pageUsed = PageRecords;
	readonly List<BufferHandle> dataPages = new();
	BufferHandle dataPage;
	int dataSlot = -1;
	long dataUsed = DataPageBytes;
	long frame;

	// The run table: 8 bytes per draw record
	BufferHandle runs;
	int runSlot = -1;
	long runBytes;

	// The corner table: a corner run per draw record, grown as the run table is
	BufferHandle corners;
	int cornerSlot = -1;
	long cornerBytes;
	// Run tables it grew out of, with the device frame they were retired in: draws recorded in that frame read them
	readonly List<(BufferHandle Buffer, ulong Frame)> retiredRuns = new();

	/// <summary>Ranges asked for, built, published and refused; meshlets and triangles published; build time on the workers.</summary>
	public long Requested, Built, Published, Refused, Meshlets, Triangles, Triangles16, BuildTicks;

	/// <summary>Bytes of first-corner tables uploaded (<see cref="CornerData"/>).</summary>
	public long CornerTableBytes;

	/// <summary>Ranges whose meshlets were cooked into their model, taken as they are, and the worker time that took.</summary>
	public long CookedHits, CookedTicks;

	/// <summary>Ranges of index buffers with cooked meshlets that were built all the same: another config, a range not cooked, or records that don't cover it.</summary>
	public long CookedMisses;

	public MeshletStore( Device gpu, MeshletConfig config, int threads, string cacheFolder = null )
	{
		CacheFolder = cacheFolder;
		this.gpu = gpu;
		Config = config;
		workers = new ConcurrentExclusiveSchedulerPair( TaskScheduler.Default, Math.Max( threads, 1 ) ).ConcurrentScheduler;
		build = entry =>
		{
			try
			{
				if ( !disposed ) BuildOn( (Entry)entry );
			}
			finally
			{
				lock ( flight )
				{
					if ( --building == 0 ) Monitor.PulseAll( flight );
				}
			}
		};
		GrowRuns( 1 << 16 );
	}

	/// <summary>The run table's bindless slot, for the culls.</summary>
	public int RunSlot
	{
		get
		{
			lock ( gate ) return runSlot;
		}
	}

	/// <summary>Bytes of the run table, for tests.</summary>
	internal long RunBytesHeld
	{
		get
		{
			lock ( gate ) return runBytes;
		}
	}

	/// <summary>
	/// The run table's bindless slot, grown first to hold a run for every one of <paramref name="drawRecords"/> draw records:
	/// the culls read a draw's run whether or not it has one, so a draw record past the table's end read past its buffer.
	/// What's grown is zeros, which read as no meshlets.
	/// </summary>
	public int RunSlotCovering( long drawRecords )
	{
		lock ( gate )
		{
			if ( drawRecords * RunBytes > runBytes ) GrowRuns( drawRecords * RunBytes );
			return runSlot;
		}
	}

	/// <summary>
	/// The corner table's bindless slot, grown first to hold a corner run for every one of <paramref name="drawRecords"/>
	/// draw records, as <see cref="RunSlotCovering"/> grows the run table.
	/// </summary>
	public int CornerSlotCovering( long drawRecords )
	{
		lock ( gate )
		{
			if ( drawRecords * CornerRunBytes > cornerBytes || !corners.IsValid ) GrowCorners( Math.Max( drawRecords * CornerRunBytes, 1 << 16 ) );
			return cornerSlot;
		}
	}

	/// <summary>Ranges still being built or waiting to be published, for reports and waiting on them.</summary>
	public int Pending
	{
		get
		{
			lock ( gate ) return entries.Values.Count( e => e.State is State.Building or State.Built or State.Uploaded );
		}
	}

	/// <summary>
	/// Ask for a range's meshlets: built once, on a worker. A range that overlaps another asked for, without being it, is
	/// refused (rewriting one would reorder the other's triangles) and draws whole.
	/// </summary>
	public void Request( in Range range, Source source )
	{
		if ( range.IndexCount < 3 ) return;
		lock ( gate )
		{
			if ( entries.ContainsKey( range ) ) return;
			var entry = new Entry { Range = range, Source = source, State = State.Building };
			entries[range] = entry;
			if ( waitingDraws.Remove( range, out var draws ) ) entry.Draws.AddRange( draws );
			if ( !Claim( range ) )
			{
				entry.State = State.None;
				Refused++;
				return;
			}

			Requested++;
			lock ( flight ) building++;
			Task.Factory.StartNew( build, entry, CancellationToken.None, TaskCreationOptions.DenyChildAttach, workers );
		}
	}

	/// <summary>
	/// On the build worker: the bytes a built range's upload copies (<see cref="Entry.Staging"/>), its indices and local
	/// vertices narrowed to the range's index size.
	/// </summary>
	void Stage( Entry entry )
	{
		var set = entry.Set;
		var wide = entry.Source.Format == IndexFormat.UInt32;

		// A cooked range's indices are in the page in this order already
		var indexBytes = set.InPlace ? 0 : (set.Indices.Length * (wide ? 4L : 2L) + 3) & ~3L;
		var recordBytes = set.Meshlets.Length * (long)MeshletSet.RecordBytes;
		var local = entry.Local;
		var triangleBytes = local is null ? 0 : local.Triangles.Length * 4L;
		var vertexBytes = local is null ? 0 : (local.Vertices.Length * (wide ? 4L : 2L) + 3) & ~3L;
		var tableBytes = entry.Corners is { } firstCorners ? firstCorners.Words.Length * 4L : 0;
		var staging = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( indexBytes + recordBytes + triangleBytes + vertexBytes + tableBytes, 4 ), Usage = BufferUsage.CopySource, Memory = MemoryLocation.Upload, Name = "Borealis meshlet staging" } );
		var mapped = gpu.Map( staging );
		if ( !set.InPlace ) Write( mapped, set.Indices, wide );
		MemoryMarshal.AsBytes( set.Meshlets.AsSpan() ).CopyTo( mapped[(int)indexBytes..] );
		if ( local is not null )
		{
			MemoryMarshal.AsBytes( local.Triangles.AsSpan() ).CopyTo( mapped[(int)(indexBytes + recordBytes)..] );
			Write( mapped[(int)(indexBytes + recordBytes + triangleBytes)..], local.Vertices, wide );
		}

		if ( entry.Corners is { } table ) MemoryMarshal.AsBytes( table.Words.AsSpan() ).CopyTo( mapped[(int)(indexBytes + recordBytes + triangleBytes + vertexBytes)..] );
		entry.CornersAt = indexBytes + recordBytes + triangleBytes + vertexBytes;

		entry.Staging = staging;
		entry.IndexBytes = set.InPlace ? 0 : set.Indices.Length * (wide ? 4L : 2L);
		entry.LocalTrianglesAt = indexBytes + recordBytes;
		entry.LocalVerticesAt = indexBytes + recordBytes + triangleBytes;
		entry.LocalVertexBytes = vertexBytes;

		static void Write( Span<byte> into, uint[] values, bool wide )
		{
			if ( wide )
			{
				MemoryMarshal.AsBytes( values.AsSpan() ).CopyTo( into );
				return;
			}

			var narrow = MemoryMarshal.Cast<byte, ushort>( into[..(values.Length * 2)] );
			for ( int k = 0; k < values.Length; k++ ) narrow[k] = (ushort)values[k];
		}
	}

	bool Claim( in Range range )
	{
		if ( !pageRanges.TryGetValue( range.PageSlot, out var list ) ) pageRanges[range.PageSlot] = list = new();
		var start = range.FirstIndex;
		var end = range.FirstIndex + range.IndexCount;

		// The nearest at or before the start, and the next after it
		var keys = list.Keys;
		int lo = 0, hi = keys.Count - 1, before = -1;
		while ( lo <= hi )
		{
			var mid = (lo + hi) / 2;
			if ( keys[mid] <= start ) { before = mid; lo = mid + 1; }
			else hi = mid - 1;
		}

		if ( before >= 0 && list.Values[before] > start ) return false;
		if ( before + 1 < keys.Count && keys[before + 1] < end ) return false;
		list.Add( start, end );
		return true;
	}

	void BuildOn( Entry entry )
	{
		try
		{
			using var zone = Zones.MeshletBuild.Start();
			var started = Stopwatch.GetTimestamp();
			var s = entry.Source;
			var count = (int)entry.Range.IndexCount;

			// Cooked into the model with this config: taken as it is, its indices already in that order
			var set = Cooked( s, count );
			var cooked = set is not null;
			if ( cooked ) Interlocked.Increment( ref CookedHits );
			else
			{
				// The range's indices and its vertices' positions, from the lowest index it uses, in pooled scratch: with the
				// config, the cache's key
				var indexArray = ScratchPool<uint>.Rent( count );
				V3[] positionArray = null;
				try
				{
					var indices = indexArray.AsSpan( 0, count );
					uint min = uint.MaxValue, max = 0;
					for ( int k = 0; k < count; k++ )
					{
						var index = s.Indices[s.IndexBase + k];
						indices[k] = index;
						min = Math.Min( min, index );
						max = Math.Max( max, index );
					}

					var vertexCount = count == 0 ? 0 : (int)(max - min + 1);
					positionArray = ScratchPool<V3>.Rent( vertexCount );
					for ( int v = 0; v < vertexCount; v++ ) positionArray[v] = Position( s, s.VertexBase + (int)min + v );
					var key = CacheFolder is null ? null : Key( indices, positionArray.AsSpan( 0, vertexCount ) );
					set = key is null ? null : ReadCached( key );
					if ( set is not null ) Interlocked.Increment( ref CacheHits );
					else
					{
						var positions = positionArray;
						set = MeshletBuilder.Build( indices, v => positions[v - s.VertexBase - (int)min], s.VertexBase, Config );
						if ( key is not null && set is not null ) WriteCached( key, set );
					}
				}
				finally
				{
					ScratchPool<uint>.Return( indexArray );
					if ( positionArray is not null ) ScratchPool<V3>.Return( positionArray );
				}
			}

			Interlocked.Add( ref cooked ? ref CookedTicks : ref BuildTicks, Stopwatch.GetTimestamp() - started );
			entry.Set = set is not null && set.Meshlets.Length <= MaxRunMeshlets ? set : null;
			if ( LocalData && entry.Set is not null ) entry.Local = entry.Set.Local();
			if ( CornerData && entry.Set is not null && entry.Set.Corners() is { } table && table.Words.Length * 4L <= DataPageBytes ) entry.Corners = table;
			if ( entry.Set is not null ) (entry.CornerLow, entry.CornerSpan) = entry.Set.FirstCorners();
			if ( StageOnWorkers && entry.Set is not null ) Stage( entry );
			built.Enqueue( entry );
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"Meshlets: building a range failed: {e.Message}" );
			entry.Set = null;
			built.Enqueue( entry );
		}
	}

	/// <summary>
	/// A range's cooked meshlets (<see cref="Borealis.Source2.Models.MeshIndexBuffer.Meshlets"/>), when they were cut with
	/// this store's config and cover it; null to build it.
	/// </summary>
	MeshletSet Cooked( Source s, int count )
	{
		if ( !UseCooked || s.Indices.Meshlets is not { } cooked ) return null;
		var records = cooked.MaxVertices == Config.MaxVertices && cooked.MaxTriangles == Config.MaxTriangles ? cooked.Find( s.IndexBase, count, s.Vertices, s.VertexBase ) : default;
		MeshletSet set = null;
		if ( !records.IsEmpty )
		{
			// The set keeps its indices (for local data and its triangle count): read once, nothing else
			var indices = new uint[count];
			for ( int k = 0; k < count; k++ ) indices[k] = s.Indices[s.IndexBase + k];
			set = MeshletSet.FromCooked( indices, records.Span, Config );
		}

		if ( set is null ) Interlocked.Increment( ref CookedMisses );
		return set;
	}

	/// <summary>
	/// Where a model draw's indices and vertices are on the CPU, for its index ranges' requests: the draw's first index in
	/// its index buffer and its vertex buffer's first vertex (a range adds its own first index and base vertex); null when the
	/// draw has no positions.
	/// </summary>
	public static Source SourceOf( BackendModel model, int mesh, int call, BufferHandle indexPage, IndexFormat format )
	{
		var m = model.File.Meshes[mesh];
		var c = m.DrawCalls[call];
		if ( c.VertexBuffers.Count == 0 ) return null;
		var (vbIndex, vbOffset) = c.VertexBuffers[0];
		var vb = m.VertexBuffers[vbIndex];
		if ( vb.Find( "POSITION" ) is not { } position || vb.Stride == 0 ) return null;
		var (ibIndex, ibOffset) = c.IndexBuffer;
		var ib = m.IndexBuffers[ibIndex];
		return new Source( indexPage, format, ib, ibOffset / ib.Size, vb, position, vbOffset / vb.Stride );
	}

	/// <summary>
	/// Where built ranges are kept between runs (<c>game/.source2/borealismeshlets</c>), by a hash of the config, the range's
	/// indices and its vertices' positions; null to build every time.
	/// </summary>
	public string CacheFolder { get; init; }

	/// <summary>Ranges read from the cache.</summary>
	public long CacheHits;

	const uint CacheMagic = 0x4C48534D, CacheVersion = 1;

	string Key( ReadOnlySpan<uint> indices, ReadOnlySpan<V3> positions )
	{
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		Span<int> head = [(int)CacheVersion, Config.MaxVertices, Config.MaxTriangles, indices.Length, positions.Length];
		hash.AppendData( MemoryMarshal.AsBytes( head ) );
		hash.AppendData( MemoryMarshal.AsBytes( indices ) );
		hash.AppendData( MemoryMarshal.AsBytes( positions ) );
		return Convert.ToHexString( hash.GetHashAndReset() );
	}

	string CachePath( string key ) => System.IO.Path.Combine( CacheFolder, key[..2], key + ".bin" );

	MeshletSet ReadCached( string key )
	{
		try
		{
			var path = CachePath( key );
			if ( !System.IO.File.Exists( path ) ) return null;
			// Read into pooled scratch: only the set's own arrays are new
			var buffer = PooledFile.Read( path, out var length );
			try
			{
				if ( length < 16 ) return null;
				var bytes = buffer.AsSpan( 0, length );
				var head = MemoryMarshal.Cast<byte, uint>( bytes[..16] );
				if ( head[0] != CacheMagic || head[1] != CacheVersion ) return null;
				var (indexCount, meshletCount) = ((int)head[2], (int)head[3]);
				if ( length != 16 + indexCount * 4 + meshletCount * MeshletSet.RecordBytes ) return null;
				return new MeshletSet
				{
					Indices = MemoryMarshal.Cast<byte, uint>( bytes.Slice( 16, indexCount * 4 ) ).ToArray(),
					Meshlets = MemoryMarshal.Cast<byte, MeshletRecord>( bytes[(16 + indexCount * 4)..] ).ToArray(),
				};
			}
			finally
			{
				PooledFile.Return( buffer );
			}
		}
		catch ( System.IO.IOException )
		{
			return null;
		}
	}

	void WriteCached( string key, MeshletSet set )
	{
		try
		{
			var path = CachePath( key );
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) );
			var temp = path + "." + Environment.CurrentManagedThreadId + ".tmp";
			using ( var file = System.IO.File.Create( temp ) )
			{
				Span<uint> head = [CacheMagic, CacheVersion, (uint)set.Indices.Length, (uint)set.Meshlets.Length];
				file.Write( MemoryMarshal.AsBytes( head ) );
				file.Write( MemoryMarshal.AsBytes( set.Indices.AsSpan() ) );
				file.Write( MemoryMarshal.AsBytes( set.Meshlets.AsSpan() ) );
			}

			System.IO.File.Move( temp, path, overwrite: true );
		}
		catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
		{
			// A cache that can't be written builds again next time
		}
	}

	static V3 Position( Source s, int vertex )
	{
		var p = s.Vertices.Read( s.Position, vertex );
		return new V3( p.X, p.Y, p.Z );
	}

	/// <summary>
	/// A draw record over <paramref name="range"/>: given the range's run now if it's published, else when it is. Draws over a
	/// range never asked for keep no run.
	/// </summary>
	public void RegisterDraw( uint draw, in Range range )
	{
		lock ( gate )
		{
			if ( !entries.TryGetValue( range, out var entry ) )
			{
				if ( !waitingDraws.TryGetValue( range, out var waiting ) ) waitingDraws[range] = waiting = new();
				waiting.Add( draw );
				return;
			}

			if ( entry.State == State.None ) return;
			entry.Draws.Add( draw );
			if ( entry.State == State.Published ) WriteRun( draw, entry );
		}
	}

	/// <summary>Stop publishing runs for retired draw records, without changing the runs in-flight frames can read.</summary>
	internal void ForgetDraws( HashSet<uint> draws )
	{
		lock ( gate )
		{
			Predicate<uint> retired = draws.Contains;
			foreach ( var entry in entries.Values ) entry.Draws.RemoveAll( retired );
			foreach ( var waiting in waitingDraws.Values ) waiting.RemoveAll( retired );
		}
	}

	/// <summary>Clear the old mappings before a retired draw record's slot is reused for a different range.</summary>
	internal void ClearDraws( List<uint> draws )
	{
		lock ( gate )
		{
			var runData = runs.IsValid ? gpu.Map( runs ) : Span<byte>.Empty;
			var cornerData = corners.IsValid ? gpu.Map( corners ) : Span<byte>.Empty;
			foreach ( var draw in draws )
			{
				if ( draw < runData.Length / RunBytes ) runData.Slice( (int)draw * RunBytes, RunBytes ).Clear();
				if ( draw < cornerData.Length / CornerRunBytes ) cornerData.Slice( (int)draw * CornerRunBytes, CornerRunBytes ).Clear();
			}
		}
	}

	/// <summary>
	/// Forget the ranges in the index page at <paramref name="slot"/>, whose meshes have all gone (the page is being freed,
	/// and its slot may name another page later); false while one is being built or uploaded, to be asked again next frame.
	/// Their meshlet records go back for reuse and their draws' runs are cleared (<see cref="ClearRun"/>).
	/// </summary>
	public bool TryForgetPage( int slot )
	{
		lock ( gate )
		{
			foreach ( var (range, entry) in entries )
			{
				if ( range.PageSlot == slot && entry.State is State.Building or State.Built or State.Uploaded ) return false;
			}

			foreach ( var range in entries.Keys.Where( r => r.PageSlot == slot ).ToList() )
			{
				if ( !entries.Remove( range, out var entry ) || entry.State != State.Published ) continue;
				foreach ( var draw in entry.Draws ) ClearRun( draw );
				if ( entry.Count > 0 ) FreeRecords( (int)(entry.Global / PageRecords), (int)entry.First, (int)entry.Count );
			}

			foreach ( var range in waitingDraws.Keys.Where( r => r.PageSlot == slot ).ToList() ) waitingDraws.Remove( range );
			pageRanges.Remove( slot );
			return true;
		}
	}

	/// <summary>A forgotten range's records go back for reuse, merged with free ones beside them. Under the gate.</summary>
	void FreeRecords( int page, int first, int count )
	{
		// The page being filled goes at the next frame's start instead (BeginFrame), on the thread that fills it
		pageLive[page] -= count;
		if ( pageLive[page] == 0 && page != pageIndex )
		{
			ReleasePage( page );
			return;
		}

		var i = 0;
		while ( i < freeRecords.Count && (freeRecords[i].Page, freeRecords[i].First).CompareTo( (page, first) ) < 0 ) i++;
		freeRecords.Insert( i, (page, first, count) );
		if ( i + 1 < freeRecords.Count && freeRecords[i + 1].Page == page && first + count == freeRecords[i + 1].First )
		{
			freeRecords[i] = (page, first, count + freeRecords[i + 1].Count);
			freeRecords.RemoveAt( i + 1 );
		}

		if ( i > 0 && freeRecords[i - 1].Page == page && freeRecords[i - 1].First + freeRecords[i - 1].Count == first )
		{
			freeRecords[i - 1] = (page, freeRecords[i - 1].First, freeRecords[i - 1].Count + freeRecords[i].Count);
			freeRecords.RemoveAt( i );
		}
	}

	/// <summary>
	/// Let a page no records are in go, once the frames that may read it are done, and forget its free records: nothing draws
	/// them (their ranges were forgotten with their meshes). Its index is the next new page's; pages at the end go from the
	/// list, so <see cref="MeshletCapacity"/> shrinks again. Under the gate.
	/// </summary>
	void ReleasePage( int index )
	{
		gpu.Destroy( pages[index] );
		pages[index] = default;
		freeRecords.RemoveAll( r => r.Page == index );
		while ( pages.Count > 0 && !pages[^1].IsValid )
		{
			pages.RemoveAt( pages.Count - 1 );
			pageLive.RemoveAt( pageLive.Count - 1 );
		}

		PagesReleased++;
	}

	/// <summary>
	/// Room for <paramref name="count"/> records among forgotten ranges' (the first that fits): its page index and first
	/// record, or (-1, 0). Their old ranges' draws have gone, and the upload's list comes after every earlier submission's
	/// reads. Under the gate.
	/// </summary>
	(int Page, int First) TakeRecords( int count )
	{
		for ( int i = 0; i < freeRecords.Count; i++ )
		{
			var (page, first, free) = freeRecords[i];
			if ( free < count ) continue;
			if ( free == count ) freeRecords.RemoveAt( i );
			else freeRecords[i] = (page, first + count, free - count);
			ReusedRecords += count;
			return (page, first);
		}

		return (-1, 0);
	}

	/// <summary>
	/// At a frame's start, on the thread that records it: publish rewrites old enough to be safe, then upload built ranges
	/// within the frame's budget, in a list of its own submitted ahead of the frame's.
	/// </summary>
	public void BeginFrame()
	{
		frame++;
		lock ( gate )
		{
			// The page being filled, once every range in it has been forgotten (a map unloaded): the next record makes a new one
			if ( pageIndex >= 0 && pageLive[pageIndex] == 0 )
			{
				ReleasePage( pageIndex );
				(page, pageIndex, pageSlot, pageUsed) = (default, -1, -1, PageRecords);
			}

			for ( int i = retiredRuns.Count - 1; i >= 0; i-- )
			{
				if ( gpu.FrameNumber - retiredRuns[i].Frame < GpuDrivenTables.RetiredFrames ) continue;
				gpu.Destroy( retiredRuns[i].Buffer );
				retiredRuns.RemoveAt( i );
			}

			for ( int i = uploaded.Count - 1; i >= 0; i-- )
			{
				var entry = uploaded[i];
				if ( frame - entry.UploadedFrame < PublishDelay ) continue;
				entry.State = State.Published;
				foreach ( var draw in entry.Draws ) WriteRun( draw, entry );
				Published++;
				Meshlets += entry.Set.Meshlets.Length;
				Triangles += entry.Set.Triangles;
				if ( entry.Source.Format == IndexFormat.UInt16 ) Triangles16 += entry.Set.Triangles;
				entry.Set = null;
				entry.Local = null;
				entry.Corners = null;
				entry.Source = null;
				uploaded.RemoveAt( i );
			}
		}

		if ( built.IsEmpty ) return;
		using var zone = Zones.MeshletUpload.Start();
		CommandList list = null;
		long bytes = 0;
		var most = UploadBudget;
		while ( bytes < most && built.TryDequeue( out var entry ) )
		{
			lock ( gate ) Built++;
			if ( entry.Set is not { } set )
			{
				lock ( gate ) entry.State = State.None;
				continue;
			}

			if ( list is null )
			{
				list = gpu.BeginCommandList( QueueType.Graphics, "Meshlet uploads" );

				// After every earlier submission's reads of the pages
				list.Barrier( PipelineStage.AllCommands, MemoryAccess.IndexRead | MemoryAccess.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			}

			// The range's triangles in meshlet order, in the page's index size
			var s = entry.Source;
			var range = entry.Range;
			var staging = entry.Staging;
			if ( set.InPlace )
			{
				// Cooked: already in this order in the page
			}
			else if ( staging.IsValid )
			{
				list.CopyBuffer( staging, 0, s.IndexPage, range.FirstIndex * (s.Format == IndexFormat.UInt32 ? 4L : 2L), entry.IndexBytes );
				bytes += entry.IndexBytes;
			}
			else if ( s.Format == IndexFormat.UInt32 )
			{
				list.UpdateBuffer<uint>( s.IndexPage, set.Indices, range.FirstIndex * 4L );
				bytes += set.Indices.Length * 4L;
			}
			else
			{
				var narrow = new ushort[set.Indices.Length];
				for ( int k = 0; k < narrow.Length; k++ ) narrow[k] = (ushort)set.Indices[k];
				list.UpdateBuffer<ushort>( s.IndexPage, narrow, range.FirstIndex * 2L );
				bytes += narrow.Length * 2L;
			}

			// Its records, in a forgotten range's records or in a page with room for all of them
			int target, first;
			lock ( gate ) (target, first) = LocalData ? (-1, 0) : TakeRecords( set.Meshlets.Length );
			if ( target < 0 && pageUsed + set.Meshlets.Length > PageRecords )
			{
				var size = PageRecords * (long)(MeshletSet.RecordBytes + (LocalData ? LocalRecordBytes : 0));
				page = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Borealis meshlet page" } );
				lock ( gate )
				{
					// In the place of one let go, if any
					pageIndex = pages.FindIndex( p => !p.IsValid );
					if ( pageIndex < 0 )
					{
						pageIndex = pages.Count;
						pages.Add( page );
						pageLive.Add( 0 );
					}
					else pages[pageIndex] = page;
				}

				pageSlot = gpu.BindlessIndex( page );
				pageUsed = 0;
			}

			var appended = target < 0;
			if ( appended ) (target, first) = (pageIndex, pageUsed);
			BufferHandle into;
			lock ( gate ) into = pages[target];
			var recordBytes = set.Meshlets.Length * (long)MeshletSet.RecordBytes;
			if ( staging.IsValid ) list.CopyBuffer( staging, (entry.IndexBytes + 3) & ~3L, into, first * (long)MeshletSet.RecordBytes, recordBytes );
			else list.UpdateBuffer<MeshletRecord>( into, set.Meshlets, first * (long)MeshletSet.RecordBytes );
			bytes += recordBytes;
			if ( entry.Local is { } local ) bytes += UploadLocal( list, set, local, s.Format == IndexFormat.UInt32, entry );
			if ( entry.Corners is { } table ) bytes += UploadCorners( list, table, entry );

			// Freed once the GPU has run the copies
			if ( staging.IsValid ) gpu.Destroy( staging );
			entry.Staging = default;

			lock ( gate )
			{
				entry.PageSlot = (uint)(appended ? pageSlot : gpu.BindlessIndex( into ));
				entry.First = (uint)first;
				entry.Count = (uint)set.Meshlets.Length;
				entry.Global = (uint)(target * PageRecords + first);
				pageLive[target] += set.Meshlets.Length;
				entry.State = State.Uploaded;
				entry.UploadedFrame = frame;
				uploaded.Add( entry );
			}

			if ( appended ) pageUsed += set.Meshlets.Length;
		}

		if ( list is null ) return;
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		gpu.Submit( list );
	}

	/// <summary>
	/// A range's local triangles and vertices into a data page with room for them (vertices in the range's index size), and
	/// each meshlet's local record after its page's records. Bytes uploaded.
	/// </summary>
	long UploadLocal( CommandList list, MeshletSet set, MeshletLocal local, bool wide, Entry entry )
	{
		var triangleBytes = local.Triangles.Length * 4L;
		var vertexBytes = (local.Vertices.Length * (wide ? 4L : 2L) + 3) & ~3L;
		if ( dataUsed + triangleBytes + vertexBytes > DataPageBytes )
		{
			dataPage = gpu.CreateBuffer( new BufferInfo { Size = DataPageBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Borealis meshlet data page" } );
			dataPages.Add( dataPage );
			dataSlot = gpu.BindlessIndex( dataPage );
			dataUsed = 0;
		}

		var trianglesAt = dataUsed;
		var verticesAt = dataUsed + triangleBytes;
		if ( entry.Staging.IsValid )
		{
			if ( triangleBytes > 0 ) list.CopyBuffer( entry.Staging, entry.LocalTrianglesAt, dataPage, trianglesAt, triangleBytes );
			if ( entry.LocalVertexBytes > 0 ) list.CopyBuffer( entry.Staging, entry.LocalVerticesAt, dataPage, verticesAt, entry.LocalVertexBytes );
		}
		else if ( wide )
		{
			list.UpdateBuffer<uint>( dataPage, local.Triangles, trianglesAt );
			list.UpdateBuffer<uint>( dataPage, local.Vertices, verticesAt );
		}
		else
		{
			list.UpdateBuffer<uint>( dataPage, local.Triangles, trianglesAt );
			var narrow = new ushort[(int)(vertexBytes / 2)];
			for ( int k = 0; k < local.Vertices.Length; k++ ) narrow[k] = (ushort)local.Vertices[k];
			list.UpdateBuffer<ushort>( dataPage, narrow, verticesAt );
		}

		dataUsed += triangleBytes + vertexBytes;

		var records = new MeshletLocalRecord[set.Meshlets.Length];
		for ( int m = 0; m < set.Meshlets.Length; m++ )
		{
			records[m] = new MeshletLocalRecord
			{
				Data = (uint)dataSlot,
				Triangles = (uint)(trianglesAt + set.Meshlets[m].FirstTriangle * 4L),
				Vertices = (uint)(verticesAt + local.FirstVertex[m] * (wide ? 4L : 2L)),
				VertexCount = (uint)local.VertexCount[m],
			};
		}

		list.UpdateBuffer<MeshletLocalRecord>( page, records, PageRecords * (long)MeshletSet.RecordBytes + pageUsed * (long)LocalRecordBytes );
		return triangleBytes + vertexBytes + records.Length * (long)LocalRecordBytes;
	}

	/// <summary>
	/// A range's first-corner table into a data page with room for it, and where it went in the entry for its corner runs.
	/// Bytes uploaded.
	/// </summary>
	long UploadCorners( CommandList list, MeshletCorners table, Entry entry )
	{
		var bytes = table.Words.Length * 4L;
		if ( dataUsed + bytes > DataPageBytes )
		{
			dataPage = gpu.CreateBuffer( new BufferInfo { Size = DataPageBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Borealis meshlet data page" } );
			dataPages.Add( dataPage );
			dataSlot = gpu.BindlessIndex( dataPage );
			dataUsed = 0;
		}

		if ( entry.Staging.IsValid ) list.CopyBuffer( entry.Staging, entry.CornersAt, dataPage, dataUsed, bytes );
		else list.UpdateBuffer<uint>( dataPage, table.Words, dataUsed );
		lock ( gate )
		{
			entry.CornerSlot = (uint)dataSlot + 1;
			entry.CornerAt = (uint)dataUsed;
			(entry.CornerLow, entry.CornerSpan) = (table.Low, table.Span);
			CornerTableBytes += bytes;
		}

		dataUsed += bytes;
		return bytes;
	}

	void WriteRun( uint draw, Entry entry )
	{
		// The corner run first, its slot last: a resolve finds no table until it's whole. The lowest first corner and the
		// span in one write: compaction, which reads them together, sees both or neither
		if ( entry.CornerSpan != 0 )
		{
			var cornerAt = draw * (long)CornerRunBytes;
			if ( cornerAt + CornerRunBytes > cornerBytes || !corners.IsValid ) GrowCorners( cornerAt + CornerRunBytes );
			var corner = MemoryMarshal.Cast<byte, uint>( gpu.Map( corners ).Slice( (int)cornerAt, CornerRunBytes ) );
			corner[1] = entry.CornerAt;
			Volatile.Write( ref MemoryMarshal.Cast<uint, ulong>( corner )[1], entry.CornerLow | (ulong)entry.CornerSpan << 32 );
			Thread.MemoryBarrier();
			Volatile.Write( ref corner[0], entry.CornerSlot );
		}

		var at = draw * (long)RunBytes;
		if ( at + RunBytes > runBytes ) GrowRuns( at + RunBytes );
		ref var run = ref MemoryMarshal.AsRef<MeshletRun>( gpu.Map( runs ).Slice( (int)at, RunBytes ) );

		// The count last: a cull reading in between sees no meshlets rather than a wrong page
		run.Page = entry.PageSlot;
		run.First = entry.First;
		run.Global = entry.Global;
		Thread.MemoryBarrier();
		Volatile.Write( ref run.Count, entry.Count );
	}

	/// <summary>
	/// Clear a forgotten range's draw's run (<see cref="TryForgetPage"/>), as <see cref="GpuDrivenTables"/> reuses the slots
	/// of draw records that went with their page: a record made over it later reads no meshlets until its own range's are
	/// published, rather than the freed range's. Under the gate.
	/// </summary>
	void ClearRun( uint draw )
	{
		var at = draw * (long)RunBytes;
		if ( runs.IsValid && at + RunBytes <= runBytes )
		{
			// The count first: a cull reading in between sees no meshlets
			ref var run = ref MemoryMarshal.AsRef<MeshletRun>( gpu.Map( runs ).Slice( (int)at, RunBytes ) );
			Volatile.Write( ref run.Count, 0 );
			Thread.MemoryBarrier();
			run = default;
		}

		var cornerAt = draw * (long)CornerRunBytes;
		if ( corners.IsValid && cornerAt + CornerRunBytes <= cornerBytes )
		{
			// Its slot first: a resolve finds no table without one
			var corner = MemoryMarshal.Cast<byte, uint>( gpu.Map( corners ).Slice( (int)cornerAt, CornerRunBytes ) );
			Volatile.Write( ref corner[0], 0 );
			Thread.MemoryBarrier();
			corner.Clear();
		}
	}

	/// <summary>Every meshlet's index is below this: what a view's meshlet bits must hold.</summary>
	public int MeshletCapacity
	{
		get
		{
			lock ( gate ) return pages.Count * PageRecords;
		}
	}

	void GrowRuns( long needed )
	{
		var size = Math.Max( runBytes * 2, 1 << 16 );
		while ( size < needed ) size *= 2;
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = "Borealis meshlet runs" } );
		var mapped = gpu.Map( buffer );
		mapped.Clear();
		if ( runs.IsValid )
		{
			gpu.Map( runs )[..(int)runBytes].CopyTo( mapped );
			retiredRuns.Add( (runs, gpu.FrameNumber) );
		}

		runs = buffer;
		runBytes = size;
		runSlot = gpu.BindlessIndex( buffer );
	}

	void GrowCorners( long needed )
	{
		var size = Math.Max( cornerBytes * 2, 1 << 16 );
		while ( size < needed ) size *= 2;
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Storage, Memory = MemoryLocation.Upload, Name = "Borealis meshlet corner runs" } );
		var mapped = gpu.Map( buffer );
		mapped.Clear();
		if ( corners.IsValid )
		{
			gpu.Map( corners )[..(int)cornerBytes].CopyTo( mapped );
			retiredRuns.Add( (corners, gpu.FrameNumber) );
		}

		corners = buffer;
		cornerBytes = size;
		cornerSlot = gpu.BindlessIndex( buffer );
	}

	public void Dispose()
	{
		// Builds not started yet do nothing; running ones finish first (bounded, so a build stuck on the device can't hold up
		// the shutdown for good)
		disposed = true;
		lock ( flight )
		{
			var until = Environment.TickCount64 + 10_000;
			while ( building > 0 && Environment.TickCount64 < until ) Monitor.Wait( flight, 100 );
		}

		while ( built.TryDequeue( out var entry ) )
		{
			if ( entry.Staging.IsValid ) gpu.Destroy( entry.Staging );
		}

		foreach ( var p in pages )
		{
			if ( p.IsValid ) gpu.Destroy( p );
		}

		pages.Clear();
		pageLive.Clear();
		foreach ( var p in dataPages ) gpu.Destroy( p );
		dataPages.Clear();
		if ( runs.IsValid ) gpu.Destroy( runs );
		if ( corners.IsValid ) gpu.Destroy( corners );
		foreach ( var (r, _) in retiredRuns ) gpu.Destroy( r );
		retiredRuns.Clear();
	}
}
