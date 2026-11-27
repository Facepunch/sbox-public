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
	public bool Pinned
	{
		get => Volatile.Read( ref pinned );
		set
		{
			// Set on any thread, often again while it's pinned: newly pinned, its streamer looks at it again at its next update
			var was = Volatile.Read( ref pinned );
			Volatile.Write( ref pinned, value );
			if ( value && !was ) Volatile.Read( ref Rechecks )?.Enqueue( this );
		}
	}

	bool pinned;

	/// <summary>Where it's queued to be looked at again when it's pinned: its streamer's, once added.</summary>
	internal ConcurrentQueue<StreamedTexture> Rechecks;

	/// <summary>In its streamer's set of textures that may want finer mips than they hold.</summary>
	internal bool Growable;

	/// <summary>Gone from its streamer: its texture was disposed.</summary>
	internal bool Forgotten;

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
}

/// <summary>Something that names textures in what it binds (a material), and is told when they go, so it binds defaults.</summary>
internal interface IStreamDependent
{
	/// <summary>A texture it names is gone: move its version on.</summary>
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

	// Textures that may want finer mips than they hold, kept across frames so a frame doesn't look at every texture. A
	// texture only comes to want more than it holds when it's added, asked for finer mips, pinned, or a build of it ends
	// (its mips or its unreadable mip move): those are touched, and join the set; the set drops what no longer wants more.
	// The ask window's rollover only ever makes textures want fewer mips.
	readonly List<StreamedTexture> growable = new();
	readonly List<StreamedTexture> touched = new();
	readonly ConcurrentQueue<StreamedTexture> rechecks = new();
	uint frame = 1;
	long room;
	uint memoryFrame;
	long residentAtMeasure, growMargin;

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

	/// <summary>Stop taking sizes from <paramref name="source"/>: what it sized has gone with its render system.</summary>
	public void RemoveSource( ITextureSizeSource source )
	{
		lock ( sources ) sources.Remove( source );
	}

