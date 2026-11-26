using Borealis.Source2.Materials;
using Borealis.Source2.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Borealis.Backend;

/// <summary>
/// Models loaded without holding up the frame (<see cref="LoadModelAsync"/>): a model's file, its materials' files and their
/// textures are read and decoded on workers, then its buffers, materials and textures are made on the caller's thread, a
/// few each frame within <see cref="LoadBudgetMs"/>. <see cref="LoadModel"/> still loads at once, for what must be there
/// (the lab, tests, and anything that measures).
/// </summary>
public sealed partial class BackendDevice
{
	readonly ConcurrentDictionary<string, Task<ModelFile>> modelReads = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<string, MaterialFile> readMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<string, TextureFile> readTextures = new( StringComparer.OrdinalIgnoreCase );

	// Images made on the worker that read a big whole texture (ImageAheadBytes), with the file they're for and, for one of
	// StageAheadBytes or more, the upload buffer its mips were copied into there (StageAheadBytes), for Load to fill
	readonly ConcurrentDictionary<string, (TextureFile File, Borealis.Gpu.TextureHandle Image, Borealis.Gpu.BufferHandle Staged)> imagesAhead = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A whole texture read ahead (<see cref="LoadTextureAsync"/>, a map's lightmaps, a model's texture that doesn't stream)
	/// of at least this many bytes has its image made on the worker too: the driver's allocation for one takes milliseconds,
	/// and a map's lightmaps took 100 ms of a frame. 0 makes none ahead. For comparisons.
	/// </summary>
	public static long ImageAheadBytes { get; set; } = 1 << 20;

	/// <summary>
	/// An image made ahead (<see cref="ImageAheadBytes"/>) whose top mip is at least this many bytes has its mips copied
	/// into an upload buffer of its own on the worker too, so the frame that makes it only records copies: written there, a
	/// mip larger than an upload page took an upload allocation of its own, about 20 ms each, and Construct's lightmaps took
	/// 96 ms of a frame after its load. 0 stages none. For comparisons.
	/// </summary>
	public static long StageAheadBytes { get; set; } = 4 << 20;
	readonly Dictionary<(string Path, ulong Groups), RenderMesh> streamedModels = new();
	int readsPending;
	ulong loadFrame, budgetFrame;
	long budgetEnd;

	/// <summary>
	/// Whether whoever asks for models here (the game window's mirror) loads them with <see cref="LoadModelAsync"/> rather
	/// than <see cref="LoadModel"/>.
	/// </summary>
	public bool AsyncModels { get; set; }

	/// <summary>
	/// Whether material textures stream (<see cref="TextureStreamer"/>): made from their tails, with the mips their draws
	/// ask for read and swapped in later, within video memory's room. Set before loading anything.
	/// </summary>
	public bool StreamTextures { get; set; }

	/// <summary>A budget in bytes for streamed textures, or 0 for as much as video memory has room for.</summary>
	public long TextureBudgetBytes
	{
		get => Streamer.BudgetBytes;
		set => Streamer.BudgetBytes = value;
	}

	TextureStreamer streamer;

	/// <summary>Textures of this device disposed so far (<see cref="BackendTexture.Dispose"/>, any thread): the streamer looks for disposed ones when it moves.</summary>
	internal int TexturesDisposed;

	internal TextureStreamer Streamer => streamer ??= new TextureStreamer( this ) { Loading = loading };

	internal override bool StreamsTextures => StreamTextures;

	internal override void AddTextureSizes( ITextureSizeSource source )
	{
		if ( StreamTextures ) Streamer.AddSource( source );
	}

	internal override void RemoveTextureSizes( ITextureSizeSource source ) => streamer?.RemoveSource( source );

	/// <summary>
	/// Whether streamed textures still want mips they don't hold and have room for, or have builds in flight. What
	/// measures or compares frames waits until it's false, as for <see cref="LoadingModels"/>.
	/// </summary>
	public bool StreamingTextures => streamer is { } s && s.Waiting > 0;

	/// <summary>Streamed textures, for the window's stats: how many, their bytes now and at the mips they want, builds in flight.</summary>
	public string DescribeStreaming() => streamer is not { } s ? null
		: $"streamed textures {s.Count}, {s.ResidentBytes >> 20} MiB resident, {s.WantedBytes >> 20} MiB wanted, {s.BuildsInFlight} building, {s.GivenBack} given back";

	/// <summary>Streamed textures' bytes now and at the mips they want, and builds in flight; null when nothing streams.</summary>
	public (long Resident, long Wanted, int Building)? StreamingStats => streamer is { } s ? (s.ResidentBytes, s.WantedBytes, s.BuildsInFlight) : null;

	/// <summary>A draw's texture size (<c>TextureSizes</c>) for its material's streamed textures, from any recording thread.</summary>
	internal void AskTextures( GpuMaterial material, int size )
	{
		if ( streamer is not { } s ) return;
		switch ( material )
		{
			case BackendMaterial m: m.Asks.Ask( s, size ); break;
			case SurfaceMaterial m: m.Asks.Ask( s, size ); break;
		}
	}

	/// <summary>
	/// Main thread time each frame may spend making models whose files have been read (<see cref="LoadModelAsync"/>), at
	/// least: while frames are slower (a map loading), up to <see cref="LoadBudgetShare"/> of the last one.
	/// </summary>
	public double LoadBudgetMs { get; set; } = 6;

	/// <summary>
	/// The share of the last frame's time the next may spend making models, up to <see cref="LoadBudgetMaxMs"/>, when that's
	/// more than <see cref="LoadBudgetMs"/>; the upload budget grows with it. A map's models arrive while its first frames are
	/// slow anyway, and a fast frame's budget would spread them over many seconds.
	/// </summary>
	public double LoadBudgetShare { get; set; } = 0.25;

	/// <summary>The most main thread time a frame spends making models, however slow the last one was.</summary>
	public double LoadBudgetMaxMs { get; set; } = 32;

	long lastFrameStart, lastFrameTicks;

	/// <summary>
	/// Bytes of buffers and textures each frame may upload for what <see cref="LoadModelAsync"/> and
	/// <see cref="LoadTextureAsync"/> make, so the GPU isn't held up copying a whole map in one frame.
	/// </summary>
	public long LoadUploadBytes { get; set; } = 32 << 20;

	long budgetBytes;

	/// <summary>
	/// Whether a loading screen hides the frames (the host says so each frame): nobody watches them, so what's read is made
	/// within <see cref="LoadingBudgetMs"/> and <see cref="LoadingUploadBytes"/> a frame instead, and textures stream faster
	/// (<see cref="TextureStreamer.Loading"/>). A map's models are then made before its first frame, as native makes them in
	/// its load, rather than over the seconds after it. As a load starts, its meshes start new geometry pages
	/// (<see cref="GpuDrivenTables.StartNewPages"/>), so they go with it, and the compute pipelines its first frames make
	/// whatever the map are queued (<see cref="QueueLoadPipelines"/>).
	/// </summary>
	public bool Loading
	{
		get => loading;
		set
		{
			if ( value && !loading )
			{
				gpuDrivenTables?.StartNewPages();
				QueueLoadPipelines();
			}

			loading = value;
			if ( streamer is { } textures ) textures.Loading = value;
			Gpu.HoldRelocation = value;
			Gpu.BuildersYield = !value;
		}
	}

	bool loading;

	/// <summary>Main thread time a frame behind a loading screen may spend making models (<see cref="Loading"/>).</summary>
	public double LoadingBudgetMs { get; set; } = 100;

	/// <summary>Bytes a frame behind a loading screen may upload for what's made (<see cref="Loading"/>).</summary>
	public long LoadingUploadBytes { get; set; } = 256 << 20;

	/// <summary>Models still being read on workers for <see cref="LoadModelAsync"/>.</summary>
	public int ModelReadsPending => Volatile.Read( ref readsPending );

	/// <summary>
	/// Whether models are still loading: being read, or asked for in the last two frames and not made yet. What measures or
	/// compares frames waits until it's false.
	/// </summary>
	public bool LoadingModels => ModelReadsPending > 0 || pendingFrame + 2 > loadFrame;

	/// <summary>
	/// Whether shaders and pipelines the frames asked for are still being made on workers: pass module stages, surfaces,
	/// pipelines whose draws are skipped until they're ready, and those a load queues ahead (<see cref="QueueLoadPipelines"/>).
	/// A load waits behind its loading screen until it's false, as for <see cref="LoadingModels"/>, so the first frame it
	/// shows compiles nothing.
	/// </summary>
	public bool CompilingShaders => Passes.Compiling > 0 || (surfaces?.Compiling ?? 0) > 0 || Gpu.DeferredPipelines.Pending > 0 || LoadPipelinesPending;

	/// <summary>The time between the last two frames' starts, for the load budget.</summary>
	void TimeFrame()
	{
		var now = Stopwatch.GetTimestamp();
		lastFrameTicks = lastFrameStart == 0 ? 0 : now - lastFrameStart;
		lastFrameStart = now;
	}

	ulong pendingFrame;

	/// <summary>
	/// Where content reads run (models, their materials and textures, streamed mips): the thread pool, at most a core's worth
	/// at once. Started one task each, a map's thousands of reads grew the pool to hundreds of threads that slowed each other
	/// and the main thread.
	/// </summary>
	internal static TaskScheduler Reads { get; } = new ConcurrentExclusiveSchedulerPair( TaskScheduler.Default, Math.Max( Environment.ProcessorCount - 2, 2 ) ).ConcurrentScheduler;

	/// <summary>Run a content read on <see cref="Reads"/>.</summary>
	internal static Task<T> Read<T>( Func<T> read ) => Task.Factory.StartNew( read, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Reads );

	/// <summary>Run a content read on <see cref="Reads"/>, with its state passed rather than captured.</summary>
	internal static Task<T> Read<T>( Func<object, T> read, object state ) => Task.Factory.StartNew( read, state, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Reads );

	/// <summary>
	/// A model as <see cref="LoadModel"/> makes it (every mesh group in <paramref name="meshGroups"/>, the default material
	/// group), without waiting: while its files are read on a worker, or once this frame's <see cref="LoadBudgetMs"/> is
	/// spent, it's null with <paramref name="pending"/> set; ask again later. Null without pending when there's no compiled
	/// model. Call on one thread at a time.
	/// </summary>
	/// <remarks>
	/// Without <paramref name="pin"/> the caller holds the model it gets (<see cref="HoldModel"/>) and it's freed once nothing
	/// holds it (<see cref="ReleaseModel"/>), as a world's mirror does. A model loaded pinned once stays.
	/// </remarks>
	public RenderMesh LoadModelAsync( string path, ulong meshGroups, out bool pending, bool pin = true )
	{
		pending = false;
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( streamedModels.TryGetValue( (path, meshGroups), out var made ) )
		{
			if ( pin && made is not null ) PinModel( made );
			return made;
		}

		var read = modelReads.GetOrAdd( path, StartRead );
		if ( !read.IsCompleted )
		{
			pending = true;
			pendingFrame = loadFrame;
			return null;
		}

		if ( !WithinBudget() )
		{
			pending = true;
			pendingFrame = loadFrame;
			return null;
		}

		// Its buffers and textures written with the frame's next submission, not each waited for
		using ( Zones.ModelMake.Start() )
		using ( Gpu.DeferUploads() )
			made = read.IsCompletedSuccessfully && read.Result is { } file ? BackendModel.Create( this, file, meshGroups ) : null;
		streamedModels[(path, meshGroups)] = made;
		if ( made is not null )
		{
			lock ( modelLives ) modelLives[made] = new ModelLife { Key = (path, meshGroups), Model = made.Geometry as BackendModel, Pinned = pin };
		}

		return made;
	}

	//
	// Model lifetimes: a model loaded for worlds' mirrors is held by each (HoldModel) and freed a while after the last lets
	// go (ReleaseModel), its vertices and indices given back to the GPU-driven pages
	//

	sealed class ModelLife
	{
		public (string Path, ulong Groups) Key;

		/// <summary>A model made at run time (<see cref="TrackModel"/>), which has no key in the loaded models.</summary>
		public RenderMesh Mesh;

		/// <summary>Its geometry, whose last use (<see cref="BackendModel.LastUsedFrame"/>) says whether anything still draws it.</summary>
		public BackendModel Model;
		public int Holders;
		public bool Pinned;
		public long ReleasedAt;

		/// <summary>The device frame the last holder let it go in.</summary>
		public long ReleasedFrame;

		/// <summary>Whether it has been reported as drawn after its last holder let it go.</summary>
		public bool Reported;

		/// <summary>Whether it's in the unheld models, once however often it was held and let go again.</summary>
		public bool Queued;
	}

	readonly Dictionary<RenderMesh, ModelLife> modelLives = new( ReferenceEqualityComparer.Instance );
	readonly List<ModelLife> unheldModels = new();
	readonly List<RenderMesh> freeing = new();
	HashSet<string> freedPaths;
	List<string> warnings;

	/// <summary>Seconds a model nothing holds is kept, so the same map loaded again soon finds it.</summary>
	public static double ModelGraceSeconds { get; set; } = 10;

	/// <summary>Models freed after nothing held them, for soaks and tests.</summary>
	public int ModelsFreed { get; private set; }

	/// <summary>A world draws <paramref name="mesh"/> (loaded unpinned by <see cref="LoadModelAsync"/>).</summary>
	public void HoldModel( RenderMesh mesh )
	{
		if ( mesh is null ) return;
		lock ( modelLives )
		{
			if ( modelLives.TryGetValue( mesh, out var life ) ) life.Holders++;
		}
	}

	/// <summary>A world that held <paramref name="mesh"/> no longer draws it: the last one lets it go after <see cref="ModelGraceSeconds"/>.</summary>
	public void ReleaseModel( RenderMesh mesh )
	{
		if ( mesh is null ) return;
		lock ( modelLives )
		{
			if ( !modelLives.TryGetValue( mesh, out var life ) || life.Holders == 0 ) return;
			if ( --life.Holders > 0 || life.Pinned ) return;
			life.ReleasedAt = Stopwatch.GetTimestamp();
			life.ReleasedFrame = (long)Gpu.FrameNumber;
			if ( life.Queued ) return;
			life.Queued = true;
			unheldModels.Add( life );
		}
	}

	/// <summary>
	/// A model made at run time (<see cref="MakeModel"/>, no file behind it) is held by worlds as a loaded one is
	/// (<see cref="HoldModel"/>) and freed <see cref="ModelGraceSeconds"/> after the last lets go: a game's procedural model
	/// still referenced after its map was unloaded held a vertex page and a 56 MiB index page. Its maker makes it again if
	/// it's asked for after that (<see cref="BackendModel.Freed"/>).
	/// </summary>
	public void TrackModel( RenderMesh mesh )
	{
		if ( mesh is null ) return;
		lock ( modelLives ) modelLives.TryAdd( mesh, new ModelLife { Mesh = mesh, Model = mesh.Geometry as BackendModel } );
	}

	void PinModel( RenderMesh mesh )
	{
		lock ( modelLives )
		{
			if ( modelLives.TryGetValue( mesh, out var life ) ) life.Pinned = true;
		}
	}

	/// <summary>Models kept because something drew them after their last holder let go, for soaks and tests.</summary>
	public int ModelsDrawnUnheld { get; private set; }

	/// <summary>
	/// At a frame's start: free the models nothing has held for <see cref="ModelGraceSeconds"/> (the next ask loads them
	/// again), and the GPU-driven pages all of whose meshes have gone. One something drew after its last holder let go is
	/// kept, for another grace period from then, and reported once: freeing it would leave that draw binding destroyed buffers.
	/// </summary>
	void FreeUnheldModels()
	{
		if ( unheldModels.Count > 0 )
		{
			var due = Stopwatch.GetTimestamp() - (long)(ModelGraceSeconds * Stopwatch.Frequency);
			lock ( modelLives )
			{
				for ( int i = unheldModels.Count - 1; i >= 0; i-- )
				{
					var life = unheldModels[i];
					if ( life.Holders > 0 || life.Pinned )
					{
						life.Queued = false;
						unheldModels.RemoveAt( i );
						continue;
					}

					if ( life.ReleasedAt > due ) continue;
					if ( life.Model is { } drawn && drawn.LastUsedFrame > life.ReleasedFrame )
					{
						life.ReleasedAt = Stopwatch.GetTimestamp();
						life.ReleasedFrame = (long)Gpu.FrameNumber;
						ModelsDrawnUnheld++;
						if ( !life.Reported ) (warnings ??= new()).Add( drawn.Name );
						life.Reported = true;
						continue;
					}

					life.Queued = false;
					unheldModels.RemoveAt( i );
					if ( life.Mesh is { } made )
					{
						modelLives.Remove( made );
						freeing.Add( made );
					}
					else if ( streamedModels.Remove( life.Key, out var mesh ) && mesh is not null )
					{
						modelLives.Remove( mesh );
						freeing.Add( mesh );
						(freedPaths ??= new( StringComparer.OrdinalIgnoreCase )).Add( life.Key.Path );
					}
				}
			}

			// A freed model's file (its vertices and indices as read) goes too, unless another of its mesh group sets is still made
			if ( freedPaths is { Count: > 0 } )
			{
				foreach ( var key in streamedModels.Keys ) freedPaths.Remove( key.Path );
				foreach ( var path in freedPaths ) modelReads.TryRemove( path, out _ );
				freedPaths.Clear();
			}

			if ( warnings is not null )
			{
				foreach ( var name in warnings ) Warn?.Invoke( $"Borealis.Gpu: {name} is still drawn after the last world holding it let it go; kept until nothing draws it" );
				warnings.Clear();
			}

			foreach ( var mesh in freeing )
			{
				(mesh.Geometry as BackendModel)?.Free();
				ModelsFreed++;
			}

			// What the models' textures shared memory blocks with is moved out of those left mostly empty
			if ( freeing.Count > 0 && streamer is { } textures ) textures.CompactSoon();
			freeing.Clear();
		}

		lock ( freedMaterials )
		{
			if ( freedMaterials.Count > 0 )
			{
				gpuDrivenTables?.ForgetMaterials( freedMaterials );
				freedMaterials.Clear();
			}
		}

		gpuDrivenTables?.FreeEmptyPages();
		Borealis.Source2.ContentBuffers.TrimIdle();
	}

	/// <summary>
	/// Start reading a model made at run time, which has no file: <paramref name="save"/> makes its compiled bytes
	/// (<c>Model.SaveToVmdl</c>) on a worker, where they're read with its materials and textures, for <see cref="MakeModel"/>.
	/// </summary>
	public Task<ModelFile> ReadModelAsync( Func<byte[]> save )
	{
		Interlocked.Increment( ref readsPending );
		QueueWorkItem();
		return Read( () =>
		{
			try
			{
				if ( !StartWorkItem( "read" ) ) return null;
				using var zone = Zones.ModelRead.Start();
				return ReadModelAhead( save() );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
				EndWorkItem();
			}
		} );
	}

	/// <summary>
	/// A model <see cref="ReadModelAsync"/> read, made as <see cref="LoadModelAsync"/> makes one: null with
	/// <paramref name="pending"/> set while it's read or once this frame's budget is spent; ask again later. Null without
	/// pending when it couldn't be read. Call on one thread at a time.
	/// </summary>
	public RenderMesh MakeModel( Task<ModelFile> read, ulong meshGroups, bool drawFastPath, out bool pending, bool generateLevels = true )
	{
		pending = false;
		if ( !read.IsCompleted || !WithinBudget() )
		{
			pending = true;
			pendingFrame = loadFrame;
			return null;
		}

		if ( !read.IsCompletedSuccessfully || read.Result is not { } file ) return null;
		using ( Zones.ModelMake.Start() )
		using ( Gpu.DeferUploads() )
			return BackendModel.Create( this, file, meshGroups, drawFastPath: drawFastPath, generateLevels: generateLevels );
	}

	/// <summary>
	/// Whether this frame's <see cref="LoadBudgetMs"/> and <see cref="LoadUploadBytes"/> have room left; they start with the
	/// first thing made in the frame.
	/// </summary>
	bool WithinBudget()
	{
		var now = Stopwatch.GetTimestamp();
		if ( budgetFrame != loadFrame && loading )
		{
			budgetFrame = loadFrame;
			budgetEnd = now + (long)(LoadingBudgetMs * Stopwatch.Frequency / 1000);
			budgetBytes = Gpu.DeferredUploadBytes + LoadingUploadBytes;
		}
		else if ( budgetFrame != loadFrame )
		{
			budgetFrame = loadFrame;
			var scale = Math.Clamp( LoadBudgetShare * lastFrameTicks * 1000.0 / Stopwatch.Frequency / LoadBudgetMs, 1.0, Math.Max( LoadBudgetMaxMs / LoadBudgetMs, 1.0 ) );
			budgetEnd = now + (long)(LoadBudgetMs * scale * Stopwatch.Frequency / 1000);
			budgetBytes = Gpu.DeferredUploadBytes + (long)(LoadUploadBytes * scale);
		}

		return now <= budgetEnd && Gpu.DeferredUploadBytes < budgetBytes;
	}

	readonly ConcurrentDictionary<string, Task> textureReads = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A texture as <see cref="LoadTexture"/> makes it, without waiting: read and decoded on a worker, then made within
	/// <see cref="LoadBudgetMs"/>; null with <paramref name="pending"/> set until then. Call on one thread at a time. With
	/// <paramref name="hold"/> the texture it returns is held by the caller until <see cref="ReleaseTexture"/> (as
	/// <see cref="HoldTexture"/> holds one), not pinned.
	/// </summary>
	public GpuTexture LoadTextureAsync( string path, out bool pending, bool hold = false )
	{
		pending = false;
		if ( string.IsNullOrEmpty( path ) ) return null;
		lock ( loadedTextures )
		{
			if ( loadedTextures.ContainsKey( path ) ) return LoadOnce( path, streamed: false, hold );
		}

		var read = textureReads.GetOrAdd( path, StartTextureRead );
		if ( !read.IsCompleted || !WithinBudget() )
		{
			pending = true;
			pendingFrame = loadFrame;
			return null;
		}

		textureReads.TryRemove( path, out _ );
		using ( Gpu.DeferUploads() )
			return LoadOnce( path, streamed: false, hold );
	}

	Task StartTextureRead( string path )
	{
		Interlocked.Increment( ref readsPending );
		QueueWorkItem();
		return Read( () =>
		{
			try
			{
				if ( !StartWorkItem( "read" ) ) return false;
				ReadTextureAhead( path );
				return true;
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
				EndWorkItem();
			}
		} );
	}

	/// <summary>Whether a texture has been read for <see cref="LoadTextureAsync"/> (or isn't being), so asking again can make it.</summary>
	public bool TextureRead( string path ) => !textureReads.TryGetValue( path, out var read ) || read.IsCompleted;

	/// <summary>Whether a model's files have been read for <see cref="LoadModelAsync"/>, so asking again can make it.</summary>
	public bool ModelRead( string path ) => modelReads.TryGetValue( path, out var read ) && read.IsCompleted;

	/// <summary>A model's file as <see cref="LoadModelAsync"/> reads it, on a worker, shared with it: null where there's none.</summary>
	public Task<ModelFile> ReadModel( string path ) => modelReads.GetOrAdd( path, StartRead );

	Task<ModelFile> StartRead( string path )
	{
		Interlocked.Increment( ref readsPending );
		QueueWorkItem();
		return Read( () =>
		{
			try
			{
				if ( !StartWorkItem( "read" ) ) return null;
				using var zone = Zones.ModelRead.Start();
				return ReadModelAhead( path );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
				EndWorkItem();
			}
		} );
	}

	// On a worker: the model's file, and its materials' files and textures, read and decoded for the thread that makes them
	ModelFile ReadModelAhead( string path ) => ReadContent( path ) is { } data ? ReadModelAhead( data ) : null;

	byte[] ReadContent( string path )
	{
		using var zone = Zones.ContentRead.Start();
		return Content.TryRead( path );
	}

	ModelFile ReadModelAhead( byte[] data )
	{
		var file = ModelFile.Read( data, Content.TryRead );
		foreach ( var mesh in file.Meshes )
		{
			foreach ( var call in mesh.DrawCalls ) ReadMaterialAhead( file.MaterialFor( call.Material, 0 ) );
		}

		return file;
	}

	void ReadMaterialAhead( string path )
	{
		if ( string.IsNullOrEmpty( path ) || readMaterials.ContainsKey( path ) ) return;
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.ContainsKey( path ) ) return;
		}

