using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Borealis.Backend;

/// <summary>
/// A streamed texture's state: the mips it holds, those its draws ask for, and the materials that name it. Its
/// <see cref="BackendTexture"/> keeps its identity while its image is swapped for one of more or fewer mips.
/// </summary>
internal sealed class StreamedTexture
{
	public StreamedTexture( BackendTexture texture, string path, TextureFile file )
	{
		Texture = texture;
		Path = path;
		Mips = file.Mips;
		TailTop = file.TailTop;
		MaxSide = Math.Max( file.Width, file.Height );
		ResidentTop = file.TopMip;
		ResidentBytes = TextureStreamer.Bytes( file, file.TopMip );
		MipBytes = new long[file.Mips];
		for ( int mip = 0; mip < file.Mips; mip++ ) MipBytes[mip] = file.MipBytes( mip );
	}

	public BackendTexture Texture { get; }
	public string Path { get; }
	public int Mips { get; }
	public int TailTop { get; }
	public int MaxSide { get; }
	public long[] MipBytes { get; }

	/// <summary>The finest mip its image holds.</summary>
	public int ResidentTop { get; set; }

	/// <summary>Bytes of its image.</summary>
	public long ResidentBytes { get; set; }

	/// <summary>The finest mip asked for in this window and the last; <see cref="int.MaxValue"/> for none.</summary>
	public int Asked = int.MaxValue, AskedBefore = int.MaxValue;

	/// <summary>Kept at every mip whatever is asked: something that doesn't ask (<see cref="BackendDevice.LoadTexture"/>) uses it.</summary>
	public bool Pinned { get; set; }

	/// <summary>The streamer's frame it was last asked for in.</summary>
	public uint LastAsked { get; set; }

	/// <summary>When it was last asked for (or added), in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
	public long LastAskedTime { get; set; }

	/// <summary>A build in flight for it, or null.</summary>
	public TextureStreamer.Build Building { get; set; }

	/// <summary>
	/// The coarsest mip a build couldn't read its file from, or -1: builds from it or finer aren't tried again, so a file
	/// that can't be read isn't read every frame.
	/// </summary>
	public int Unreadable { get; set; } = -1;

	/// <summary>The finest mip a build may start from: what it wants, coarser than any it couldn't read.</summary>
	public int Reachable => Math.Max( Wanted, Unreadable + 1 );

	/// <summary>The finest mip it wants: what was asked for lately, or its tail.</summary>
	public int Wanted => Pinned ? 0 : Math.Min( Math.Min( Asked, AskedBefore ), TailTop );

	/// <summary>Bytes of an image of mips <paramref name="top"/> to the last.</summary>
	public long BytesFrom( int top )
	{
		long bytes = 0;
		for ( int mip = top; mip < Mips; mip++ ) bytes += MipBytes[mip];
		return bytes;
	}

	/// <summary>The coarsest mip a draw asking for <paramref name="size"/> texels needs (native's required texture size).</summary>
	public int TopFor( int size )
	{
		if ( size <= 0 ) return TailTop;
		var top = 0;
		while ( top < TailTop && (MaxSide >> (top + 1)) >= size ) top++;
		return top;
	}

	/// <summary>Note a material that names this texture, so it re-keys when the image is swapped.</summary>
	public void AddDependent( IStreamDependent dependent ) => Texture.AddDependent( dependent );

	/// <summary>Tell the materials that name it that its image changed.</summary>
	public void Swapped() => Texture.Swapped();
}

/// <summary>Something that names streamed textures in what it binds (a material), so a swapped image re-keys it.</summary>
internal interface IStreamDependent
{
	/// <summary>A texture it names has a new image: move its version on.</summary>
	void TexturesChanged();
}

/// <summary>
/// A material's asks: the largest texture size its draws asked for in a frame, and its streamed textures. Draws on any
/// thread ask; the streamer reads them at the start of the next frame.
/// </summary>
internal sealed class MaterialAsks
{
	long packed;
	StreamedTexture[] textures = [];

