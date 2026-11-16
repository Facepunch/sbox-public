using Borealis.Backend.Meshlets;
using Borealis.Gpu;
using Borealis.Source2.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Range = Borealis.Backend.Meshlets.MeshletStore.Range;
using Source = Borealis.Backend.Meshlets.MeshletStore.Source;
using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Lods;

/// <summary>
/// Generated levels of GPU-driven aggregate ranges (docs/managed/borealis-gpu-driven.md, "Generated LODs"). A fragment's
/// range is asked for as its records are written; its chain of coarser levels is built off the frame on workers
/// (<see cref="LodBuilder"/>), and once a GPU-driven colour pass draws the range too, the levels' indices go in its index
/// page's level room at a frame's start and every draw record over the range gets a LOD record: for each level its own
/// draw record (so it has meshlets of its own), first index, index count and error. The culls pick a level per fragment
/// from those records.
/// </summary>
internal sealed class LodStore : IDisposable
{
	/// <summary>Coarser levels a range can have.</summary>
	public const int MaxLevels = 6;

	/// <summary>
	/// Bytes of a LOD record, per draw record in the LOD table: the level count, a word unused, each level's error (a fraction
	/// of the fragment's bounds' half diagonal), then 16 bytes per level: first index, index count, draw record and a word unused.
	/// </summary>
	public const int RecordBytes = 128;

	/// <summary>Bytes before the levels kept per view: counters of level changes (changes, reversals, coarser, finer).</summary>
	public const int StateHeaderBytes = 64;

	/// <summary>Views that keep a level per fragment, with hysteresis; the rest pick theirs afresh each frame.</summary>
	public const int StateSlices = 8;

	/// <summary>Bytes of a view's levels: a byte per aggregate visibility fragment (level, last direction, changed before).</summary>
	public const int StateSliceBytes = GpuDrivenTables.FrameVisibilityFragmentBase;

	const uint CacheMagic = 0x53444F4C, CacheVersion = 1;
	const int CounterRing = 4;

	readonly Device gpu;
	readonly GpuDrivenTables tables;

	public LodConfig Config { get; }

	/// <summary>Where level ranges' meshlets are asked for, or null.</summary>
	public MeshletStore Meshlets { get; set; }

	/// <summary>Bytes a frame uploads at most (level indices); the rest waits for the next frame.</summary>
	public long FrameUploadBytes { get; set; } = 16 << 20;

	enum Phase { Building, Built, Published, None }

	sealed class Entry
	{
		public Range Range;
		public Source Source;
		public Phase State;
		public LodChain Chain;
		public bool Color;
		public (uint First, uint Count)[] Placed;
		public readonly List<uint> Draws = new();
	}

	readonly object gate = new();
	readonly Dictionary<Range, Entry> entries = new();
	readonly Dictionary<Range, List<uint>> waitingDraws = new();
	readonly HashSet<Range> levelRanges = new();
	readonly ConcurrentQueue<Entry> built = new();
	readonly List<Entry> ready = new();
	readonly ConcurrentQueue<(uint Draw, uint[] Record)> records = new();
	readonly SemaphoreSlim workers;

	BufferHandle table;
	int tableSlot = -1;
	long tableRecords;
	readonly List<BufferHandle> retired = new();
	volatile bool cleared;

	readonly BufferHandle state;
	readonly WeakReference<object>[] sliceOwners = new WeakReference<object>[StateSlices];
	readonly bool[] sliceReady = new bool[StateSlices];
	readonly List<int> sliceClears = new();
	readonly BufferHandle[] counterRing = new BufferHandle[CounterRing];
	long frame;

	/// <summary>Ranges asked for, built, published (with levels placed), dropped (no room for any) and with no level worth making; levels placed and their triangles; the published ranges' own triangles; build time on the workers; ranges read from the cache; levels left out for want of room.</summary>
	public long Requested, Built, Published, Dropped, Simplest, Levels, LevelTriangles, BaseTriangles, BuildTicks, CacheHits, NoRoom;

	public LodStore( Device gpu, GpuDrivenTables tables, LodConfig config, int threads, string cacheFolder = null )
	{
		this.gpu = gpu;
		this.tables = tables;
		Config = config;
		CacheFolder = cacheFolder;
		workers = new SemaphoreSlim( Math.Max( threads, 1 ) );
		GrowTable( null, 1 << 12 );
		state = gpu.CreateBuffer( new BufferInfo { Size = StateHeaderBytes + (long)StateSlices * StateSliceBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | BufferUsage.CopySource, Name = "Borealis LOD levels" } );
	}