		MaterialFile file;
		using ( Zones.MaterialRead.Start() )
		{
			if ( ReadContent( path ) is not { } data || ReadMaterial( data ) is not { } read ) return;
			file = read;
		}

		readMaterials.TryAdd( path, file );

		// Its shader too: the first load of one reads a large file, and a package's may recompile; and the render state its
		// stencil check reads (a pass module's preprocessed lines)
		if ( !string.IsNullOrEmpty( file.Shader ) )
		{
			using var zone = Zones.ShaderRead.Start();
			BackendMaterial.ReadStencilAhead( Shaders, Shaders.Load( file.Shader ), file );
		}

		foreach ( var (_, texture) in file.Textures ) ReadTextureAhead( texture, tail: StreamTextures );
	}

	void ReadTextureAhead( string path, bool tail = false )
	{
		// Nothing more once the device is going (Dispose waits for the read this is part of)
		if ( disposing || string.IsNullOrEmpty( path ) || readTextures.ContainsKey( path ) ) return;
		lock ( loadedTextures )
		{
			if ( loadedTextures.ContainsKey( path ) ) return;
		}

		using var zone = Zones.TextureRead.Start();
		try
		{
			// A streamed texture's tail, unless it doesn't stream; its mips pooled until Load has written them
			var texture = tail ? TextureStreamer.ReadMips( Content, path, TextureFile.Tail ) : null;
			if ( texture is null || (texture.TopMip > 0 && !TextureStreamer.Streams( texture )) )
			{
				texture?.Release();
				texture = TextureStreamer.ReadWhole( Content, path );
			}

			if ( texture is null ) return;

			// Its image, and a big one's mips staged, before Load can take the file: it releases the mips once it's made
			var bytes = TextureStreamer.Bytes( texture, 0 );
			(TextureFile File, Borealis.Gpu.TextureHandle Image, Borealis.Gpu.BufferHandle Staged) ahead = default;
			if ( ImageAheadBytes > 0 && texture.TopMip == 0 && texture.Format != Borealis.Gpu.Format.Unknown && bytes >= ImageAheadBytes && !imagesAhead.ContainsKey( path ) )
			{
				ahead = (texture, texture.CreateEmpty( Gpu, path ), default);
				if ( StageAheadBytes > 0 && bytes >= StageAheadBytes && texture.Dimension != Borealis.Gpu.TextureDimension.Texture3D ) ahead.Staged = Stage( texture );
				if ( !imagesAhead.TryAdd( path, ahead ) )
				{
					DestroyAhead( ahead );
					ahead = default;
				}
			}

			if ( !readTextures.TryAdd( path, texture ) )
			{
				if ( ahead.File is not null && imagesAhead.TryRemove( new KeyValuePair<string, (TextureFile, Borealis.Gpu.TextureHandle, Borealis.Gpu.BufferHandle)>( path, ahead ) ) ) DestroyAhead( ahead );
				texture.Release();
				return;
			}
		}
		catch ( InvalidDataException )
		{
			// Read again where it's made, which reports it
		}
	}

	/// <summary>An upload buffer holding <paramref name="texture"/>'s mips (<see cref="StageAheadBytes"/>), or none when there's no memory for one.</summary>
	Borealis.Gpu.BufferHandle Stage( TextureFile texture )
	{
		Borealis.Gpu.BufferHandle staged = default;
		try
		{
			staged = Gpu.CreateBuffer( new Borealis.Gpu.BufferInfo { Size = texture.StagedBytes, Usage = Borealis.Gpu.BufferUsage.CopySource, Memory = Borealis.Gpu.MemoryLocation.Upload, Name = "Staged texture" } );
			texture.Stage( Gpu, staged );
			return staged;
		}
		catch ( Borealis.Gpu.GpuException )
		{
			// Written where it's made instead
			if ( staged.IsValid ) Gpu.Destroy( staged );
			return default;
		}
	}

	void DestroyAhead( (TextureFile File, Borealis.Gpu.TextureHandle Image, Borealis.Gpu.BufferHandle Staged) ahead )
	{
		Gpu.Destroy( ahead.Image );
		if ( ahead.Staged.IsValid ) Gpu.Destroy( ahead.Staged );
	}
}
