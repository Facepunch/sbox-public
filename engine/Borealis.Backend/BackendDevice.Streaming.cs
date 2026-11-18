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

	internal TextureStreamer Streamer => streamer ??= new TextureStreamer( this );

	internal override bool StreamsTextures => StreamTextures;

	internal override void AddTextureSizes( ITextureSizeSource source )
	{
		if ( StreamTextures ) Streamer.AddSource( source );
	}

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

	/// <summary>Models still being read on workers for <see cref="LoadModelAsync"/>.</summary>
	public int ModelReadsPending => Volatile.Read( ref readsPending );

	/// <summary>
	/// Whether models are still loading: being read, or asked for in the last two frames and not made yet. What measures or
	/// compares frames waits until it's false.
	/// </summary>
	public bool LoadingModels => ModelReadsPending > 0 || pendingFrame + 2 > loadFrame;

	/// <summary>The time between the last two frames' starts, for the load budget.</summary>
	void TimeFrame()
	{
		var now = Stopwatch.GetTimestamp();
		lastFrameTicks = lastFrameStart == 0 ? 0 : now - lastFrameStart;
		lastFrameStart = now;
	}

	ulong pendingFrame;

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
			lock ( modelLives ) modelLives[made] = new ModelLife { Key = (path, meshGroups), Pinned = pin };
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
		public int Holders;
		public bool Pinned;
		public long ReleasedAt;
	}

	readonly Dictionary<RenderMesh, ModelLife> modelLives = new( ReferenceEqualityComparer.Instance );
	readonly List<ModelLife> unheldModels = new();
	readonly List<RenderMesh> freeing = new();

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
		lock ( modelLives ) modelLives.TryAdd( mesh, new ModelLife { Mesh = mesh } );
	}

	void PinModel( RenderMesh mesh )
	{
		lock ( modelLives )
		{
			if ( modelLives.TryGetValue( mesh, out var life ) ) life.Pinned = true;
		}
	}

	/// <summary>
	/// At a frame's start: free the models nothing has held for <see cref="ModelGraceSeconds"/> (the next ask loads them
	/// again), and the GPU-driven pages all of whose meshes have gone.
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
						unheldModels.RemoveAt( i );
						continue;
					}

					if ( life.ReleasedAt > due ) continue;
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
					}
				}
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

		gpuDrivenTables?.FreeEmptyPages();
	}

	/// <summary>
	/// Start reading a model made at run time, which has no file: <paramref name="save"/> makes its compiled bytes
	/// (<c>Model.SaveToVmdl</c>) on a worker, where they're read with its materials and textures, for <see cref="MakeModel"/>.
	/// </summary>
	public Task<ModelFile> ReadModelAsync( Func<byte[]> save )
	{
		Interlocked.Increment( ref readsPending );
		return Task.Run( () =>
		{
			try
			{
				using var zone = Zones.ModelRead.Start();
				return ReadModelAhead( save() );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
			}
		} );
	}

	/// <summary>
	/// A model <see cref="ReadModelAsync"/> read, made as <see cref="LoadModelAsync"/> makes one: null with
	/// <paramref name="pending"/> set while it's read or once this frame's budget is spent; ask again later. Null without
	/// pending when it couldn't be read. Call on one thread at a time.
	/// </summary>
	public RenderMesh MakeModel( Task<ModelFile> read, ulong meshGroups, bool drawFastPath, out bool pending )
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
			return BackendModel.Create( this, file, meshGroups, drawFastPath: drawFastPath );
	}

	/// <summary>
	/// Whether this frame's <see cref="LoadBudgetMs"/> and <see cref="LoadUploadBytes"/> have room left; they start with the
	/// first thing made in the frame.
	/// </summary>
	bool WithinBudget()
	{
		var now = Stopwatch.GetTimestamp();
		if ( budgetFrame != loadFrame )
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
		return Task.Run( () =>
		{
			try
			{
				ReadTextureAhead( path );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
			}
		} );
	}

	/// <summary>Whether a texture has been read for <see cref="LoadTextureAsync"/> (or isn't being), so asking again can make it.</summary>
	public bool TextureRead( string path ) => !textureReads.TryGetValue( path, out var read ) || read.IsCompleted;

	/// <summary>Whether a model's files have been read for <see cref="LoadModelAsync"/>, so asking again can make it.</summary>
	public bool ModelRead( string path ) => modelReads.TryGetValue( path, out var read ) && read.IsCompleted;

	Task<ModelFile> StartRead( string path )
	{
		Interlocked.Increment( ref readsPending );
		return Task.Run( () =>
		{
			try
			{
				using var zone = Zones.ModelRead.Start();
				return ReadModelAhead( path );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
			}
		} );
	}

	// On a worker: the model's file, and its materials' files and textures, read and decoded for the thread that makes them
	ModelFile ReadModelAhead( string path ) => Content.TryRead( path ) is { } data ? ReadModelAhead( data ) : null;

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

		if ( Content.TryRead( path ) is not { } data || ReadMaterial( data ) is not { } file ) return;
		readMaterials.TryAdd( path, file );

		// Its shader too: the first load of one reads a large file, and a package's may recompile; and the render state its
		// stencil check reads (a pass module's preprocessed lines)
		if ( !string.IsNullOrEmpty( file.Shader ) ) BackendMaterial.ReadStencilAhead( Shaders, Shaders.Load( file.Shader ), file );
		foreach ( var (_, texture) in file.Textures ) ReadTextureAhead( texture, tail: StreamTextures );
	}

	void ReadTextureAhead( string path, bool tail = false )
	{
		if ( string.IsNullOrEmpty( path ) || readTextures.ContainsKey( path ) ) return;
		lock ( loadedTextures )
		{
			if ( loadedTextures.ContainsKey( path ) ) return;
		}

		try
		{
			// A streamed texture's tail, unless it doesn't stream
			var texture = tail ? TextureStreamer.ReadMips( Content, path, TextureFile.Tail ) : null;
			if ( texture is null || (texture.TopMip > 0 && !TextureStreamer.Streams( texture )) ) texture = Content.TryRead( path ) is { } data ? TextureFile.Read( data ) : null;
			if ( texture is not null ) readTextures.TryAdd( path, texture );
		}
		catch ( InvalidDataException )
		{
			// Read again where it's made, which reports it
		}
	}
}
