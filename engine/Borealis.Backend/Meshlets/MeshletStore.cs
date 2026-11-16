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
/// The meshlets of GPU-driven index ranges (docs/managed/borealis-gpu-driven.md, "Meshlets"). A range is asked for as its
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

	/// <summary>Frames a rewrite waits before its runs are written: frames in flight may read the range's old order.</summary>
	const int PublishDelay = 4;

	readonly Device gpu;

	public MeshletConfig Config { get; }

	/// <summary>Bytes a frame uploads at most (indices and records); the rest waits for the next frame.</summary>
	public long FrameUploadBytes { get; set; } = 16 << 20;

	enum State { Building, Built, Uploaded, Published, None }

	sealed class Entry
	{
		public Range Range;
		public Source Source;
		public State State;
		public MeshletSet Set;
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
	BufferHandle page;
	int pageSlot = -1, pageUsed = PageRecords;
	long frame;

	// The run table: 8 bytes per draw record
	BufferHandle runs;
	int runSlot = -1;
	long runBytes;
	readonly List<BufferHandle> retiredRuns = new();

	/// <summary>Ranges asked for, built, published and refused; meshlets and triangles published; build time on the workers.</summary>
	public long Requested, Built, Published, Refused, Meshlets, Triangles, BuildTicks;

	public MeshletStore( Device gpu, MeshletConfig config, int threads )
	{
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
			for ( int k = 0; k < count; k++ ) indices[k] = s.Indices[s.IndexBase + k];
			var set = MeshletBuilder.Build( indices, v => Position( s, v ), s.VertexBase, Config );
			Interlocked.Add( ref BuildTicks, Stopwatch.GetTimestamp() - started );
			entry.Set = set is not null && set.Meshlets.Length <= MaxRunMeshlets ? set : null;
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
	/// At a frame's start, on the thread that records it: publish rewrites old enough to be safe, then upload built ranges
	/// within the frame's budget, in a list of its own submitted ahead of the frame's.
	/// </summary>
	public void BeginFrame()
	{
		frame++;
		lock ( gate )
		{
			for ( int i = uploaded.Count - 1; i >= 0; i-- )
			{
				var entry = uploaded[i];
				if ( frame - entry.UploadedFrame < PublishDelay ) continue;
				entry.State = State.Published;
				foreach ( var draw in entry.Draws ) WriteRun( draw, entry );
				Published++;
				Meshlets += entry.Set.Meshlets.Length;
				Triangles += entry.Set.Triangles;
				entry.Set = null;
				entry.Source = null;
				uploaded.RemoveAt( i );
			}
		}

		if ( built.IsEmpty ) return;
		using var zone = Zones.MeshletUpload.Start();
		CommandList list = null;
		long bytes = 0;
		while ( bytes < FrameUploadBytes && built.TryDequeue( out var entry ) )
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
			if ( s.Format == IndexFormat.UInt32 )
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

			// Its records, in a page with room for all of them
			if ( pageUsed + set.Meshlets.Length > PageRecords )
			{
				page = gpu.CreateBuffer( new BufferInfo { Size = PageRecords * MeshletSet.RecordBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Memory = MemoryLocation.Device, Name = "Borealis meshlet page" } );
				lock ( gate ) pages.Add( page );
				pageSlot = gpu.BindlessIndex( page );
				pageUsed = 0;
			}

			list.UpdateBuffer<MeshletRecord>( page, set.Meshlets, pageUsed * (long)MeshletSet.RecordBytes );
			bytes += set.Meshlets.Length * (long)MeshletSet.RecordBytes;

			lock ( gate )
			{
				entry.PageSlot = (uint)pageSlot;
				entry.First = (uint)pageUsed;
				entry.Count = (uint)set.Meshlets.Length;
				entry.Global = (uint)((pages.Count - 1) * PageRecords + pageUsed);
				entry.State = State.Uploaded;
				entry.UploadedFrame = frame;
				uploaded.Add( entry );
			}

			pageUsed += set.Meshlets.Length;
		}

		if ( list is null ) return;
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		gpu.Submit( list );
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
			retiredRuns.Add( runs );
		}

		runs = buffer;
		runBytes = size;
		runSlot = gpu.BindlessIndex( buffer );
	}

	public void Dispose()
	{
		foreach ( var p in pages ) gpu.Destroy( p );
		pages.Clear();
		if ( runs.IsValid ) gpu.Destroy( runs );
		foreach ( var r in retiredRuns ) gpu.Destroy( r );
		retiredRuns.Clear();
	}
}
