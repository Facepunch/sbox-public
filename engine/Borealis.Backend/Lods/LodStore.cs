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
/// Generated levels of GPU-driven aggregate ranges (docs/reference/borealis-gpu-driven.md, "Generated LODs"). A fragment's
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
	public const int RecordBytes = LodRecord.Size;

	/// <summary>Bytes before the levels kept per view: counters of level changes (changes, reversals, coarser, finer).</summary>
	public const int StateHeaderBytes = 64;

	/// <summary>Views that keep a level per fragment, with hysteresis; the rest pick theirs afresh each frame.</summary>
	public const int StateSlices = 8;

	/// <summary>Bytes of a view's levels: a byte per aggregate visibility fragment (level, last direction, changed before).</summary>
	public const int StateSliceBytes = GpuDrivenTables.FrameVisibilityFragmentBase;

	const uint CacheMagic = 0x53444F4C, CacheVersion = 5;
	const int CounterRing = 4;

	readonly Device gpu;
	readonly GpuDrivenTables tables;

	public LodConfig Config { get; }

	/// <summary>Where level ranges' meshlets are asked for, or null.</summary>
	public MeshletStore Meshlets { get; set; }

	/// <summary>Bytes a frame uploads at most (level indices); the rest waits for the next frame.</summary>
	public long FrameUploadBytes { get; set; } = 16 << 20;

	/// <summary>Whether a loading screen hides the frames (<see cref="BackendDevice.Loading"/>): a frame uploads <see cref="LoadingUploadBytes"/>.</summary>
	public bool Loading { get; set; }

	/// <summary>Bytes a frame behind a loading screen uploads at most.</summary>
	public long LoadingUploadBytes { get; set; } = 128 << 20;

	long UploadBudget => Loading ? Math.Max( LoadingUploadBytes, FrameUploadBytes ) : FrameUploadBytes;

	enum Phase { Building, Built, Published, None }

	sealed class Entry
	{
		public Range Range;
		public Source Source;
		public Phase State;
		public LodChain Chain;
		public bool Color;
		public Label Label;
		public (uint First, uint Count)[] Placed;
		public readonly List<uint> Draws = new();

		/// <summary>
		/// Its levels in the range's index size one after another (at <see cref="LevelsAt"/>), written on the build worker
		/// (<see cref="Stage"/>) for the upload to copy, and each level's indices as the meshlet request wants them.
		/// </summary>
		public BufferHandle Staging;
		public long[] LevelsAt;
		public byte[][] LevelBytes;

		/// <summary>Cooked levels' own index buffers, with their meshlets, for the levels' meshlet requests; null when made here.</summary>
		public Borealis.Source2.Models.MeshIndexBuffer[] LevelBuffers;
	}

	/// <summary>
	/// Whether a built chain a colour pass draws is written on its build worker into a staging buffer of its own (on), so
	/// the upload on the frame's thread only records copies; off writes it there, narrowing 16-bit indices. For comparisons.
	/// </summary>
	public static bool StageOnWorkers { get; set; } = true;

	/// <summary>On the build worker: a chain's levels in the range's index size, in a staging buffer, and their bytes for meshlets.</summary>
	void Stage( Entry entry )
	{
		var wide = entry.Source.Format == IndexFormat.UInt32;
		var levels = entry.Chain.Levels;
		var at = new long[levels.Length];
		long size = 0;
		for ( int k = 0; k < levels.Length; k++ )
		{
			at[k] = size;
			size += (levels[k].Length * (wide ? 4L : 2L) + 3) & ~3L;
		}

		var staging = gpu.CreateBuffer( new BufferInfo { Size = Math.Max( size, 4 ), Usage = BufferUsage.CopySource, Memory = MemoryLocation.Upload, Name = "Borealis LOD staging" } );
		var mapped = gpu.Map( staging );
		// Cooked levels' meshlet requests take their own buffers
		var bytes = entry.LevelBuffers is null ? new byte[levels.Length][] : null;
		for ( int k = 0; k < levels.Length; k++ )
		{
			var level = levels[k];
			if ( wide ) MemoryMarshal.AsBytes( level.AsSpan() ).CopyTo( mapped[(int)at[k]..] );
			else
			{
				var narrow = MemoryMarshal.Cast<byte, ushort>( mapped.Slice( (int)at[k], level.Length * 2 ) );
				for ( int i = 0; i < level.Length; i++ ) narrow[i] = (ushort)level[i];
			}

			if ( bytes is not null ) bytes[k] = MemoryMarshal.AsBytes( level.AsSpan() ).ToArray();
		}

		entry.LevelsAt = at;
		entry.LevelBytes = bytes;
		entry.Staging = staging;
	}

	// A LOD record of no levels, written over a forgotten range's draws' (read only)
	readonly uint[] noLevels = new uint[RecordBytes / 4];

	/// <summary>
	/// Forget the ranges in the index page at <paramref name="slot"/>, whose meshes have all gone (the page is being freed,
	/// and its slot may name another page later), with their levels; false while one is being built or waits to be placed,
	/// to be asked again next frame. Their draws' LOD records are cleared at the next frame's start, before any record made
	/// over one of their slots (<see cref="GpuDrivenTables"/> reuses them) is written: it reads no levels until its own
	/// range's are published, rather than the freed range's levels' draw records.
	/// </summary>
	public bool TryForgetPage( int slot )
	{
		lock ( gate )
		{
			foreach ( var (range, entry) in entries )
			{
				if ( range.PageSlot == slot && entry.State is Phase.Building or Phase.Built ) return false;
			}

			foreach ( var range in entries.Keys.Where( r => r.PageSlot == slot ).ToList() )
			{
				if ( !entries.Remove( range, out var entry ) || entry.State != Phase.Published ) continue;
				foreach ( var draw in entry.Draws ) records.Enqueue( (draw, noLevels) );
				mayShrink = true;
			}

			foreach ( var range in waitingDraws.Keys.Where( r => r.PageSlot == slot ).ToList() ) waitingDraws.Remove( range );
			levelRanges.RemoveWhere( r => r.PageSlot == slot );
			return true;
		}
	}

	readonly object gate = new();
	readonly Dictionary<Range, Entry> entries = new();
	readonly Dictionary<Range, List<uint>> waitingDraws = new();
	readonly HashSet<Range> levelRanges = new();
	readonly ConcurrentQueue<Entry> built = new();
	readonly List<Entry> ready = new();
	readonly ConcurrentQueue<(uint Draw, uint[] Record)> records = new();

	// Where ranges build: at most the store's threads at once, the rest queued here rather than each holding a thread pool
	// thread while it waits (a map's thousands of ranges starved the pool, so the load's own work on it waited seconds)
	readonly TaskScheduler workers;
	readonly Action<object> build;

	BufferHandle table;
	int tableSlot = -1;
	long tableRecords;
	// Tables it grew out of, with the frame they were retired in: freed once that frame's lists have been submitted
	readonly List<(BufferHandle Buffer, long Frame)> retired = new();
	volatile bool cleared;

	// Builds in flight, so Dispose waits for them before destroying what they write to: a build making its staging buffer
	// after the device went crashes the process, as the meshlet store's did. Monitor.Wait gives the lock up while it waits
	readonly object flight = new();
	int building;
	volatile bool disposed;

	readonly BufferHandle state;
	readonly WeakReference<object>[] sliceOwners = new WeakReference<object>[StateSlices];
	readonly bool[] sliceReady = new bool[StateSlices];
	readonly List<int> sliceClears = new();
	readonly BufferHandle[] counterRing = new BufferHandle[CounterRing];
	long frame;

	/// <summary>Ranges asked for, built, published (with levels placed), dropped (no room for any) and with no level worth making; levels placed and their triangles; the published ranges' own triangles; build time on the workers; ranges read from the cache; levels left out for want of room.</summary>
	public long Requested, Built, Published, Dropped, Simplest, Levels, LevelTriangles, BaseTriangles, BuildTicks, CacheHits, NoRoom;

	/// <summary>Ranges whose levels were cooked into their model, taken as they are.</summary>
	public long CookedHits;

	/// <summary>Ranges of index buffers with cooked meshlets whose levels were made all the same: none cooked, another config, or a range not cooked.</summary>
	public long CookedMisses;

	public LodStore( Device gpu, GpuDrivenTables tables, LodConfig config, int threads, string cacheFolder = null )
	{
		this.gpu = gpu;
		this.tables = tables;
		Config = config;
		CacheFolder = cacheFolder;
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

	/// <summary>The vertex layouts of the ranges built, and how many of each: which texture coordinates were weighed.</summary>
	public readonly ConcurrentDictionary<string, int> FieldsSeen = new();

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
	/// What a range draws and where, for reports (<c>borealislab -lod-ranges</c>): its material and its fragment's world
	/// bounds. Kept only while <see cref="Labelled"/>.
	/// </summary>
	public readonly record struct Label( string Material, V3 Center, V3 Extents );

	/// <summary>Whether requests keep their <see cref="Label"/>, for <see cref="Ranges"/>. Set before the first draws.</summary>
	public bool Labelled { get; set; }

	/// <summary>Every range asked for: its label, triangles, whether it simplified no further, and its levels' triangles and errors once built.</summary>
	public List<(Label Label, int Triangles, bool Simplest, int[] Levels, float[] Errors)> Ranges()
	{
		lock ( gate )
		{
			var ranges = new List<(Label, int, bool, int[], float[])>( entries.Count );
			foreach ( var (range, entry) in entries )
			{
				// A published range keeps only its levels' errors and where their indices went
				var chain = entry.Chain;
				var levels = entry.Placed is { } placed ? placed.Select( p => (int)(p.Count / 3) ).ToArray() : chain?.Levels.Select( l => l.Length / 3 ).ToArray() ?? [];
				ranges.Add( (entry.Label, (int)(range.IndexCount / 3), entry.State == Phase.None, levels, chain?.Errors ?? []) );
			}

			return ranges;
		}
	}

	int publishVersion;

	/// <summary>Counts ranges settled (published, or with no levels): what waits for some asks again once it moves.</summary>
	public int PublishVersion => Volatile.Read( ref publishVersion );

	/// <summary>
	/// A range's levels once settled: true with each level's first index and count in the page and its error when
	/// published, true with none when it has none (no level worth making, no room for any, or too few triangles to ask for);
	/// false until it's asked for (<see cref="Request"/>), while it's being built and while it waits to be published.
	/// </summary>
	public bool TryGetLevels( in Range range, out (uint First, uint Count)[] placed, out float[] errors )
	{
		placed = [];
		errors = [];
		lock ( gate )
		{
			if ( !entries.TryGetValue( range, out var entry ) ) return range.IndexCount / 3 < Config.MinRangeTriangles;
			if ( entry.State is Phase.Building or Phase.Built ) return false;
			if ( entry.State == Phase.Published )
			{
				placed = entry.Placed;
				errors = entry.Chain.Errors;
			}

			return true;
		}
	}

	/// <summary>
	/// Ask for a range's levels: built once, on a worker. They're published once a colour pass whose prepass draws the same
	/// level draws the range too (<paramref name="color"/>): a GPU-driven one, or a view's own draws, which pick one level per
	/// object for every pass. A range drawn whole in colour must keep its full triangles in the prepass, whose depth the colour
	/// pass tests equal against. <paramref name="label"/> is kept while <see cref="Labelled"/>.
	/// </summary>
	public void Request( in Range range, Source source, bool color, Label label = default )
	{
		if ( range.IndexCount / 3 < Config.MinRangeTriangles ) return;
		lock ( gate )
		{
			if ( entries.TryGetValue( range, out var known ) )
			{
				known.Color |= color;
				return;
			}

			var entry = new Entry { Range = range, Source = source, State = Phase.Building, Color = color, Label = Labelled ? label : default };
			entries[range] = entry;
			if ( waitingDraws.Remove( range, out var draws ) ) entry.Draws.AddRange( draws );
			Requested++;
			lock ( flight ) building++;
			Task.Factory.StartNew( build, entry, CancellationToken.None, TaskCreationOptions.DenyChildAttach, workers );
		}
	}

	/// <summary>
	/// What a range's levels are made from, its vertices' from the lowest index it uses, in pooled scratch (a map's ranges made
	/// hundreds of MiB of garbage a load; each array holds <see cref="VertexCount"/> values at its start, pooled arrays are
	/// longer): positions; texture coordinates (2D float sets), vertex colours and blend weights (four bytes each), which count
	/// in the error; and normals where the format is one read here (a compressed tangent frame's octahedral normal, or floats).
	/// Disposing returns them.
	/// </summary>
	internal readonly struct Attributes : IDisposable
	{
		public readonly V3[] Positions, Normals;
		public readonly List<System.Numerics.Vector2[]> Sets;
		public readonly List<System.Numerics.Vector4[]> Colors;
		public readonly int VertexCount, Offset;

		public Attributes( MeshVertexBuffer vertices, Borealis.Source2.Models.VertexField position, int vertexBase, uint min, int vertexCount )
		{
			VertexCount = vertexCount;
			Offset = vertexBase + (int)min;
			Sets = new( 4 );
			Colors = new( 4 );
			Normals = null;
			Positions = System.Buffers.ArrayPool<V3>.Shared.Rent( vertexCount );
			for ( int v = 0; v < vertexCount; v++ )
			{
				var p = vertices.Read( position, Offset + v );
				Positions[v] = new V3( p.X, p.Y, p.Z );
			}

			foreach ( var field in vertices.Fields )
			{
				if ( Sets.Count == 4 || !string.Equals( field.Semantic, "TEXCOORD", StringComparison.OrdinalIgnoreCase ) || field.PerInstance ) continue;
				if ( field.Format is not (ColorFormat.R32G32_FLOAT or ColorFormat.R16G16_FLOAT or ColorFormat.R32G32B32_FLOAT or ColorFormat.R32G32B32A32_FLOAT or ColorFormat.R16G16B16A16_FLOAT or ColorFormat.R16G16_UNORM) ) continue;
				var set = System.Buffers.ArrayPool<System.Numerics.Vector2>.Shared.Rent( vertexCount );
				Sets.Add( set );
				for ( int v = 0; v < vertexCount; v++ ) set[v] = Coordinate( vertices, field, Offset + v );
			}

			foreach ( var field in vertices.Fields )
			{
				if ( Colors.Count == 4 || field.PerInstance || field.Format != ColorFormat.R8G8B8A8_UNORM ) continue;
				if ( !string.Equals( field.Semantic, "TEXCOORD", StringComparison.OrdinalIgnoreCase ) && !string.Equals( field.Semantic, "COLOR", StringComparison.OrdinalIgnoreCase ) ) continue;
				var set = System.Buffers.ArrayPool<System.Numerics.Vector4>.Shared.Rent( vertexCount );
				Colors.Add( set );
				for ( int v = 0; v < vertexCount; v++ )
				{
					var c = vertices.Read( field, Offset + v );
					set[v] = new System.Numerics.Vector4( c.X, c.Y, c.Z, c.W );
				}
			}

			if ( vertices.Find( "NORMAL" ) is { } normalField && normalField.Format is ColorFormat.R10G10B10A2_UNORM or ColorFormat.R32G32B32_FLOAT or ColorFormat.R32G32B32A32_FLOAT )
			{
				Normals = System.Buffers.ArrayPool<V3>.Shared.Rent( vertexCount );
				for ( int v = 0; v < vertexCount; v++ ) Normals[v] = Normal( vertices, normalField, Offset + v );
			}
		}

		/// <summary>The range's levels from these, or null when it has none.</summary>
		public LodChain Make( ReadOnlySpan<uint> indices, int vertexBase, LodConfig config )
		{
			var (positions, normals, offset) = (Positions, Normals, Offset);
			var readers = Sets.Select( set => (Func<int, System.Numerics.Vector2>)(v => set[v - offset]) ).ToArray();
			var colorReaders = Colors.Select( set => (Func<int, System.Numerics.Vector4>)(v => set[v - offset]) ).ToArray();
			return LodBuilder.Build( indices, v => positions[v - offset], vertexBase, config, readers, normals is null ? null : v => normals[v - offset], colorReaders );
		}

		public void Dispose()
		{
			if ( Positions is not null ) System.Buffers.ArrayPool<V3>.Shared.Return( Positions );
			if ( Normals is not null ) System.Buffers.ArrayPool<V3>.Shared.Return( Normals );
			foreach ( var set in Sets ?? [] ) System.Buffers.ArrayPool<System.Numerics.Vector2>.Shared.Return( set );
			foreach ( var set in Colors ?? [] ) System.Buffers.ArrayPool<System.Numerics.Vector4>.Shared.Return( set );
		}
	}

	/// <summary>
	/// The cut <see cref="Borealis.Source2.Models.MeshletCooker"/> cooks levels with: what a load makes of a range, as the
	/// resource compiler makes it (<c>compilemeshlets.cpp</c>); empty when nothing simplifies.
	/// </summary>
	public static Borealis.Source2.Models.LevelCut Cut( LodConfig config ) => ( indices, vertices, position, vertexBase ) =>
	{
		uint min = uint.MaxValue, max = 0;
		foreach ( var index in indices )
		{
			min = Math.Min( min, index );
			max = Math.Max( max, index );
		}

		using var attributes = new Attributes( vertices, position, vertexBase, min, indices.Length == 0 ? 0 : (int)(max - min + 1) );
		return attributes.Make( indices, vertexBase, config ) is { } chain ? (chain.Levels, chain.Errors) : ([], []);
	};

	/// <summary>The config's identity as cooked levels carry it.</summary>
	public static CookedLodConfig Cooked( LodConfig config ) => new( config.MaxLevels, config.Ratio, config.KeepRatio, config.MinTriangles, config.MinRangeTriangles, config.NormalWeight, config.ColorWeight );

	/// <summary>A range's cooked levels (<see cref="CookedMeshlets.FindLevels"/>), when they were made with this store's config; null to make them.</summary>
	CookedLevels Cooked( Source s, int count )
	{
		if ( !MeshletStore.UseCooked || s.Indices.Meshlets is not { } cooked ) return null;
		var levels = cooked.Lods == Cooked( Config ) ? cooked.FindLevels( s.IndexBase, count, s.Vertices, s.VertexBase ) : null;
		if ( levels is null ) Interlocked.Increment( ref CookedMisses );
		return levels;
	}

	void BuildOn( Entry entry )
	{
		try
		{
			using var zone = Zones.LodBuild.Start();
			var started = Stopwatch.GetTimestamp();
			var s = entry.Source;
			var count = (int)entry.Range.IndexCount;
			LodChain chain;

			// Cooked into the model with this config: its levels taken as they are, each with its meshlets, nothing read
			if ( Cooked( s, count ) is { } cooked )
			{
				if ( cooked.Errors.Count == 0 ) chain = null;
				else
				{
					var levels = new uint[cooked.Buffers.Count][];
					for ( int k = 0; k < levels.Length; k++ ) levels[k] = MemoryMarshal.Cast<byte, uint>( cooked.Buffers[k].Data ).ToArray();
					chain = new LodChain { Levels = levels, Errors = [.. cooked.Errors] };
					entry.LevelBuffers = [.. cooked.Buffers];
				}

				Interlocked.Increment( ref CookedHits );
			}
			else
			{
				// The range's indices and its vertices' attributes (Attributes), in pooled scratch: with the config, the cache's key
				var indexArray = System.Buffers.ArrayPool<uint>.Shared.Rent( count );
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

					using var attributes = new Attributes( s.Vertices, s.Position, s.VertexBase, min, count == 0 ? 0 : (int)(max - min + 1) );
					FieldsSeen.AddOrUpdate( string.Join( " ", s.Vertices.Fields.Select( f => f.ToString() ) ), 1, ( _, n ) => n + 1 );
					var key = CacheFolder is null ? null : Key( indices, attributes.Positions.AsSpan( 0, attributes.VertexCount ), attributes.Sets, attributes.Normals, attributes.Colors, attributes.VertexCount );
					var empty = false;
					var cached = key is null ? null : ReadCached( key, out empty );
					if ( cached is not null || empty )
					{
						chain = cached;
						Interlocked.Increment( ref CacheHits );
					}
					else
					{
						chain = attributes.Make( indices, s.VertexBase, Config );
						if ( key is not null ) WriteCached( key, chain );
					}
				}
				finally
				{
					System.Buffers.ArrayPool<uint>.Shared.Return( indexArray );
				}
			}

			Interlocked.Add( ref BuildTicks, Stopwatch.GetTimestamp() - started );
			entry.Chain = chain;

			// Staged only for a range a colour pass draws, which uploads soon; others wait without holding upload memory
			if ( StageOnWorkers && chain is { Levels.Length: > 0 } && Volatile.Read( ref entry.Color ) ) Stage( entry );
		}
		catch ( Exception e )
		{
			Console.Error.WriteLine( $"LODs: building a range failed: {e.Message}" );
			entry.Chain = null;
		}
		finally
		{
			built.Enqueue( entry );
		}
	}

	/// <summary>
	/// A draw record over <paramref name="range"/> (from <see cref="GpuDrivenTables.Draw"/>, by the thread that made it): given the range's
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
		ref var lod = ref MemoryMarshal.AsRef<LodRecord>( MemoryMarshal.AsBytes( record.AsSpan() ) );
		var placed = entry.Placed;
		lod.Levels = (uint)placed.Length;
		var errors = lod.Errors;
		var levels = lod.Level;
		for ( int k = 0; k < placed.Length; k++ )
		{
			errors[k] = entry.Chain.Errors[k];
			levels[k] = new LodLevel { FirstIndex = placed[k].First, IndexCount = placed[k].Count, Draw = tables.Draw( geometry, vertex, pixel, flags, placed[k].First, placed[k].Count, baseVertex, entry.Range.PageSlot ) };
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
		for ( int i = retired.Count - 1; i >= 0; i-- )
		{
			if ( frame - retired[i].Frame < (long)GpuDrivenTables.RetiredFrames ) continue;
			gpu.Destroy( retired[i].Buffer );
			retired.RemoveAt( i );
		}

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
					publishVersion++;
					continue;
				}

				entry.State = Phase.Built;
				ready.Add( entry );
			}

			for ( int i = 0; i < ready.Count && budget < UploadBudget; i++ )
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

		var shrinkTo = ShrinkTo();
		if ( publish is null && records.IsEmpty && cleared && !CountChanges && clears.Length == 0 && shrinkTo == 0 ) return;

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
		if ( shrinkTo > 0 ) ShrinkTable( list, shrinkTo );

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

				if ( entry.Staging.IsValid ) list.CopyBuffer( entry.Staging, entry.LevelsAt[k], page, first * (long)size, level.Length * (long)size );
				else if ( s.Format == IndexFormat.UInt32 ) list.UpdateBuffer<uint>( page, level, first * 4L );
				else
				{
					var narrow = new ushort[level.Length];
					for ( int i = 0; i < narrow.Length; i++ ) narrow[i] = (ushort)level[i];
					list.UpdateBuffer<ushort>( page, narrow, first * 2L );
				}

				placed.Add( (first, (uint)level.Length) );
				var levelRange = new Range( entry.Range.PageSlot, first, (uint)level.Length, entry.Range.BaseVertex );
				var levelIndices = entry.LevelBuffers?[k] ?? new MeshIndexBuffer( level.Length, 4, entry.LevelBytes?[k] ?? MemoryMarshal.AsBytes( level.AsSpan() ).ToArray() );
				(meshletRequests ??= new()).Add( (levelRange, s with { Indices = levelIndices, IndexBase = 0 }) );
				LevelTriangles += level.Length / 3;
			}

			// Freed once the GPU has run the copies
			if ( entry.Staging.IsValid ) gpu.Destroy( entry.Staging );
			entry.Staging = default;
			entry.LevelBytes = null;
			entry.LevelBuffers = null;

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
				publishVersion++;
			}

			// Outside the lock: making the levels' draw records registers them here
			foreach ( var draw in draws ) records.Enqueue( (draw, MakeRecord( draw, entry )) );
		}

		// The LOD records, in a table grown to the highest draw record they're for
		if ( !records.IsEmpty )
		{
			var pending = new List<(uint Draw, uint[] Record)>();
			uint highest = 0;
			while ( records.TryDequeue( out var r ) )
			{
				// A cleared record past a table that shrank: its draw record went, and the table reads none there
				if ( r.Record == noLevels && r.Draw >= tableRecords ) continue;
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

	// Set as ranges are forgotten, when the table may cover more draw records with levels than are left
	bool mayShrink;

	/// <summary>
	/// The size the LOD table shrinks to, or 0: after ranges were forgotten (a map unloaded), once it's four times or more
	/// what the draw records with levels need (the smallest power of two over the highest, at least the first table's), it's
	/// made again at that size. The culls read no levels for a draw record past the table's count, as for one without a
	/// record. Takes the gate; allocates nothing.
	/// </summary>
	long ShrinkTo()
	{
		if ( !mayShrink ) return 0;
		mayShrink = false;
		long highest = 0;
		lock ( gate )
		{
			foreach ( var entry in entries.Values )
			{
				if ( entry.State != Phase.Published ) continue;
				foreach ( var draw in entry.Draws ) highest = Math.Max( highest, draw + 1L );
			}
		}

		var needed = Math.Max( 1L << 12, (long)System.Numerics.BitOperations.RoundUpToPowerOf2( (ulong)Math.Max( highest, 1 ) ) );
		return tableRecords >= needed * 4 ? needed : 0;
	}

	/// <summary>
	/// A table of <paramref name="size"/> records with the old one's first ones copied in: every draw record with levels is
	/// below it, and one made later past it grows the table again. Its count is set before its slot, so a cull reading the
	/// slot after it reads the smaller count with either table.
	/// </summary>
	void ShrinkTable( CommandList list, long size )
	{
		var buffer = gpu.CreateBuffer( new BufferInfo { Size = size * RecordBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | BufferUsage.CopySource, Memory = MemoryLocation.Device, Name = "Borealis LOD table" } );
		list.CopyBuffer( table, 0, buffer, 0, size * RecordBytes );
		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite | MemoryAccess.TransferRead );
		retired.Add( (table, frame) );
		table = buffer;
		Volatile.Write( ref tableRecords, size );
		Volatile.Write( ref tableSlot, gpu.BindlessIndex( buffer ) );
		TableShrinks++;
	}

	/// <summary>Times the LOD table shrank (<see cref="ShrinkTo"/>), for tests and reports.</summary>
	public int TableShrinks { get; private set; }

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
			retired.Add( (table, frame) );
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

	/// <summary>A vertex's texture coordinate pair.</summary>
	static System.Numerics.Vector2 Coordinate( MeshVertexBuffer vertices, Borealis.Source2.Models.VertexField field, int vertex )
	{
		if ( field.Format == ColorFormat.R16G16_UNORM )
		{
			var at = vertices.Data.AsSpan( vertex * vertices.Stride + field.Offset );
			return new System.Numerics.Vector2( System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( at ), System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( at[2..] ) ) / 65535f;
		}

		var uv = vertices.Read( field, vertex );
		return new System.Numerics.Vector2( uv.X, uv.Y );
	}

	/// <summary>A vertex's normal: floats, or a compressed tangent frame's octahedral normal (<c>_DecompressNormal</c>).</summary>
	static V3 Normal( MeshVertexBuffer vertices, Borealis.Source2.Models.VertexField field, int vertex )
	{
		if ( field.Format != ColorFormat.R10G10B10A2_UNORM )
		{
			var n = vertices.Read( field, vertex );
			return new V3( n.X, n.Y, n.Z );
		}

		var packed = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( vertices.Data.AsSpan( vertex * vertices.Stride + field.Offset ) );
		var x = (packed & 1023) / 1023f * 2 - 1;
		var y = ((packed >> 10) & 1023) / 1023f * 2 - 1;
		var normal = new V3( x, y, 1 - MathF.Abs( x ) - MathF.Abs( y ) );
		var t = Math.Clamp( -normal.Z, 0, 1 );
		normal.X += normal.X >= 0 ? -t : t;
		normal.Y += normal.Y >= 0 ? -t : t;
		return normal.LengthSquared() > 0 ? V3.Normalize( normal ) : V3.UnitZ;
	}

	// Each set holds vertexCount values at its start (pooled arrays are longer)
	string Key( ReadOnlySpan<uint> indices, ReadOnlySpan<V3> positions, List<System.Numerics.Vector2[]> sets, V3[] normals, List<System.Numerics.Vector4[]> colors, int vertexCount )
	{
		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );
		Span<int> head = [(int)CacheVersion, Config.MaxLevels, BitConverter.SingleToInt32Bits( Config.Ratio ), BitConverter.SingleToInt32Bits( Config.KeepRatio ), Config.MinTriangles, Config.MinRangeTriangles, BitConverter.SingleToInt32Bits( Config.NormalWeight ), BitConverter.SingleToInt32Bits( Config.ColorWeight ), indices.Length, positions.Length];
		hash.AppendData( MemoryMarshal.AsBytes( head ) );
		hash.AppendData( MemoryMarshal.AsBytes( indices ) );
		hash.AppendData( MemoryMarshal.AsBytes( positions ) );
		foreach ( var set in sets ) hash.AppendData( MemoryMarshal.AsBytes( set.AsSpan( 0, vertexCount ) ) );
		if ( normals is not null ) hash.AppendData( MemoryMarshal.AsBytes( normals.AsSpan( 0, vertexCount ) ) );
		foreach ( var set in colors ) hash.AppendData( MemoryMarshal.AsBytes( set.AsSpan( 0, vertexCount ) ) );
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
			// Read into pooled scratch: only the chain's own arrays are new
			var buffer = PooledFile.Read( path, out var length );
			try
			{
				var bytes = buffer.AsSpan( 0, length );
				if ( bytes.Length < 12 ) return null;
				var head = MemoryMarshal.Cast<byte, uint>( bytes.Slice( 0, 12 ) );
				if ( head[0] != CacheMagic || head[1] != CacheVersion || head[2] > MaxLevels ) return null;
				var levels = (int)head[2];
				if ( levels == 0 )
				{
					empty = bytes.Length == 12;
					return null;
				}

				var table = 12 + levels * 8;
				if ( bytes.Length < table ) return null;
				var counts = MemoryMarshal.Cast<byte, uint>( bytes.Slice( 12, levels * 4 ) );
				var errors = MemoryMarshal.Cast<byte, float>( bytes.Slice( 12 + levels * 4, levels * 4 ) ).ToArray();
				long total = 0;
				foreach ( var count in counts ) total += count;
				if ( bytes.Length != table + total * 4 ) return null;

				var chain = new uint[levels][];
				var at = table;
				for ( int k = 0; k < levels; k++ )
				{
					chain[k] = MemoryMarshal.Cast<byte, uint>( bytes.Slice( at, (int)counts[k] * 4 ) ).ToArray();
					at += (int)counts[k] * 4;
				}

				return new LodChain { Levels = chain, Errors = errors };
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
		// Builds not started yet do nothing; running ones finish first (bounded, so a build stuck on the device can't hold up
		// the shutdown for good)
		disposed = true;
		lock ( flight )
		{
			var until = Environment.TickCount64 + 10_000;
			while ( building > 0 && Environment.TickCount64 < until ) Monitor.Wait( flight, 100 );
		}

		lock ( gate )
		{
			while ( built.TryDequeue( out var entry ) ) ready.Add( entry );
			foreach ( var entry in ready )
			{
				if ( entry.Staging.IsValid ) gpu.Destroy( entry.Staging );
			}
		}

		if ( table.IsValid ) gpu.Destroy( table );
		foreach ( var (old, _) in retired ) gpu.Destroy( old );
		retired.Clear();
		if ( state.IsValid ) gpu.Destroy( state );
		foreach ( var buffer in counterRing )
		{
			if ( buffer.IsValid ) gpu.Destroy( buffer );
		}
	}
}