	/// <summary>The material's streamed textures.</summary>
	public StreamedTexture[] Textures
	{
		get => Volatile.Read( ref textures );
		set => Volatile.Write( ref textures, value ?? [] );
	}

	/// <summary>The size asked for in the last frame asked in.</summary>
	public int Size => (int)(uint)Volatile.Read( ref packed );

	/// <summary>Ask for <paramref name="size"/> texels this frame; the first ask of a frame queues it.</summary>
	public void Ask( TextureStreamer streamer, int size )
	{
		if ( Volatile.Read( ref textures ).Length == 0 ) return;

		var frame = streamer.Frame;
		while ( true )
		{
			var old = Volatile.Read( ref packed );
			var oldFrame = (uint)(old >> 32);
			var oldSize = (int)(uint)old;
			if ( oldFrame == frame && oldSize >= size ) return;

			var next = ((long)frame << 32) | (uint)Math.Max( size, oldFrame == frame ? oldSize : 0 );
			if ( Interlocked.CompareExchange( ref packed, next, old ) != old ) continue;
			if ( oldFrame != frame ) streamer.Queue( this );
			return;
		}
	}
}

/// <summary>
/// Texture streaming for the game window: material textures start as their tails (<see cref="TextureFile.Tail"/>) and get
/// the mips their draws ask for (<c>textureSize</c>, see <c>TextureSizes</c>) as new images built from the file on a
/// worker, uploaded a few each frame and swapped in whole. Textures grow while video memory has room and give mips back
/// when it runs short. Runs on the thread that begins frames. See docs/reference/borealis-gpu-path.md.
/// </summary>
internal sealed class TextureStreamer
{
	readonly BackendDevice device;
	readonly List<StreamedTexture> textures = new();
	readonly ConcurrentQueue<MaterialAsks> asked = new();
	readonly ConcurrentQueue<StreamedTexture> added = new();
	readonly List<ITextureSizeSource> sources = new();
	readonly Action<GpuMaterial, int> ask;
	readonly List<Build> builds = new();
	readonly List<StreamedTexture> candidates = new();
	uint frame = 1;
	long room;
	uint memoryFrame;
	long startedSinceMemory;

	public TextureStreamer( BackendDevice device )
	{
		this.device = device;
		ask = device.AskTextures;
	}

	/// <summary>Take sizes from <paramref name="source"/> at the start of every <see cref="Update"/>, as draws' asks.</summary>
	public void AddSource( ITextureSizeSource source )
	{
		lock ( sources )
		{
			if ( !sources.Contains( source ) ) sources.Add( source );
		}
	}

	/// <summary>The frame draws are asking in.</summary>
	public uint Frame => Volatile.Read( ref frame );

	/// <summary>Frames in each of the two windows asks are kept over.</summary>
	public int WindowFrames { get; set; } = 60;

	/// <summary>Builds in flight at once.</summary>
	public int MaxBuilds { get; set; } = 16;

	/// <summary>Bytes of mips uploaded each frame.</summary>
	public long UploadBytesPerFrame { get; set; } = 16 << 20;

	/// <summary>
	/// Whether a loading screen hides the frames (<see cref="BackendDevice.Loading"/>): builds go <see cref="LoadingMaxBuilds"/> at
	/// once and upload <see cref="LoadingUploadBytesPerFrame"/> a frame, so what the first frame after a load asks for is there.
	/// </summary>
	public bool Loading { get; set; }

	/// <summary>Builds in flight at once behind a loading screen.</summary>
	public int LoadingMaxBuilds { get; set; } = 64;

	/// <summary>Bytes of mips uploaded each frame behind a loading screen.</summary>
	public long LoadingUploadBytesPerFrame { get; set; } = 128 << 20;

	int BuildsAtOnce => Loading ? Math.Max( LoadingMaxBuilds, MaxBuilds ) : MaxBuilds;

	long UploadBytes => Loading ? Math.Max( LoadingUploadBytesPerFrame, UploadBytesPerFrame ) : UploadBytesPerFrame;