	/// <summary>
	/// Where built chains are kept between runs (<c>game/.source2/borealislods</c>), by a hash of the config, the range's
	/// indices and its vertices' positions; null to build every time.
	/// </summary>
	public string CacheFolder { get; }

	/// <summary>The LOD table's bindless slot and how many draw records it covers, for the culls; set at a frame's start.</summary>
	public (int Slot, uint Records) Table => cleared ? (Volatile.Read( ref tableSlot ), (uint)Volatile.Read( ref tableRecords )) : (-1, 0u);

	/// <summary>The levels views keep, which the culls bind (<c>BorealisCullLevels</c>).</summary>
	public BufferHandle State => state;

	/// <summary>Slices views hold.</summary>
	public int SlicesTaken
	{
		get
		{
			lock ( sliceOwners ) return sliceOwners.Count( o => o is not null && o.TryGetTarget( out _ ) );
		}
	}

	/// <summary>
	/// The slice of <see cref="State"/> where <paramref name="view"/> keeps its fragments' levels, or -1 while it has none
	/// ready: a slice is taken the first time a view asks (one whose view is gone is reused) and cleared at the next frame's
	/// start, so every call of the view picks afresh until then and keeps levels from then on (a cleared level picks as
	/// afresh does), never both in one frame.
	/// </summary>
	public int SliceFor( object view )
	{
		lock ( sliceOwners )
		{
			var free = -1;
			for ( int i = 0; i < StateSlices; i++ )
			{
				if ( sliceOwners[i] is { } owner && owner.TryGetTarget( out var held ) )
				{
					if ( ReferenceEquals( held, view ) ) return sliceReady[i] ? i : -1;
				}
				else if ( free < 0 ) free = i;
			}

			if ( free < 0 ) return -1;
			sliceOwners[free] = new WeakReference<object>( view );
			sliceReady[free] = false;
			sliceClears.Add( free );
			return -1;
		}
	}

	/// <summary>Built ranges no GPU-driven colour pass has drawn, so not published, and their triangles.</summary>
	public (int Ranges, long Triangles) WaitingForColor
	{
		get
		{
			lock ( gate ) return (ready.Count( e => !e.Color ), ready.Where( e => !e.Color ).Sum( e => (long)e.Range.IndexCount / 3 ));
		}
	}

	/// <summary>Ranges still being built, or built and waiting for their upload (not those no colour pass has drawn).</summary>
	public int Pending
	{
		get
		{
			lock ( gate ) return entries.Values.Count( e => e.State is Phase.Building || e.State is Phase.Built && e.Color );
		}
	}

	/// <summary>
	/// Ask for a range's levels: built once, on a worker. They're published once a GPU-driven colour pass draws the range
	/// too (<paramref name="color"/>): a range drawn the classic way in colour must keep its full triangles in the prepass,
	/// whose depth the colour pass tests equal against.
	/// </summary>
	public void Request( in Range range, Source source, bool color )
	{
		if ( range.IndexCount / 3 < Config.MinRangeTriangles ) return;
		lock ( gate )
		{
			if ( entries.TryGetValue( range, out var known ) )
			{
				known.Color |= color;
				return;
			}

			var entry = new Entry { Range = range, Source = source, State = Phase.Building, Color = color };
			entries[range] = entry;
			if ( waitingDraws.Remove( range, out var draws ) ) entry.Draws.AddRange( draws );
			Requested++;
			Task.Run( () => BuildOn( entry ) );
		}
	}