	/// <summary>Sources sizes are taken from, for tests.</summary>
	internal int SourceCount
	{
		get
		{
			lock ( sources ) return sources.Count;
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

	/// <summary>Bytes they'd take at the mips they want: summed when asked, as only the window's title and reports read it.</summary>
	public long WantedBytes
	{
		get
		{
			long wanted = 0;
			foreach ( var texture in textures ) wanted += texture.BytesFrom( Math.Min( texture.Wanted, texture.ResidentTop ) );
			return wanted;
		}
	}

	/// <summary>Builds in flight.</summary>
	public int BuildsInFlight => builds.Count;

	/// <summary>The most bytes of mips builds have had read or waiting to be written at once so far, for stats.</summary>
	public long PeakAheadBytes { get; private set; }

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
	/// Keep a texture read from a file that doesn't stream (too small to, read whole, a volume) where it can be moved out of a
	/// block being emptied, from the next <see cref="Update"/>. Any thread.
	/// </summary>
	public void Keep( BackendTexture texture ) => keptAdded.Enqueue( texture );

	readonly ConcurrentQueue<BackendTexture> keptAdded = new();
	readonly List<BackendTexture> kept = new();

	/// <summary>
	/// Keep a texture image the engine read from a file (<c>Texture.Load</c> of a <c>.vtex</c>, the view's lookup tables)
	/// where it can be moved out of a block being emptied, from the next <see cref="Update"/>: in <see cref="ImageLayout.ShaderRead"/>
	/// once made and never written again. It's content, and nothing else moves it: each block one was in stayed (a 16 MiB
	/// block for a 64 KiB table at the menu, three of them after a map). Forgotten once destroyed. Any thread.
	/// </summary>
	public void Keep( TextureHandle image ) => keptImagesAdded.Enqueue( image );

	readonly ConcurrentQueue<TextureHandle> keptImagesAdded = new();
	readonly List<TextureHandle> keptImages = new();

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
		while ( keptImagesAdded.TryDequeue( out var image ) ) keptImages.Add( image );
		if ( keptImages.Count > keptImagesLive * 2 + 64 ) ForgetDestroyedImages();
		while ( added.TryDequeue( out var texture ) )
		{
			texture.LastAskedTime = updateTime;
			Volatile.Write( ref texture.Rechecks, rechecks );
			textures.Add( texture );
			touched.Add( texture );
			ResidentBytes += texture.ResidentBytes;
		}

		if ( now % (uint)Math.Max( WindowFrames, 1 ) == 0 )
		{
			foreach ( var texture in textures ) (texture.AskedBefore, texture.Asked) = (texture.Asked, int.MaxValue);
		}

		while ( asked.TryDequeue( out var asks ) )
		{
			var size = asks.Size;
			foreach ( var texture in asks.Textures )
			{
				var top = texture.TopFor( size );
				if ( top < texture.Asked )
				{
					texture.Asked = top;
					touched.Add( texture );
				}
				texture.LastAsked = now;
				texture.LastAskedTime = updateTime;
			}
		}

		Volatile.Write( ref frame, now + 1 );

		Forget();
		Upload();
		Measure( now );
		GiveBack();
		GiveBackIdle( now, updateTime );
		MoveOutOfSparseBlocks();
		MoveOutOfDrainingBlocks();
		Start();
	}

	/// <summary>Frames between looks for textures idle for <see cref="IdleSeconds"/>: a look walks every streamed texture.</summary>
	const uint IdleCheckFrames = 16;

	// Textures unasked for IdleSeconds back to their tail, a few builds at a time, looked for every IdleCheckFrames frames
	void GiveBackIdle( uint frame, long now )
	{
		if ( IdleSeconds <= 0 || frame % IdleCheckFrames != 0 ) return;
		var idle = (long)(IdleSeconds * System.Diagnostics.Stopwatch.Frequency);
		foreach ( var texture in textures )
		{
			if ( texture.Pinned || now - texture.LastAskedTime < idle ) continue;
			// The wall-clock idle timeout can expire before both frame-based ask windows roll over.
			texture.Asked = texture.AskedBefore = int.MaxValue;
			if ( builds.Count >= BuildsAtOnce || texture.Building is not null || texture.ResidentTop >= texture.TailTop ) continue;
			StartBuild( texture, texture.TailTop, texture.BytesFrom( texture.TailTop ) );
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
			StartBuild( texture, texture.ResidentTop, texture.ResidentBytes );
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
	// other blocks' room on the GPU and swapped in like a build's image, under the same handle and bindless index: the old
	// image goes once the frames using it are done
	void MoveOutOfDrainingBlocks()
	{
		var gpu = device.Gpu;
		var count = textures.Count + kept.Count + keptImages.Count;
		if ( !gpu.Defragmenting || count == 0 ) return;

		long moved = 0;
		var checks = Math.Min( count, MoveChecksPerFrame );
		for ( int i = 0; i < checks && moved < MoveBytesPerFrame; i++ )
		{
			if ( moveAt >= count ) moveAt = 0;
			var index = moveAt++;
			if ( index >= textures.Count + kept.Count )
			{
				moved += MoveImage( gpu, index - textures.Count - kept.Count );
				continue;
			}

			var streamed = index < textures.Count ? textures[index] : null;
			var texture = streamed?.Texture ?? kept[index - textures.Count];
			if ( streamed?.Building is not null || !texture.IsValid || gpu.IsReplacing( texture.Handle ) || !gpu.InDrainingBlock( texture.Handle ) ) continue;

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
			gpu.ReplaceSoon();
			moved += bytes;
			Moved++;
			MovedBytes += bytes;
		}
	}

	// One of keptImages out of a block being emptied, swapped in under its handle; the bytes copied. A destroyed one is
	// forgotten (the last takes its place, looked at on a later pass)
	long MoveImage( Device gpu, int index )
	{
		var image = keptImages[index];
		if ( !gpu.IsAlive( image ) )
		{
			keptImages[index] = keptImages[^1];
			keptImages.RemoveAt( keptImages.Count - 1 );
			return 0;
		}

		if ( gpu.IsReplacing( image ) || !gpu.InDrainingBlock( image ) ) return 0;

		TextureHandle copy;
		try
		{
			copy = gpu.Relocate( image, ImageLayout.ShaderRead );
		}
		catch ( GpuException e ) when ( e.Result == OutOfDeviceMemory )
		{
			return 0;
		}

		if ( !copy.IsValid ) return 0;
		gpu.Replace( image, copy );
		gpu.ReplaceSoon();
		var bytes = gpu.MemorySizeOf( copy );
		Moved++;
		MovedBytes += bytes;
		return bytes;
	}

	// keptImages alive at the last look for destroyed ones, which goes once as many more have been added: a map's load and
	// unload each add and destroy their own
	int keptImagesLive;

	void ForgetDestroyedImages()
	{
		var gpu = device.Gpu;
		var live = 0;
		for ( int i = 0; i < keptImages.Count; i++ )
		{
			if ( gpu.IsAlive( keptImages[i] ) ) keptImages[live++] = keptImages[i];
		}

		keptImages.RemoveRange( live, keptImages.Count - live );
		keptImagesLive = live;
	}

	/// <summary>Engine images kept to be moved (<see cref="Keep(TextureHandle)"/>), for the tests.</summary>
	internal int KeptImages => keptImages.Count;

	// The device's textures disposed at the last look for disposed ones
	int disposedSeen = -1;

	/// <summary>Looks for disposed textures so far, for the tests: one after each frame a texture of the device's was disposed in.</summary>
	public int ForgetScans { get; private set; }

	// Textures disposed since: looked for only once a texture of the device's has been disposed since the last look
	void Forget()
	{
		var disposed = Volatile.Read( ref device.TexturesDisposed );
		if ( disposed == disposedSeen ) return;
		disposedSeen = disposed;
		ForgetScans++;
		kept.RemoveAll( static texture => !texture.IsValid );
		for ( int i = textures.Count - 1; i >= 0; i-- )
		{
			var texture = textures[i];
			if ( texture.Texture.IsValid ) continue;
			ResidentBytes -= texture.ResidentBytes;
			texture.Forgotten = true;
			textures.RemoveAt( i );
		}
	}

	/// <summary>A new image of a texture's mips from <see cref="Top"/>: read on a worker, then made and uploaded over frames, then swapped in.</summary>
	internal sealed class Build
	{
		public StreamedTexture Texture;
		public int Top;
		public long Bytes;
		public ContentFiles Content;
		public Task<TextureFile> Read;
		public TextureHandle Image;
		public int NextMip = -1;

		/// <summary>The device its image is made on, on the worker that reads it.</summary>
		public Device Gpu;

		/// <summary>Its image couldn't be made: video memory is full.</summary>
		public bool OutOfMemory;

		/// <summary>
		/// On a worker: the texture's mips from <see cref="Top"/>, pooled until they're uploaded, and the image they go in. A
		/// big image is an allocation of its own from the driver, which takes milliseconds: not on the frame's thread.
		/// </summary>
		public static readonly Func<object, TextureFile> ReadFile = static state =>
		{
			var build = (Build)state;
			var file = ReadMips( build.Content, build.Texture.Path, build.Top );
			if ( file is null || file.TopMip != build.Top || build.Gpu is null ) return file;

			try
			{
				build.Image = file.CreateEmpty( build.Gpu, build.Texture.Path );
			}
			catch ( GpuException e ) when ( e.Result == OutOfDeviceMemory )
			{
				build.OutOfMemory = true;
			}
			catch ( NotSupportedException )
			{
				// Made again on the frame's thread, which reports it
			}

			return file;
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

			if ( build.NextMip < 0 && build.OutOfMemory )
			{
				OutOfMemory( build.Top );
				Drop( build, i-- );
				continue;
			}

			if ( build.NextMip < 0 )
			{
				try
				{
					if ( !build.Image.IsValid ) build.Image = file.CreateEmpty( gpu, texture.Path );
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
			ResidentBytes += build.Bytes - texture.ResidentBytes;
			texture.Texture.Swap( build.Image );
			texture.ResidentTop = build.Top;
			texture.ResidentBytes = build.Bytes;
			texture.Building = null;
			touched.Add( texture );
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
		device.Gpu.ReplaceSoon();
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

	/// <summary>
	/// Before the device goes: wait for the builds being read (their workers make images on it) and destroy the images of
	/// those not swapped in.
	/// </summary>
	public void Finish()
	{
		for ( int i = builds.Count - 1; i >= 0; i-- )
		{
			var build = builds[i];
			try
			{
				build.Read?.Wait();
			}
			catch ( AggregateException )
			{
				// A read that failed made nothing
			}

			Drop( build, i );
		}
	}

	void Drop( Build build, int index )
	{
		build.Release();
		if ( build.Image.IsValid ) device.Gpu.Destroy( build.Image );
		build.Texture.Building = null;
		touched.Add( build.Texture );
		builds.RemoveAt( index );
	}

	// Keep projecting against the last sample until there's a new one, including builds that straddle it.
	void Measure( uint now )
	{
		if ( memoryFrame == 0 || now - memoryFrame >= 15 )
		{
			memoryFrame = now;
			residentAtMeasure = ResidentBytes;
			var (used, budget) = device.Gpu.DeviceLocalMemory();
			var headroom = Math.Max( 256L << 20, budget / 10 );
			room = budget - used - headroom;
			growMargin = budget / 10;
		}

		var left = room + residentAtMeasure - ResidentBytes;
		var textureBudget = BudgetBytes;
		var budgetLeft = textureBudget > 0 ? textureBudget - ResidentBytes : long.MaxValue;
		var budgetMargin = textureBudget > 0 ? textureBudget / 10 : 0;
		long growing = 0, shrinking = 0;
		foreach ( var build in builds )
		{
			var change = build.Bytes - build.Texture.ResidentBytes;
			if ( change > 0 ) growing += change;
			else shrinking -= change;
		}

		available = Math.Min( left, budgetLeft ) - growing + shrinking;
		growAvailable = Math.Min( left - growMargin, budgetLeft - budgetMargin ) - growing;
	}

	long available, growAvailable;

	// Give back mips from textures this window hasn't asked for first, then from those with spare mips.
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
			var aAsked = a.Asked != int.MaxValue;
			var bAsked = b.Asked != int.MaxValue;
			if ( aAsked != bAsked ) return aAsked ? 1 : -1;
			var aSpare = a.ResidentTop < a.Wanted;
			var bSpare = b.ResidentTop < b.Wanted;
			if ( aSpare != bSpare ) return aSpare ? -1 : 1;
			return a.LastAsked.CompareTo( b.LastAsked );
		} );

		foreach ( var texture in candidates )
		{
			if ( available >= 0 || builds.Count >= BuildsAtOnce ) break;
			var top = texture.ResidentTop < texture.Wanted ? texture.Wanted : texture.ResidentTop + 1;
			var bytes = texture.BytesFrom( top );
			available += texture.ResidentBytes - bytes;
			StartBuild( texture, top, bytes );
			GivenBack++;
		}
	}

	// Builds for textures that want finer mips than they hold, largest shortfall first, while there's room above the margin
	void Start()
	{
		candidates.Clear();
		while ( rechecks.TryDequeue( out var pinned ) ) touched.Add( pinned );
		foreach ( var texture in touched )
		{
			if ( texture.Growable || texture.Forgotten ) continue;
			texture.Growable = true;
			growable.Add( texture );
		}

		touched.Clear();
		if ( Frame < growAfter || growAvailable <= 0 )
		{
			Waiting = builds.Count;
			return;
		}

		// What still wants finer mips than it holds and isn't being built; the rest leave the set until they're touched again
		var still = 0;
		for ( int i = 0; i < growable.Count; i++ )
		{
			var texture = growable[i];
			if ( !texture.Forgotten && texture.Building is null && texture.Reachable < texture.ResidentTop )
			{
				growable[still++] = texture;
				candidates.Add( texture );
			}
			else
			{
				// A build's end touches its texture again
				texture.Growable = false;
			}
		}

		growable.RemoveRange( still, growable.Count - still );

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
			foreach ( var build in builds ) ahead += build.Bytes;

			foreach ( var texture in candidates )
			{
				var top = texture.Reachable;
				var bytes = texture.BytesFrom( top );
				var grow = bytes - texture.ResidentBytes;
				if ( grow > growAvailable ) continue;
				waiting++;
				if ( builds.Count >= BuildsAtOnce || (builds.Count > 0 && ahead >= UploadBytes * FramesAhead) ) continue;
				growAvailable -= grow;
				ahead += bytes;
				StartBuild( texture, top, bytes );
			}

			PeakAheadBytes = Math.Max( PeakAheadBytes, ahead );
		}

		Waiting = waiting + builds.Count;
	}

	void StartBuild( StreamedTexture texture, int top, long bytes )
	{
		var build = new Build { Texture = texture, Top = top, Bytes = bytes, Content = device.Content, Gpu = device.Gpu };
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