	/// <summary>
	/// Frames of uploads that builds read and decode ahead of, at most: their mips wait in pooled arrays
	/// (<see cref="ContentBuffers"/>) until they're written, and more than the pool keeps would be made and let go again.
	/// </summary>
	public int FramesAhead { get; set; } = 2;

	/// <summary>A budget in bytes for streamed textures, or 0 for as much as video memory has room for.</summary>
	public long BudgetBytes { get; set; }

	/// <summary>Streamed textures.</summary>
	public int Count => textures.Count;

	/// <summary>Bytes of streamed textures' images.</summary>
	public long ResidentBytes { get; private set; }

	/// <summary>Bytes they'd take at the mips they want.</summary>
	public long WantedBytes { get; private set; }

	/// <summary>Builds in flight.</summary>
	public int BuildsInFlight => builds.Count;

	/// <summary>Textures that want mips they don't hold and there's room for: what deterministic runs wait on.</summary>
	public int Waiting { get; private set; }

	/// <summary>Mips given back for room so far.</summary>
	public int GivenBack { get; private set; }

	/// <summary>
	/// Seconds a texture nothing asks for keeps mips finer than its tail with room to spare: after that they're given back,
	/// so what a map streamed in doesn't stay resident once the map has gone. Long enough that turning away from something
	/// and back doesn't stream it again.
	/// </summary>
	public double IdleSeconds { get; set; } = 30;

	/// <summary>Textures shrunk to their tail after going unasked for <see cref="IdleSeconds"/>, for tests and soaks.</summary>
	public int IdleGivenBack { get; private set; }

	/// <summary>Bytes of an image of a file's mips from <paramref name="top"/>.</summary>
	public static long Bytes( TextureFile file, int top )
	{
		long bytes = 0;
		for ( int mip = Math.Max( top, 0 ); mip < file.Mips; mip++ ) bytes += file.MipBytes( mip );
		return bytes;
	}

	/// <summary>Whether a texture file streams: a 2D texture of one slice, with mips above its tail.</summary>
	public static bool Streams( TextureFile file ) => file.Dimension == TextureDimension.Texture2D && file.Slices == 1 && file.Format != Format.Unknown && file.TailTop > 0;

	/// <summary>
	/// Keep a material texture too small to stream (a file with no mips above its tail) where it can be moved out of a block
	/// being emptied, from the next <see cref="Update"/>. Any thread.
	/// </summary>
	public void Keep( BackendTexture texture ) => keptAdded.Enqueue( texture );

	readonly ConcurrentQueue<BackendTexture> keptAdded = new();
	readonly List<BackendTexture> kept = new();

	/// <summary>Keep a texture made from a file's tail (or more) streaming, from the next <see cref="Update"/>. Any thread.</summary>
	public StreamedTexture Add( BackendTexture texture, string path, TextureFile file )
	{
		var streamed = new StreamedTexture( texture, path, file );
		added.Enqueue( streamed );
		return streamed;
	}

	internal void Queue( MaterialAsks asks ) => asked.Enqueue( asks );

	/// <summary>
	/// Once a frame, between frames: take the last frame's asks, finish builds whose files have been read (uploads go with
	/// the frame's next submission, in a <see cref="Device.DeferUploads"/> scope the caller holds), give mips back when
	/// room is short, and start builds for what's wanted.
	/// </summary>
	public void Update()
	{
		lock ( sources )
		{
			foreach ( var source in sources ) source.Take( ask );
		}

		var now = Frame;
		var updateTime = System.Diagnostics.Stopwatch.GetTimestamp();
		while ( keptAdded.TryDequeue( out var small ) ) kept.Add( small );
		while ( added.TryDequeue( out var texture ) )
		{
			texture.LastAskedTime = updateTime;
			textures.Add( texture );
			ResidentBytes += texture.ResidentBytes;
		}

		while ( asked.TryDequeue( out var asks ) )
		{
			var size = asks.Size;
			foreach ( var texture in asks.Textures )
			{
				var top = texture.TopFor( size );
				if ( top < texture.Asked ) texture.Asked = top;
				texture.LastAsked = now;
				texture.LastAskedTime = updateTime;
			}
		}

		if ( now % (uint)Math.Max( WindowFrames, 1 ) == 0 )
		{
			foreach ( var texture in textures ) (texture.AskedBefore, texture.Asked) = (texture.Asked, int.MaxValue);
		}

		Volatile.Write( ref frame, now + 1 );

		Forget();
		Upload();
		Measure( now );
		GiveBack();
		GiveBackIdle( updateTime );
		MoveOutOfSparseBlocks();
		MoveOutOfDrainingBlocks();
		Start();
	}

