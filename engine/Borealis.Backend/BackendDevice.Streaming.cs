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

	/// <summary>Main thread time each frame may spend making models whose files have been read (<see cref="LoadModelAsync"/>).</summary>
	public double LoadBudgetMs { get; set; } = 6;

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

	ulong pendingFrame;

	/// <summary>
	/// A model as <see cref="LoadModel"/> makes it (every mesh group in <paramref name="meshGroups"/>, the default material
	/// group), without waiting: while its files are read on a worker, or once this frame's <see cref="LoadBudgetMs"/> is
	/// spent, it's null with <paramref name="pending"/> set; ask again later. Null without pending when there's no compiled
	/// model. Call on one thread at a time.
	/// </summary>
	public RenderMesh LoadModelAsync( string path, ulong meshGroups, out bool pending )
	{
		pending = false;
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( streamedModels.TryGetValue( (path, meshGroups), out var made ) ) return made;

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
		using ( Gpu.DeferUploads() )
			made = read.IsCompletedSuccessfully && read.Result is { } file ? BackendModel.Create( this, file, meshGroups ) : null;
		streamedModels[(path, meshGroups)] = made;
		return made;
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
			budgetEnd = now + (long)(LoadBudgetMs * Stopwatch.Frequency / 1000);
			budgetBytes = Gpu.DeferredUploadBytes + LoadUploadBytes;
		}

		return now <= budgetEnd && Gpu.DeferredUploadBytes < budgetBytes;
	}

	readonly ConcurrentDictionary<string, Task> textureReads = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A texture as <see cref="LoadTexture"/> makes it, without waiting: read and decoded on a worker, then made within
	/// <see cref="LoadBudgetMs"/>; null with <paramref name="pending"/> set until then. Call on one thread at a time.
	/// </summary>
	public GpuTexture LoadTextureAsync( string path, out bool pending )
	{
		pending = false;
		if ( string.IsNullOrEmpty( path ) ) return null;
		lock ( loadedTextures )
		{
			if ( loadedTextures.TryGetValue( path, out var loaded ) ) return loaded;
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
			return LoadTexture( path );
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
				return ReadModelAhead( path );
			}
			finally
			{
				Interlocked.Decrement( ref readsPending );
			}
		} );
	}

	// On a worker: the model's file, and its materials' files and textures, read and decoded for the thread that makes them
	ModelFile ReadModelAhead( string path )
	{
		if ( Content.TryRead( path ) is not { } data ) return null;
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

		// Its shader too: the first load of one reads a large file, and a package's may recompile
		if ( !string.IsNullOrEmpty( file.Shader ) ) Shaders.Load( file.Shader );
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