	void BuildOn( Entry entry )
	{
		workers.Wait();
		try
		{
			using var zone = Zones.LodBuild.Start();
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

			var positions = new V3[max - min + 1];
			for ( int v = 0; v < positions.Length; v++ )
			{
				var p = s.Vertices.Read( s.Position, s.VertexBase + (int)min + v );
				positions[v] = new V3( p.X, p.Y, p.Z );
			}

			var key = CacheFolder is null ? null : Key( indices, positions );
			var empty = false;
			var cached = key is null ? null : ReadCached( key, out empty );
			LodChain chain;
			if ( cached is not null || empty )
			{
				chain = cached;
				Interlocked.Increment( ref CacheHits );
			}
			else
			{
				chain = LodBuilder.Build( indices, v => positions[v - s.VertexBase - (int)min], s.VertexBase, Config );
				if ( key is not null ) WriteCached( key, chain );
			}

			Interlocked.Add( ref BuildTicks, Stopwatch.GetTimestamp() - started );
			entry.Chain = chain;
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"LODs: building a range failed: {e.Message}" );
			entry.Chain = null;
		}
		finally
		{
			workers.Release();
			built.Enqueue( entry );
		}
	}

	/// <summary>
	/// A draw record over <paramref name="range"/> (from <see cref="GpuDrivenTables.Draw"/>, under its lock): given the range's
	/// levels now if they're published, else when they are. Draw records of levels themselves keep none.
	/// </summary>
	public void RegisterDraw( uint draw, in Range range )
	{
		Entry entry;
		lock ( gate )
		{
			if ( levelRanges.Contains( range ) ) return;
			if ( !entries.TryGetValue( range, out entry ) )
			{
				if ( !waitingDraws.TryGetValue( range, out var waiting ) ) waitingDraws[range] = waiting = new();
				waiting.Add( draw );
				return;
			}

			if ( entry.State == Phase.None ) return;
			entry.Draws.Add( draw );
			if ( entry.State != Phase.Published ) return;
		}

		records.Enqueue( (draw, MakeRecord( draw, entry )) );
	}

	/// <summary>A draw record's LOD record: a draw record per level, with the base's geometry, materials and flags.</summary>
	uint[] MakeRecord( uint draw, Entry entry )
	{
		var (geometry, vertex, pixel, flags, baseVertex) = tables.DrawFields( draw );
		var record = new uint[RecordBytes / 4];
		var placed = entry.Placed;
		record[0] = (uint)placed.Length;
		for ( int k = 0; k < placed.Length; k++ )
		{
			record[2 + k] = BitConverter.SingleToUInt32Bits( entry.Chain.Errors[k] );
			var at = 8 + k * 4;
			record[at] = placed[k].First;
			record[at + 1] = placed[k].Count;
			record[at + 2] = tables.Draw( geometry, vertex, pixel, flags, placed[k].First, placed[k].Count, baseVertex, entry.Range.PageSlot );
		}

		return record;
	}

	/// <summary>
	/// At a frame's start, on the thread that records it: place and upload built chains whose ranges a colour pass draws,
	/// within the frame's budget, and write the LOD records of their draws and of draws made since, in a list of its own
	/// submitted ahead of the frame's.
	/// </summary>
	public void BeginFrame()
	{
		frame++;
		ReadCounters();

		List<Entry> publish = null;
		long budget = 0;
		lock ( gate )
		{
			while ( built.TryDequeue( out var entry ) )
			{
				Built++;
				if ( entry.Chain is null )
				{
					Simplest++;
					entry.State = Phase.None;
					entry.Source = null;
					continue;
				}

				entry.State = Phase.Built;
				ready.Add( entry );
			}

			for ( int i = 0; i < ready.Count && budget < FrameUploadBytes; i++ )
			{
				var entry = ready[i];
				if ( !entry.Color ) continue;
				(publish ??= new()).Add( entry );
				foreach ( var level in entry.Chain.Levels ) budget += level.Length * 4L;
				ready.RemoveAt( i-- );
			}
		}

		int[] clears;
		lock ( sliceOwners )
		{
			clears = sliceClears.ToArray();
			sliceClears.Clear();
		}

		if ( publish is null && records.IsEmpty && cleared && !CountChanges && clears.Length == 0 ) return;

		using var zone = Zones.LodUpload.Start();
		var list = gpu.BeginCommandList( QueueType.Graphics, "LOD uploads" );
		list.Barrier( PipelineStage.AllCommands, MemoryAccess.IndexRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite | MemoryAccess.TransferRead );
		if ( !cleared )
		{
			list.FillBuffer( table, 0 );
			list.FillBuffer( state, 0 );
			list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite | MemoryAccess.TransferRead );
			cleared = true;
		}

		if ( CountChanges ) CopyCounters( list );

		// Slices views took last frame, ready for this one's
		foreach ( var slice in clears ) list.FillBuffer( state, 0, StateHeaderBytes + (long)slice * StateSliceBytes, StateSliceBytes );
		lock ( sliceOwners )
		{
			foreach ( var slice in clears ) sliceReady[slice] = true;
		}

		// Each chain's levels in its range's page, as many as there's room for, coarsest last
		List<(Range Range, Source Source)> meshletRequests = null;
		foreach ( var entry in publish ?? [] )
		{
			var s = entry.Source;
			var placed = new List<(uint, uint)>();
			for ( int k = 0; k < entry.Chain.Levels.Length; k++ )
			{
				var level = entry.Chain.Levels[k];
				var size = s.Format == IndexFormat.UInt32 ? 4 : 2;
				if ( !tables.TryAddLevelIndices( entry.Range.PageSlot, s.Format, level.Length * size, out var page, out var first ) )
				{
					Interlocked.Add( ref NoRoom, entry.Chain.Levels.Length - k );
					break;
				}

				if ( s.Format == IndexFormat.UInt32 ) list.UpdateBuffer<uint>( page, level, first * 4L );
				else
				{
					var narrow = new ushort[level.Length];
					for ( int i = 0; i < narrow.Length; i++ ) narrow[i] = (ushort)level[i];
					list.UpdateBuffer<ushort>( page, narrow, first * 2L );
				}

				placed.Add( (first, (uint)level.Length) );
				var levelRange = new Range( entry.Range.PageSlot, first, (uint)level.Length, entry.Range.BaseVertex );
				var bytes = new byte[level.Length * 4];
				MemoryMarshal.AsBytes( level.AsSpan() ).CopyTo( bytes );
				(meshletRequests ??= new()).Add( (levelRange, s with { Indices = new MeshIndexBuffer( level.Length, 4, bytes ), IndexBase = 0 }) );
				LevelTriangles += level.Length / 3;
			}

			uint[] draws;
			lock ( gate )
			{
				entry.Placed = placed.ToArray();
				foreach ( var (first, count) in entry.Placed ) levelRanges.Add( new Range( entry.Range.PageSlot, first, count, entry.Range.BaseVertex ) );
				entry.State = entry.Placed.Length > 0 ? Phase.Published : Phase.None;
				draws = entry.State == Phase.Published ? entry.Draws.ToArray() : [];

				// Its indices are in the page now: records need only the errors
				entry.Source = null;
				entry.Chain = new LodChain { Levels = [], Errors = entry.Chain.Errors };
				if ( entry.State == Phase.Published )
				{
					Published++;
					Levels += entry.Placed.Length;
					BaseTriangles += entry.Range.IndexCount / 3;
				}
				else Dropped++;
			}

			// Outside the lock: making the levels' draw records takes the draw table's, under which draws register here
			foreach ( var draw in draws ) records.Enqueue( (draw, MakeRecord( draw, entry )) );
		}

		// The LOD records, in a table grown to the highest draw record they're for
		if ( !records.IsEmpty )
		{
			var pending = new List<(uint Draw, uint[] Record)>();
			uint highest = 0;
			while ( records.TryDequeue( out var r ) )
			{
				pending.Add( r );
				highest = Math.Max( highest, r.Draw );
			}

			if ( highest >= tableRecords ) GrowTable( list, highest + 1 );
			foreach ( var (draw, record) in pending ) list.UpdateBuffer<uint>( table, record, draw * (long)RecordBytes );
		}

		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.IndexRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		gpu.Submit( list );

		// The levels' meshlets, built from their own indices once they're in their pages
		var store = Meshlets;
		if ( store is not null && meshletRequests is not null )
		{
			foreach ( var (range, source) in meshletRequests ) store.Request( range, source );
		}
	}

	/// <summary>A table covering at least <paramref name="needed"/> draw records: the old one copied in, the rest zero.</summary>
	void GrowTable( CommandList list, long needed )
	{
		var size = Math.Max( tableRecords * 2, 1 << 12 );
		while ( size < needed ) size *= 2;
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size * RecordBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | BufferUsage.CopySource, Memory = MemoryLocation.Device, Name = "Borealis LOD table" } );
		if ( table.IsValid && list is not null )
		{
			list.CopyBuffer( table, 0, buffer, 0, tableRecords * RecordBytes );
			list.FillBuffer( buffer, 0, tableRecords * RecordBytes, (size - tableRecords) * RecordBytes );
			list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite | MemoryAccess.TransferRead );
			retired.Add( table );
		}

		table = buffer;
		Volatile.Write( ref tableSlot, gpu.BindlessIndex( buffer ) );
		Volatile.Write( ref tableRecords, size );
	}

	//
	// Level changes, counted by the culls in the views that keep levels (borealislab's shimmer check)
	//

	/// <summary>Copy the culls' counters of level changes back each frame, for <see cref="Changes"/>.</summary>
	public bool CountChanges { get; set; }

	/// <summary>
	/// Level changes the culls have made in views that keep levels, as of a few frames ago: every change, those against the
	/// fragment's last change's direction, to coarser and to finer levels.
	/// </summary>
	public (long Changes, long Reversals, long Coarser, long Finer) Changes { get; private set; }

	void CopyCounters( CommandList list )
	{
		var slot = (int)(frame % CounterRing);
		if ( !counterRing[slot].IsValid ) counterRing[slot] = gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.CopyDestination, Memory = MemoryLocation.Readback, Name = "Borealis LOD counters" } );
		list.CopyBuffer( state, 0, counterRing[slot], 0, 16 );
	}

	void ReadCounters()
	{
		if ( !CountChanges ) return;

		// The copy made CounterRing frames ago, which the device has finished with
		var slot = (int)(frame % CounterRing);
		if ( !counterRing[slot].IsValid ) return;
		var words = MemoryMarshal.Cast<byte, uint>( gpu.Map( counterRing[slot] )[..16] );
		Changes = (words[0], words[1], words[2], words[3]);
	}

	//
	// The disk cache
	//

	string Key( uint[] indices, V3[] positions )
	{
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		Span<int> head = [(int)CacheVersion, Config.MaxLevels, BitConverter.SingleToInt32Bits( Config.Ratio ), BitConverter.SingleToInt32Bits( Config.KeepRatio ), Config.MinTriangles, Config.MinRangeTriangles, indices.Length, positions.Length];
		hash.AppendData( MemoryMarshal.AsBytes( head ) );
		hash.AppendData( MemoryMarshal.AsBytes( indices.AsSpan() ) );
		hash.AppendData( MemoryMarshal.AsBytes( positions.AsSpan() ) );
		return Convert.ToHexString( hash.GetHashAndReset() );
	}

	string CachePath( string key ) => System.IO.Path.Combine( CacheFolder, key[..2], key + ".bin" );

	/// <summary>A cached chain; null with <paramref name="empty"/> set for a range cached as having none.</summary>
	LodChain ReadCached( string key, out bool empty )
	{
		empty = false;
		try
		{
			var path = CachePath( key );
			if ( !System.IO.File.Exists( path ) ) return null;
			var bytes = System.IO.File.ReadAllBytes( path );
			if ( bytes.Length < 12 ) return null;
			var head = MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( 0, 12 ) );
			if ( head[0] != CacheMagic || head[1] != CacheVersion || head[2] > MaxLevels ) return null;
			var levels = (int)head[2];
			if ( levels == 0 )
			{
				empty = bytes.Length == 12;
				return null;
			}

			var table = 12 + levels * 8;
			if ( bytes.Length < table ) return null;
			var counts = MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( 12, levels * 4 ) );
			var errors = MemoryMarshal.Cast<byte, float>( bytes.AsSpan( 12 + levels * 4, levels * 4 ) ).ToArray();
			long total = 0;
			foreach ( var count in counts ) total += count;
			if ( bytes.Length != table + total * 4 ) return null;

			var chain = new uint[levels][];
			var at = table;
			for ( int k = 0; k < levels; k++ )
			{
				chain[k] = MemoryMarshal.Cast<byte, uint>( bytes.AsSpan( at, (int)counts[k] * 4 ) ).ToArray();
				at += (int)counts[k] * 4;
			}

			return new LodChain { Levels = chain, Errors = errors };
		}
		catch ( System.IO.IOException )
		{
			return null;
		}
	}

	void WriteCached( string key, LodChain chain )
	{
		try
		{
			var path = CachePath( key );
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) );
			var temp = path + "." + Environment.CurrentManagedThreadId + ".tmp";
			using ( var file = System.IO.File.Create( temp ) )
			{
				var levels = chain?.Levels.Length ?? 0;
				Span<uint> head = [CacheMagic, CacheVersion, (uint)levels];
				file.Write( MemoryMarshal.AsBytes( head ) );
				for ( int k = 0; k < levels; k++ ) file.Write( BitConverter.GetBytes( (uint)chain.Levels[k].Length ) );
				for ( int k = 0; k < levels; k++ ) file.Write( BitConverter.GetBytes( chain.Errors[k] ) );
				for ( int k = 0; k < levels; k++ ) file.Write( MemoryMarshal.AsBytes( chain.Levels[k].AsSpan() ) );
			}

			System.IO.File.Move( temp, path, overwrite: true );
		}
		catch ( Exception e ) when ( e is System.IO.IOException or UnauthorizedAccessException )
		{
			// A cache that can't be written builds again next time
		}
	}

	/// <summary>Device memory it holds: the LOD table and the levels views keep.</summary>
	public long Bytes => tableRecords * RecordBytes + StateHeaderBytes + (long)StateSlices * StateSliceBytes;

	public void Dispose()
	{
		if ( table.IsValid ) gpu.Destroy( table );
		foreach ( var old in retired ) gpu.Destroy( old );
		retired.Clear();
		if ( state.IsValid ) gpu.Destroy( state );
		foreach ( var buffer in counterRing )
		{
			if ( buffer.IsValid ) gpu.Destroy( buffer );
		}
	}
}