	// Textures unasked for IdleSeconds back to their tail, a few builds at a time
	void GiveBackIdle( long now )
	{
		if ( IdleSeconds <= 0 ) return;
		var idle = (long)(IdleSeconds * System.Diagnostics.Stopwatch.Frequency);
		foreach ( var texture in textures )
		{
			if ( builds.Count >= MaxBuilds ) break;
			if ( texture.Building is not null || texture.Pinned || texture.ResidentTop >= texture.TailTop || now - texture.LastAskedTime < idle ) continue;
			StartBuild( texture, texture.TailTop );
			IdleGivenBack++;
		}
	}

	/// <summary>
	/// Make the streamed textures in memory blocks that are mostly empty again elsewhere (<see cref="CompactSoon"/>), so the
	/// blocks drain and go back to the driver: after a map unloads, its survivors kept them. A pass goes over every texture
	/// once, a few builds at a time.
	/// </summary>
	public void CompactSoon()
	{
		// Once what was let go has been destroyed and idle textures have gone back to their tails (IdleSeconds), which
		// moves their memory too
		compactAt = 0;
		compactAfter = System.Diagnostics.Stopwatch.GetTimestamp() + (long)((Math.Max( IdleSeconds, 0 ) + 5) * System.Diagnostics.Stopwatch.Frequency);
		compacting = true;
	}

	/// <summary>Streamed textures made again out of mostly empty memory blocks, for soaks and tests.</summary>
	public int Compacted { get; private set; }

	/// <summary>How full a block has to be for what's in it to stay put when compacting.</summary>
	const double SparseBlock = 0.25;

	bool compacting;
	int compactAt;
	long compactAfter;

	void MoveOutOfSparseBlocks()
	{
		if ( !compacting || System.Diagnostics.Stopwatch.GetTimestamp() < compactAfter ) return;
		var gpu = device.Gpu;
		for ( ; compactAt < textures.Count && builds.Count < MaxBuilds; compactAt++ )
		{
			var texture = textures[compactAt];
			if ( texture.Building is not null || !texture.Texture.IsValid || !gpu.InSparseBlock( texture.Texture.Handle, SparseBlock ) ) continue;
			StartBuild( texture, texture.ResidentTop );
			Compacted++;
		}

		if ( compactAt >= textures.Count ) compacting = false;
	}

	/// <summary>Bytes of textures copied out of blocks being emptied each frame, at most (<see cref="Device.InDrainingBlock"/>).</summary>
	public long MoveBytesPerFrame { get; set; } = 32 << 20;

	/// <summary>Textures looked at each frame for being in a block that's being emptied, while one is.</summary>
	public int MoveChecksPerFrame { get; set; } = 256;

	/// <summary>Textures copied out of blocks being emptied so far, and their bytes.</summary>
	public int Moved { get; private set; }

	/// <inheritdoc cref="Moved"/>
	public long MovedBytes { get; private set; }

	int moveAt;

