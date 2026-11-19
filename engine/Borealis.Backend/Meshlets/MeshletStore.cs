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
	public const int RunBytes = 16;

	/// <summary>
	/// Bytes of a meshlet's local record, after a page's <see cref="PageRecords"/> records when <see cref="LocalData"/> is on:
	/// its data page's bindless slot, where its triangles and its vertices start there (bytes), and its vertex count.
	/// </summary>
	public const int LocalRecordBytes = 16;

	/// <summary>Bytes of a data page, which holds ranges' local triangles and vertices (a range's in one page).</summary>
	public const int DataPageBytes = 32 << 20;

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
	/// Whether a built range's bytes are written on its build worker into a staging buffer of its own (on), so the upload on
	/// the frame's thread only records copies; off writes them there (narrowing 16-bit indices too). For comparisons.
	/// </summary>
	public static bool StageOnWorkers { get; set; } = true;

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
	readonly SemaphoreSlim workers;
	readonly List<BufferHandle> pages = new();

	// Records of ranges forgotten with their geometry page (page index, first, count), sorted and never adjacent: taken before
	// the last page fills, so maps loaded one after another reuse them rather than adding pages
	readonly List<(int Page, int First, int Count)> freeRecords = new();

	/// <summary>Meshlet records reused from forgotten ranges, for soaks and tests.</summary>
	public long ReusedRecords { get; private set; }
	BufferHandle page;
	int pageSlot = -1, pageUsed = PageRecords;
	readonly List<BufferHandle> dataPages = new();
	BufferHandle dataPage;
	int dataSlot = -1;
	long dataUsed = DataPageBytes;
	long frame;

	// The run table: 8 bytes per draw record
	BufferHandle runs;
	int runSlot = -1;
	long runBytes;
	// Run tables it grew out of, with the device frame they were retired in: draws recorded in that frame read them
	readonly List<(BufferHandle Buffer, ulong Frame)> retiredRuns = new();

	/// <summary>Ranges asked for, built, published and refused; meshlets and triangles published; build time on the workers.</summary>
	public long Requested, Built, Published, Refused, Meshlets, Triangles, Triangles16, BuildTicks;

	public MeshletStore( Device gpu, MeshletConfig config, int threads, string cacheFolder = null )
	{
		CacheFolder = cacheFolder;
		this.gpu = gpu;
		Config = config;
		workers = new SemaphoreSlim( Math.Max( threads, 1 ) );
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
			Task.Run( () => BuildOn( entry ) );
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
		var indexBytes = (set.Indices.Length * (wide ? 4L : 2L) + 3) & ~3L;
		var recordBytes = set.Meshlets.Length * (long)MeshletSet.RecordBytes;
		var local = entry.Local;
		var triangleBytes = local is null ? 0 : local.Triangles.Length * 4L;
		var vertexBytes = local is null ? 0 : (local.Vertices.Length * (wide ? 4L : 2L) + 3) & ~3L;
		var staging = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( indexBytes + recordBytes + triangleBytes + vertexBytes, 4 ), Usage = BufferUsage.CopySource, Memory = MemoryLocation.Upload, Name = "Borealis meshlet staging" } );
		var mapped = gpu.Map( staging );
		Write( mapped, set.Indices, wide );
		MemoryMarshal.AsBytes( set.Meshlets.AsSpan() ).CopyTo( mapped[(int)indexBytes..] );
		if ( local is not null )
		{
			MemoryMarshal.AsBytes( local.Triangles.AsSpan() ).CopyTo( mapped[(int)(indexBytes + recordBytes)..] );
			Write( mapped[(int)(indexBytes + recordBytes + triangleBytes)..], local.Vertices, wide );
		}

		entry.Staging = staging;
		entry.IndexBytes = set.Indices.Length * (wide ? 4L : 2L);
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
		workers.Wait();
		try
		{
			using var zone = Zones.MeshletBuild.Start();
			var started = Stopwatch.GetTimestamp();
			var s = entry.Source;
			var count = (int)entry.Range.IndexCount;
			var indices = new uint[count];
			uint min = uint.MaxValue, max = 0;
			for ( int k = 0; k < count; k++ )
			{
				var index = s.Indices[s.IndexBase + k];
				indices[k] = index;
				min = Math.Min( min, index );
				max = Math.Max( max, index );
			}

			// Its vertices' positions, from the lowest index it uses; with its indices and the config, the cache's key
			var positions = new V3[max - min + 1];
			for ( int v = 0; v < positions.Length; v++ ) positions[v] = Position( s, s.VertexBase + (int)min + v );
			var key = CacheFolder is null ? null : Key( indices, positions );
			var set = key is null ? null : ReadCached( key );
			if ( set is not null ) Interlocked.Increment( ref CacheHits );
			else
			{
				set = MeshletBuilder.Build( indices, v => positions[v - s.VertexBase - (int)min], s.VertexBase, Config );
				if ( key is not null && set is not null ) WriteCached( key, set );
			}

			Interlocked.Add( ref BuildTicks, Stopwatch.GetTimestamp() - started );
			entry.Set = set is not null && set.Meshlets.Length <= MaxRunMeshlets ? set : null;
			if ( LocalData && entry.Set is not null ) entry.Local = entry.Set.Local();
			if ( StageOnWorkers && entry.Set is not null ) Stage( entry );
			built.Enqueue( entry );
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"Meshlets: building a range failed: {e.Message}" );
			entry.Set = null;
			built.Enqueue( entry );
		}
		finally
		{
			workers.Release();
		}
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

	string Key( uint[] indices, V3[] positions )
	{
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		Span<int> head = [(int)CacheVersion, Config.MaxVertices, Config.MaxTriangles, indices.Length, positions.Length];
		hash.AppendData( MemoryMarshal.AsBytes( head ) );
		hash.AppendData( MemoryMarshal.AsBytes( indices.AsSpan() ) );
		hash.AppendData( MemoryMarshal.AsBytes( positions.AsSpan() ) );
		return Convert.ToHexString( hash.GetHashAndReset() );
	}

	string CachePath( string key ) => System.IO.Path.Combine( CacheFolder, key[..2], key + ".bin" );

	MeshletSet ReadCached( string key )
	{
		try
		{
			var path = CachePath( key );
			if ( !System.IO.File.Exists( path ) ) return null;
			var bytes = System.IO.File.ReadAllBytes( path );
			var head = MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( 0, 16 ) );
			if ( head[0] != CacheMagic || head[1] != CacheVersion ) return null;
			var (indexCount, meshletCount) = ((int)head[2], (int)head[3]);
			if ( bytes.Length != 16 + indexCount * 4 + meshletCount * MeshletSet.RecordBytes ) return null;
			return new MeshletSet
			{
				Indices = MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( 16, indexCount * 4 ) ).ToArray(),
				Meshlets = MemoryMarshal.Cast<byte, MeshletRecord>( bytes.AsSpan( 16 + indexCount * 4 ) ).ToArray(),
			};
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

	/// <summary>
	/// Forget the ranges in the index page at <paramref name="slot"/>, whose meshes have all gone (the page is being freed,
	/// and its slot may name another page later); false while one is being built or uploaded, to be asked again next frame.
	/// Their meshlet records stay where they are: nothing draws them any more.
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
				if ( entries.Remove( range, out var entry ) && entry.State == State.Published && entry.Count > 0 ) FreeRecords( (int)(entry.Global / PageRecords), (int)entry.First, (int)entry.Count );
			}

			foreach ( var range in waitingDraws.Keys.Where( r => r.PageSlot == slot ).ToList() ) waitingDraws.Remove( range );
			pageRanges.Remove( slot );
			return true;
		}
	}

	/// <summary>A forgotten range's records go back for reuse, merged with free ones beside them. Under the gate.</summary>
	void FreeRecords( int page, int first, int count )
	{
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
			if ( staging.IsValid )
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
				lock ( gate ) pages.Add( page );
				pageSlot = gpu.BindlessIndex( page );
				pageUsed = 0;
			}

			var appended = target < 0;
			if ( appended ) (target, first) = (pages.Count - 1, pageUsed);
			BufferHandle into;
			lock ( gate ) into = pages[target];
			var recordBytes = set.Meshlets.Length * (long)MeshletSet.RecordBytes;
			if ( staging.IsValid ) list.CopyBuffer( staging, (entry.IndexBytes + 3) & ~3L, into, first * (long)MeshletSet.RecordBytes, recordBytes );
			else list.UpdateBuffer<MeshletRecord>( into, set.Meshlets, first * (long)MeshletSet.RecordBytes );
			bytes += recordBytes;
			if ( entry.Local is { } local ) bytes += UploadLocal( list, set, local, s.Format == IndexFormat.UInt32, entry );

			// Freed once the GPU has run the copies
			if ( staging.IsValid ) gpu.Destroy( staging );
			entry.Staging = default;

			lock ( gate )
			{
				entry.PageSlot = (uint)(appended ? pageSlot : gpu.BindlessIndex( into ));
				entry.First = (uint)first;
				entry.Count = (uint)set.Meshlets.Length;
				entry.Global = (uint)(target * PageRecords + first);
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

		var records = new uint[set.Meshlets.Length * 4];
		for ( int m = 0; m < set.Meshlets.Length; m++ )
		{
			records[m * 4] = (uint)dataSlot;
			records[m * 4 + 1] = (uint)(trianglesAt + set.Meshlets[m].FirstTriangle * 4L);
			records[m * 4 + 2] = (uint)(verticesAt + local.FirstVertex[m] * (wide ? 4L : 2L));
			records[m * 4 + 3] = (uint)local.VertexCount[m];
		}

		list.UpdateBuffer<uint>( page, records, PageRecords * (long)MeshletSet.RecordBytes + pageUsed * (long)LocalRecordBytes );
		return triangleBytes + vertexBytes + records.Length * 4L;
	}

	void WriteRun( uint draw, Entry entry )
	{
		var at = draw * (long)RunBytes;
		if ( at + RunBytes > runBytes ) GrowRuns( at + RunBytes );
		var words = MemoryMarshal.Cast<byte, uint>( gpu.Map( runs ).Slice( (int)at, RunBytes ) );

		// The count last: a cull reading in between sees no meshlets rather than a wrong page
		words[0] = entry.PageSlot;
		words[1] = entry.First;
		words[3] = entry.Global;
		Thread.MemoryBarrier();
		Volatile.Write( ref words[2], entry.Count );
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

	public void Dispose()
	{
		while ( built.TryDequeue( out var entry ) )
		{
			if ( entry.Staging.IsValid ) gpu.Destroy( entry.Staging );
		}

		foreach ( var p in pages ) gpu.Destroy( p );
		pages.Clear();
		foreach ( var p in dataPages ) gpu.Destroy( p );
		dataPages.Clear();
		if ( runs.IsValid ) gpu.Destroy( runs );
		foreach ( var (r, _) in retiredRuns ) gpu.Destroy( r );
		retiredRuns.Clear();
	}
}