	// While the allocator empties a block (defragmenting), streamed textures and those kept (Keep) in it are copied into
	// other blocks' room on the GPU and swapped in like a build's image: materials re-key, and the old image goes once the
	// frames using it are done
	void MoveOutOfDrainingBlocks()
	{
		var gpu = device.Gpu;
		var count = textures.Count + kept.Count;
		if ( !gpu.Defragmenting || count == 0 ) return;

		long moved = 0;
		var checks = Math.Min( count, MoveChecksPerFrame );
		for ( int i = 0; i < checks && moved < MoveBytesPerFrame; i++ )
		{
			if ( moveAt >= count ) moveAt = 0;
			var index = moveAt++;
			var streamed = index < textures.Count ? textures[index] : null;
			var texture = streamed?.Texture ?? kept[index - textures.Count];
			if ( streamed?.Building is not null || !texture.IsValid || !gpu.InDrainingBlock( texture.Handle ) ) continue;

			TextureHandle copy;
			try
			{
				copy = gpu.Relocate( texture.Handle, texture.Layout );
			}
			catch ( GpuException e ) when ( e.Result == OutOfDeviceMemory )
			{
				OutOfMemory( streamed?.ResidentTop ?? 0 );
				return;
			}

			if ( !copy.IsValid ) continue;
			var bytes = gpu.MemorySizeOf( copy );
			texture.Swap( copy );
			texture.Swapped();
			moved += bytes;
			Moved++;
			MovedBytes += bytes;
		}
	}

	// Textures disposed since
	void Forget()
	{
		kept.RemoveAll( static texture => !texture.IsValid );
		for ( int i = textures.Count - 1; i >= 0; i-- )
		{
			var texture = textures[i];
			if ( texture.Texture.IsValid ) continue;
			ResidentBytes -= texture.ResidentBytes;
			textures.RemoveAt( i );
		}
	}

	/// <summary>A new image of a texture's mips from <see cref="Top"/>: read on a worker, then made and uploaded over frames, then swapped in.</summary>
	internal sealed class Build
	{
		public StreamedTexture Texture;
		public int Top;
		public ContentFiles Content;
		public Task<TextureFile> Read;
		public TextureHandle Image;
		public int NextMip = -1;

		/// <summary>On a worker: the texture's mips from <see cref="Top"/>, pooled until they're uploaded.</summary>
		public static readonly Func<object, TextureFile> ReadFile = static state =>
		{
			var build = (Build)state;
			return ReadMips( build.Content, build.Texture.Path, build.Top );
		};

		/// <summary>The file it read, if it has, its mips given back: once they're uploaded, or it's dropped.</summary>
		public void Release()
		{
			if ( Read is { IsCompletedSuccessfully: true, Result: { } file } ) file.Release();
		}
	}

	/// <summary>Bytes the last <see cref="Update"/> uploaded.</summary>
	public long Uploaded { get; private set; }

	void Upload()
	{
		var gpu = device.Gpu;
		long uploaded = 0;
		Uploaded = 0;
		var most = UploadBytes;
		for ( int i = 0; i < builds.Count && uploaded < most; i++ )
		{
			var build = builds[i];
			if ( !build.Read.IsCompleted ) continue;

			var texture = build.Texture;
			if ( !texture.Texture.IsValid || !build.Read.IsCompletedSuccessfully || build.Read.Result is not { } file || file.TopMip != build.Top )
			{
				if ( texture.Texture.IsValid ) Failed( texture, build.Top );
				Drop( build, i-- );
				continue;
			}

			if ( build.NextMip < 0 )
			{
				try
				{
					build.Image = file.CreateEmpty( gpu, texture.Path );
				}
				catch ( GpuException e ) when ( e.Result == OutOfDeviceMemory )
				{
					// Video memory is full: the texture keeps the mips it has, and nothing grows until room is measured again
					OutOfMemory( build.Top );
					Drop( build, i-- );
					continue;
				}

				build.NextMip = file.Mips - 1;
			}

			// Coarsest first, so a build spread over frames leaves its largest mip for last
			while ( build.NextMip >= build.Top && uploaded < most )
			{
				uploaded += file.Write( gpu, build.Image, build.NextMip );
				build.NextMip--;
			}

			Uploaded = uploaded;
			if ( build.NextMip >= build.Top ) continue;

			// Every mip written with the frame's next submission, ahead of anything that samples it; the writes copied them
			file.Release();
			var bytes = texture.BytesFrom( build.Top );
			ResidentBytes += bytes - texture.ResidentBytes;
			texture.Texture.Swap( build.Image );
			texture.ResidentTop = build.Top;
			texture.ResidentBytes = bytes;
			texture.Building = null;
			texture.Swapped();
			builds.RemoveAt( i-- );
		}
	}

	const int OutOfDeviceMemory = -2;

	/// <summary>Builds whose image couldn't be made because video memory was full, so far.</summary>
	public int OutOfMemoryBuilds { get; private set; }

	uint growAfter;

	// A build's image didn't fit video memory: measure again at once and start no growth for a window of frames, so what's
	// resident gives mips back (GiveBack) instead of more being asked for
	void OutOfMemory( int top )
	{
		OutOfMemoryBuilds++;
		memoryFrame = 0;
		growAfter = Frame + (uint)Math.Max( WindowFrames, 1 );
		if ( OutOfMemoryBuilds == 1 ) Console.Error.WriteLine( $"Borealis.Gpu: streaming: video memory is full, so textures keep fewer mips (a build from mip {top} was dropped)" );
	}

	// A texture whose file couldn't give mips from top: not tried from there or finer again, said once
	void Failed( StreamedTexture texture, int top )
	{
		texture.Unreadable = Math.Max( texture.Unreadable, top );
		ReadFailures++;
		if ( !reportedUnreadable.Add( texture.Path ) ) return;
		Console.Error.WriteLine( $"Borealis.Gpu: streaming {texture.Path}: its file can't give mips from {top}; it keeps the mips it has" );
	}

	readonly HashSet<string> reportedUnreadable = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Builds whose file couldn't be read so far.</summary>
	public int ReadFailures { get; private set; }

	/// <summary>Builds started so far.</summary>
	public int BuildsStarted { get; private set; }

	void Drop( Build build, int index )
	{
		build.Release();
		if ( build.NextMip >= 0 ) device.Gpu.Destroy( build.Image );
		build.Texture.Building = null;
		builds.RemoveAt( index );
	}

	// The room left for textures to grow into: video memory over the headroom, and the budget
	void Measure( uint now )
	{
		if ( memoryFrame == 0 || now - memoryFrame >= 15 )
		{
			memoryFrame = now;
			startedSinceMemory = 0;
			var (used, budget) = device.Gpu.DeviceLocalMemory();
			var headroom = Math.Max( 256L << 20, budget / 10 );
			room = budget - used - headroom;
		}

		var left = room - startedSinceMemory;
		if ( BudgetBytes > 0 ) left = Math.Min( left, BudgetBytes - ResidentBytes - InFlightBytes() );
		available = left;

		long wanted = 0;
		foreach ( var texture in textures ) wanted += texture.BytesFrom( Math.Min( texture.Wanted, texture.ResidentTop ) );
		WantedBytes = wanted;
	}

	long available;

	long InFlightBytes()
	{
		long bytes = 0;
		foreach ( var build in builds ) bytes += Math.Max( build.Texture.BytesFrom( build.Top ) - build.Texture.ResidentBytes, 0 );
		return bytes;
	}

	// When there's no room: textures holding finer mips than they want give them back first, then the least lately asked
	// give back a mip, one build each
	void GiveBack()
	{
		if ( available >= 0 ) return;

		candidates.Clear();
		foreach ( var texture in textures )
		{
			if ( texture.Building is null && !texture.Pinned && texture.ResidentTop < texture.TailTop ) candidates.Add( texture );
		}

		candidates.Sort( static ( a, b ) =>
		{
			var aSpare = a.ResidentTop < a.Wanted;
			var bSpare = b.ResidentTop < b.Wanted;
			if ( aSpare != bSpare ) return aSpare ? -1 : 1;
			return a.LastAsked.CompareTo( b.LastAsked );
		} );

		foreach ( var texture in candidates )
		{
			if ( available >= 0 || builds.Count >= MaxBuilds ) break;
			var top = texture.ResidentTop < texture.Wanted ? texture.Wanted : texture.ResidentTop + 1;
			available += texture.ResidentBytes - texture.BytesFrom( top );
			StartBuild( texture, top );
			GivenBack++;
		}
	}

	// Builds for textures that want finer mips than they hold, the largest shortfall first, while there's room
	void Start()
	{
		candidates.Clear();
		if ( Frame < growAfter )
		{
			Waiting = builds.Count;
			return;
		}

		foreach ( var texture in textures )
		{
			if ( texture.Building is null && texture.Reachable < texture.ResidentTop ) candidates.Add( texture );
		}

		var waiting = 0;
		if ( candidates.Count > 0 )
		{
			candidates.Sort( static ( a, b ) =>
			{
				var shortfall = (b.ResidentTop - b.Reachable).CompareTo( a.ResidentTop - a.Reachable );
				return shortfall != 0 ? shortfall : b.LastAsked.CompareTo( a.LastAsked );
			} );

			// Mips read or waiting to be written
			long ahead = 0;
			foreach ( var build in builds ) ahead += build.Texture.BytesFrom( build.Top );

			foreach ( var texture in candidates )
			{
				var grow = texture.BytesFrom( texture.Reachable ) - texture.ResidentBytes;
				if ( grow > available ) continue;
				waiting++;
				if ( builds.Count >= BuildsAtOnce || (builds.Count > 0 && ahead >= UploadBytes * FramesAhead) ) continue;
				available -= grow;
				startedSinceMemory += grow;
				ahead += texture.BytesFrom( texture.Reachable );
				StartBuild( texture, texture.Reachable );
			}
		}

		Waiting = waiting + builds.Count;
	}

	void StartBuild( StreamedTexture texture, int top )
	{
		var build = new Build { Texture = texture, Top = top, Content = device.Content };
		BuildsStarted++;
		build.Read = BackendDevice.Read( Build.ReadFile, build );
		texture.Building = build;
		builds.Add( build );
	}

	/// <summary>
	/// A texture's mips from <paramref name="top"/>, reading only as much of its file as they need; null when it can't be
	/// read. Its file is read into, and its mips decoded into, <see cref="ContentBuffers"/>: <see cref="TextureFile.Release"/>
	/// it once they're written.
	/// </summary>
	internal static TextureFile ReadMips( ContentFiles content, string path, int top )
	{
		byte[] data = null;
		try
		{
			// The header and the tail fit the first read of most textures; finer mips read the header, then on to them
			data = content.TryReadPrefixPooled( path, top < 0 ? TextureFile.TailBytes + 16384 : 16384, out var length );
			for ( int i = 0; data is not null && i < 4; i++ )
			{
				var needed = TextureFile.PrefixNeeded( data.AsSpan( 0, length ), top );
				if ( needed <= length ) return TextureFile.Read( data.AsSpan( 0, length ), top, pooled: true );
				ContentBuffers.Return( data );
				data = null;
				data = content.TryReadPrefixPooled( path, needed, out length );
			}

			return null;
		}
		catch ( InvalidDataException )
		{
			return null;
		}
		finally
		{
			ContentBuffers.Return( data );
		}
	}

	/// <summary>
	/// A texture's every mip, read and decoded into <see cref="ContentBuffers"/> as <see cref="ReadMips"/> reads them, or
	/// null when there's no file; throws for one that can't be read. <see cref="TextureFile.Release"/> it once written.
	/// </summary>
	internal static TextureFile ReadWhole( ContentFiles content, string path )
	{
		var data = content.TryReadPrefixPooled( path, long.MaxValue, out var length );
		if ( data is null ) return null;
		try
		{
			return TextureFile.Read( data.AsSpan( 0, length ), 0, pooled: true );
		}
		finally
		{
			ContentBuffers.Return( data );
		}
	}
}
