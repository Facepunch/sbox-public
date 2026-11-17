using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GpuCommandList = Borealis.Gpu.CommandList;
using GpuDevice = Borealis.Gpu.Device;
using GpuFormat = Borealis.Gpu.Format;
using Num = System.Numerics;
using ProgramType = Borealis.Source2.Shaders.ShaderProgramType;
using RenderViewport = NativeEngine.RenderViewport;

namespace Sandbox;

/// <summary>
/// <see cref="Graphics"/> on a Borealis.Gpu device of its own: render blocks with no view (<see cref="Graphics.Scope.CreateOn"/>)
/// record into one of the device's command lists through Borealis.Backend's <see cref="GraphicsContext"/>, which tracks
/// layouts and opens rendering scopes itself, and draw with the engine's compiled shaders through
/// <see cref="ShaderDrawer"/>, bound by attribute name from managed attributes. What <c>Painter.Begin( Texture )</c>
/// needs runs here: CPU-vertex and instanced draws, clears, render targets, frame grabs, gaussian mips and compute; draws
/// from vertex and index buffers, direct or indirect, and models drawn with transforms, an instance count or indirect
/// arguments (BorealisGpuRenderBackend.Draws.cs); a mirrored scene object drawn again (<see cref="DrawObject"/>).
/// The rest throws <see cref="NotSupportedException"/> (docs/managed/borealis-gpu-path.md).
/// </summary>
/// <remarks>
/// Engine resources get a backing on this device the first time it uses them. Render targets are made empty with the
/// texture's size, format and mips; other textures are copied from Source 2's pixels (mip 0); buffers made while
/// recording for this device start empty, and others start with what Source 2's copy holds. A backing lives until its
/// resource is disposed, or until this backend is.
/// </remarks>
internal sealed partial class BorealisGpuRenderBackend : RenderBackend, IDisposable
{
	readonly GpuDevice gpu;
	readonly BackendDevice services;
	readonly ContentFiles files;
	readonly ShaderDrawer drawer;
	readonly GraphicsContext context;
	readonly bool ownsDevice;
	readonly IntPtr uploadKey;
	static int nextKey;

	readonly ConditionalWeakTable<Texture, Backing<TextureHandle>> textures = new();
	readonly ConditionalWeakTable<GpuBuffer, Backing<BufferHandle>> buffers = new();
	// Backings made here (not borrowed): destroyed when their texture is released, or collected without being released
	readonly HashSet<TextureHandle> textureOwners = new();

	// Owned backings whose texture or buffer was collected without being released (their finalizers), freed at the next frame
	readonly System.Collections.Concurrent.ConcurrentQueue<TextureHandle> collectedTextures = new();
	readonly System.Collections.Concurrent.ConcurrentQueue<BufferHandle> collectedBuffers = new();

	/// <summary>Owned backings freed after their texture or buffer was collected without being released, for soaks and tests.</summary>
	internal int CollectedBackingsFreed { get; private set; }
	readonly HashSet<BufferHandle> ownedBuffers = new();
	readonly Dictionary<SamplerState, (SamplerHandle Sampler, int Index)> samplers = new();
	readonly Dictionary<string, ShaderFile> shaders = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<uint, string> names = new();
	readonly ShaderAttributes lowered = new();

	GpuCommandList list;
	GpuCommandList lendNext;
	bool lent;
	readonly BufferHandle pipelineTextures;

	// SetRenderState's material, for the draw after it
	Material pendingMaterial;
	ShaderFile pendingShader;
	RenderAttributes pendingAttributes;

	sealed class Backing<T>( T handle )
	{
		public T Handle = handle;

		/// <summary>
		/// Set for a backing made here: where its handle goes when its engine resource is collected without being released (a
		/// GpuBuffer's or Texture's finalizer doesn't reach this backend), so the device memory goes with it.
		/// </summary>
		public System.Collections.Concurrent.ConcurrentQueue<T> Collected;

		~Backing() => Collected?.Enqueue( Handle );
	}

	/// <summary>Free owned backings whose engine textures and buffers were collected without being released.</summary>
	void FreeCollectedBackings()
	{
		while ( collectedBuffers.TryDequeue( out var buffer ) )
		{
			if ( !ownedBuffers.Remove( buffer ) ) continue;
			gpu.Destroy( buffer );
			CollectedBackingsFreed++;
		}

		if ( collectedTextures.IsEmpty ) return;
		lock ( textureOwners )
		{
			while ( collectedTextures.TryDequeue( out var texture ) )
			{
				if ( !textureOwners.Remove( texture ) ) continue;
				context.Forget( texture );
				gpu.Destroy( texture );
				CollectedBackingsFreed++;
			}
		}
	}

	/// <summary>
	/// A backend on a device of its own, introduced to the driver with Source 2's application and engine names so it gets
	/// the same driver profile (AMD's changes trilinear filtering otherwise). <paramref name="source2Names"/> false uses
	/// Borealis's, to show the difference. <paramref name="presentation"/> lets it present to windows; with
	/// <paramref name="frames"/> the first block of each engine frame starts the device's next frame, as the process's
	/// device does. <paramref name="markers"/> writes progress markers for the device-loss report
	/// (<see cref="DeviceOptions.ProgressMarkers"/>). <paramref name="pipelineCacheFolder"/> keeps the device's pipeline
	/// cache there (<see cref="DeviceOptions.PipelineCacheFolder"/>); with <paramref name="frames"/>, it's saved in the
	/// background every <see cref="PipelineCacheSaveFrames"/> frames when pipelines were made.
	/// </summary>
	public static BorealisGpuRenderBackend Create( bool validation = false, Action<DeviceMessage> message = null, bool source2Names = true, bool presentation = false, bool frames = false, bool markers = false, string pipelineCacheFolder = null )
	{
		var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = source2Names ? "Sbox" : "Borealis",
			EngineName = source2Names ? "Source2" : "Borealis",
			Validation = validation,
			Presentation = presentation,
			ProgressMarkers = markers,
			PipelineCacheFolder = pipelineCacheFolder,
			Message = message,
		} );

		gpu.Profiler = new TraceZones();
		return new BorealisGpuRenderBackend( gpu, ownsDevice: true ) { followsFrames = frames };
	}

	/// <summary>The device's own zones (pipeline and shader module creation) in the engine's profiler, while it records.</summary>
	sealed class TraceZones : IDeviceProfiler
	{
		public unsafe void BeginZone( nint name, uint color )
		{
			if ( NativeEngine.PerformanceTrace.__N.PerformanceTrace_BeginEvent != null ) NativeEngine.PerformanceTrace.BeginEvent( name, null, color, null, 0, null );
		}

		public unsafe void EndZone()
		{
			if ( NativeEngine.PerformanceTrace.__N.PerformanceTrace_BeginEvent != null ) NativeEngine.PerformanceTrace.EndEvent();
		}
	}

	/// <summary>A device beside Source 2's, logging its messages, for comparing the two in one process.</summary>
	public static BorealisGpuRenderBackend CreateBeside( bool validation ) => Create( validation, m => Log.Info( $"Borealis.Gpu: {m.Severity} {m.Text}" ) );

	/// <summary>Validation errors the device has reported.</summary>
	public int ValidationErrors => gpu.ValidationErrors;

	bool followsFrames;
	ulong frame;

	/// <summary>
	/// Each engine frame is a device frame: starting one waits for the frame before last, then recycles what it used. Called
	/// before anything records in an engine frame: a block, an acquire, a scene render (which can come before either).
	/// </summary>
	internal void FollowFrame()
	{
		// Never inside a Borealis frame's recording (a block it lends): its lists would be left in the frame before
		if ( !followsFrames || frame == Application.FrameCount || lendNext is not null ) return;
		frame = Application.FrameCount;
		BorealisGpuDeviceLost.Check( gpu );
		SubmitUploads();
		FreeModelGeometry();
		FreeCollectedBackings();
		services.BeginFrame();
		TimeFrame();
		if ( frame % PipelineCacheSaveFrames == 0 ) SavePipelineCacheInBackground();
	}

	// The GPU's busy time per device frame: a pass timer over every list (Borealis's and the engine's blocks), read back
	// once the GPU is past the frame. Someone else's timer (a lab's, a probe's) replaces it while it's set.
	Borealis.Backend.PassTimer busyTimer;
	readonly List<Borealis.Backend.PassTimer.Timing> busyTimings = new();

	/// <summary>
	/// The GPU's busy time in a recent device frame, in milliseconds: every list's execution added up, the engine's blocks
	/// and Borealis's frames, without the time the GPU waited between them. Null without timestamps. As native's GPU frame time.
	/// </summary>
	public double? GpuBusyMs { get; private set; }

	/// <summary>Which device frame <see cref="GpuBusyMs"/> was (the engine frame it followed).</summary>
	public ulong GpuBusyFrame { get; private set; }

	/// <summary>Frames between reads of <see cref="GpuBusyMs"/>.</summary>
	const int BusyReadFrames = 8;

	void TimeFrame()
	{
		if ( busyTimer is null )
		{
			if ( services.PassTimer is not null || !gpu.Capabilities.Timestamps ) return;
			busyTimer = new Borealis.Backend.PassTimer( gpu );
		}

		services.PassTimer ??= busyTimer;
		if ( services.PassTimer != busyTimer ) return;

		// The frame before last is done (starting this frame waited for it); older ones are read once. Every
		// BusyReadFrames frames: reading the queries back costs ~60 us, and the stats don't need every frame
		var done = (long)frame - (Borealis.Backend.PassTimer.Slots - 1);
		if ( done > 0 && done % BusyReadFrames == 0 && busyTimer.TryRead( done, busyTimings ) )
		{
			var busy = 0.0;
			foreach ( var timing in busyTimings )
			{
				if ( timing.Kind == Borealis.Backend.PassTimer.ScopeKind.List ) busy += timing.Ms;
			}

			GpuBusyMs = busy;
			GpuBusyFrame = (ulong)done;
		}

		busyTimer.BeginFrame( (long)frame );
	}

	/// <summary>Frames between background saves of the pipeline cache.</summary>
	const ulong PipelineCacheSaveFrames = 900;

	int savingPipelineCache;

	/// <summary>Save the device's pipeline cache on a worker if pipelines were made since the last save (and none is saving).</summary>
	void SavePipelineCacheInBackground()
	{
		if ( gpu.PipelineCachePath is null || System.Threading.Interlocked.Exchange( ref savingPipelineCache, 1 ) != 0 ) return;
		System.Threading.Tasks.Task.Run( () =>
		{
			try
			{
				gpu.SavePipelineCache();
			}
			finally
			{
				System.Threading.Volatile.Write( ref savingPipelineCache, 0 );
			}
		} );
	}

	/// <summary>
	/// Where the game's or the menu's mounted files have a compiled file on disk: a game package's models, materials,
	/// textures and shaders, which are named by their hash in the download cache.
	/// </summary>
	static string MountedFile( string path )
	{
		foreach ( var context in (Sandbox.Engine.GlobalContext[])[Sandbox.Engine.GlobalContext.Game, Sandbox.Engine.GlobalContext.Menu] )
		{
			if ( context?.FileMount is not { } mount ) continue;
			try
			{
				if ( mount.FileExists( path ) && mount.GetFullPath( path ) is { } full ) return full;
			}
			catch ( System.IO.IOException )
			{
				// A package mounting or unmounting while a worker looks: not found this time
			}
		}

		return null;
	}

	BorealisGpuRenderBackend( GpuDevice gpu, bool ownsDevice )
	{
		this.gpu = gpu;
		this.ownsDevice = ownsDevice;
		files = ContentFiles.FindGame();
		files.Resolve = MountedFile;
		services = new BackendDevice( gpu, files );
		services.Shaders.Unloaded = message => Log.Warning( $"Borealis.Gpu: {message}" );
		services.Passes.Error = message => Log.Warning( $"Borealis.Gpu: {message}" );
		services.Passes.Info = message => Log.Info( message );
		services.Surfaces.Info = message => Log.Info( message );
		drawer = new ShaderDrawer( services );
		context = new GraphicsContext( gpu, drawer, null );

		// The frame's pipeline textures (ambient occlusion's and reflections' results) by bindless index, 0 for none: what
		// Source 2's scene system keeps per frame (PipelineTextureSlot)
		pipelineTextures = gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage, Name = "Pipeline textures" } );
		gpu.WriteBuffer<uint>( pipelineTextures, [0, 0, 0, 0] );
		gpu.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, pipelineTextures );
		uploadKey = new IntPtr( -1 - System.Threading.Interlocked.Increment( ref nextKey ) );
		Register( this );
	}

	/// <summary>The device everything records on.</summary>
	public GpuDevice Device => gpu;

	/// <summary>The context blocks record through.</summary>
	public GraphicsContext Context => context;

	/// <summary>
	/// The renderer services draws bind through: the device's shaders, placeholders and material objects, shared with
	/// Borealis's scene on this device.
	/// </summary>
	public BackendDevice Services => services;

	/// <summary>Bind through material objects (the default); off binds by reflection every draw, for comparisons.</summary>
	public bool MaterialObjects
	{
		get => drawer.MaterialObjects;
		set => drawer.MaterialObjects = value;
	}

	/// <summary>Material objects made, sets written through them and held now, and draws that fell back to reflection.</summary>
	public (int Objects, int SetsWritten, int SetsHeld, int Fallbacks) MaterialObjectStats
	{
		get
		{
			var (objects, written, held) = drawer.MaterialObjectStats;
			return (objects, written, held, drawer.Fallbacks);
		}
	}

	//
	// Blocks and resources
	//

	public override RenderAttributes BeginBlock()
	{
		if ( list is not null ) throw new InvalidOperationException( "A Borealis.Gpu block is already recording." );

		FollowFrame();
		WriteWorkerUpdates();
		SubmitUploads();
		PollReadbacks();
		lent = lendNext is not null;
		list = lendNext ?? gpu.BeginCommandList( QueueType.Graphics, "Graphics block" );
		lendNext = null;
		presentTo = null;
		context.Begin( list );
		pendingShader = null;
		pendingAttributes = null;

		// Timed with Borealis's lists when a pass timer is on (diagnostics): the engine's own blocks, not lent ones
		if ( !lent && services.PassTimer is { } timer )
		{
			timer.ResetIn( list );
			blockTimer = timer.Begin( list, "Engine block", Borealis.Backend.PassTimer.ScopeKind.List );
		}
		else blockTimer = -1;

		return blockAttributes = RenderAttributes.CreateManaged();
	}

	int blockTimer = -1;

	// The block's attributes, which a draw's own are layered over, as Source 2's contexts layer them
	RenderAttributes blockAttributes;

	/// <summary>
	/// Record the next block into <paramref name="list"/>, which another recorder on the device is part way through (Borealis's
	/// frame, at a camera's stage), and leave it to that recorder to submit: the block's drawing lands at that point of it.
	/// </summary>
	internal void LendNextBlock( GpuCommandList list ) => lendNext = list;

	/// <summary>
	/// Draw with a lent frame's transform buffer as <c>g_TransformBuffer</c> (under the draws' own attributes) and its instance
	/// ids at <paramref name="offset"/> bytes (<see cref="ShaderDrawer.InstanceIds"/>), as a custom object inside Borealis's
	/// frame draws on Source 2. Default handles stop.
	/// </summary>
	internal void LendInstances( BufferHandle transforms, BufferHandle ids, long offset )
	{
		lentTransforms = transforms;
		drawer.InstanceIds = (ids, offset);
	}

	BufferHandle lentTransforms;

	/// <summary>The depth range draws map to (<see cref="ShaderDrawer.DepthRange"/>): a lent frame's, else 0 to 1.</summary>
	internal (float Min, float Max) DepthRange
	{
		get => drawer.DepthRange;
		set => drawer.DepthRange = value;
	}

	/// <summary>
	/// The shader mode draws take by name (<see cref="GraphicsContext.Mode"/>): <c>Depth</c> where a custom object draws into
	/// a shadow map or the depth prepass. Null draws each shader's first mode.
	/// </summary>
	internal string DrawMode
	{
		get => context.Mode;
		set => context.Mode = value;
	}

	/// <summary>The depth bias of draws into depth alone (<see cref="ShaderDrawer.DepthBias"/>): a shadow map's.</summary>
	internal (int Constant, float Slope) DepthBias
	{
		get => drawer.DepthBias;
		set => drawer.DepthBias = value;
	}

	/// <summary>Write a GPU timestamp in this block's list (<see cref="GraphicsContext.WriteTimestamp"/>).</summary>
	internal void WriteTimestamp( TimestampQueries queries, int index, PipelineStage stage, int reset = 0 )
	{
		_ = Recording;
		context.WriteTimestamp( queries, index, stage, reset );
	}


	/// <summary>
	/// Name <paramref name="texture"/> as the frame's pipeline texture in <paramref name="slot"/> (<c>PipelineTextureSlot</c>)
	/// for what draws after, in this block's list, as Source 2's scene system does for ambient occlusion's result.
	/// </summary>
	internal void SetPipelineTexture( int slot, Texture texture )
	{
		_ = Recording;
		Span<uint> index = [(uint)TextureIndex( texture )];
		context.MemoryBarrier();
		context.UpdateBuffer( pipelineTextures, MemoryMarshal.AsBytes( index ), slot * 4 );
	}

	/// <summary>Name no pipeline textures for what draws after, in this block's list: what a frame starts with.</summary>
	internal void ClearPipelineTextures()
	{
		_ = Recording;
		context.MemoryBarrier();
		context.UpdateBuffer( pipelineTextures, new byte[16], 0 );
	}

	public override void EndBlock()
	{
		try
		{
			context.FinishReadable();
			if ( blockTimer >= 0 ) services.PassTimer?.End( list, blockTimer );
			blockTimer = -1;
			SubmittedReadbacks( blockReadbacks, lent ? default : gpu.Submit( list, default, presentTo ) );
		}
		finally
		{
			list = null;
			lent = false;
			presentTo = null;
			pendingAttributes = null;
			pendingMaterial = null;
			pendingLayout = null;
			blockAttributes = null;
			drawer.Material = null;
		}
	}

	SwapChain presentTo;
	readonly Dictionary<SwapChain, ulong> acquiredIn = new();

	/// <summary>
	/// Call before a swap chain acquires: a second acquire in one device frame (a window drawn twice in an engine frame,
	/// from a resize) starts the next device frame, since each frame slot has one acquire semaphore per swap chain.
	/// </summary>
	public void BeforeAcquire( SwapChain swapChain )
	{
		// The frame has to have started before the acquire, so the acquire's semaphore belongs to the frame whose submit
		// waits on it
		FollowFrame();
		if ( acquiredIn.TryGetValue( swapChain, out var last ) && last == gpu.FrameNumber )
		{
			SubmitUploads();
			services.BeginFrame();
		}

		acquiredIn[swapChain] = gpu.FrameNumber;
	}

	/// <summary>Forget a swap chain that's going away.</summary>
	public void Forget( SwapChain swapChain ) => acquiredIn.Remove( swapChain );

	/// <summary>
	/// A swap chain on a window, presenting with vsync or as soon as it can.
	/// </summary>
	public SwapChain CreateSwapChain( SurfaceSource surface, int width, int height, bool vsync ) => gpu.CreateSwapChain( new SwapChainInfo
	{
		Surface = surface,
		Width = width,
		Height = height,
		PresentMode = vsync ? PresentMode.Fifo : PresentMode.Immediate,
		Linear = true,
	} );

	/// <summary>
	/// Copy a texture into the image <paramref name="swapChain"/> acquired, last thing in the block, which then submits for
	/// that image; present it after the block ends.
	/// </summary>
	public void CopyToSwapChain( Texture source, SwapChain swapChain )
	{
		_ = Recording;
		context.CopyToPresent( TextureOf( source ), swapChain.Current );
		presentTo = swapChain;
	}

	/// <summary>
	/// Read a map's compiled content on this device, after the game's: the loose files of the package it's in and its VPK,
	/// with its 3D skybox's beside it, as the engine's search path has them once the map is mounted. Its world geometry,
	/// materials and textures then load here by path.
	/// </summary>
	/// <summary>The last map whose content was added (<see cref="AddMapContent"/>), not counting 3D skyboxes: <c>construct_25</c>.</summary>
	internal string LastMap { get; private set; }

	internal void AddMapContent( string vpk )
	{
		// A map's 3D skybox is a map of its own (<map>_sky), loaded after it: it isn't the map being played
		var mapName = System.IO.Path.GetFileNameWithoutExtension( vpk );
		if ( LastMap is null || !mapName.EndsWith( "_sky", StringComparison.OrdinalIgnoreCase ) ) LastMap = mapName;
		vpk = System.IO.Path.GetFullPath( vpk );
		if ( !mapPackages.Add( vpk ) ) return;

		// <package>/maps/<map>.vpk
		if ( System.IO.Path.GetDirectoryName( System.IO.Path.GetDirectoryName( vpk ) ) is { } root && mapRoots.Add( root ) ) files.Add( root );
		AddPackage( vpk );

		var sky = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( vpk ), System.IO.Path.GetFileNameWithoutExtension( vpk ) + "_sky.vpk" );
		if ( System.IO.File.Exists( sky ) && mapPackages.Add( sky ) ) AddPackage( sky );

		// The map's lightmaps (and its 3D skybox's), read and decoded on a worker while the map loads, not on the frame that
		// first draws it
		var map = System.IO.Path.GetFileNameWithoutExtension( vpk );
		services.ReadMapLightmapsAhead( $"maps/{map}" );
		if ( System.IO.File.Exists( sky ) ) services.ReadMapLightmapsAhead( $"maps/{map}_sky" );

		// The process's video memory as the map's world is made (both devices: Source 2 still loads the map's lightmaps)
		Log.Info( $"Borealis.Gpu memory at {System.IO.Path.GetFileName( vpk )}: {gpu.DescribeMemory()}{(services.DescribeStreaming() is { } streaming ? $"; {streaming}" : "")}" );
	}

	void AddPackage( string vpk )
	{
		var package = Borealis.Source2.Resources.VpkFile.Open( vpk );
		mapVpks.Add( package );
		files.AddPackage( package );
	}

	readonly List<Borealis.Source2.Resources.VpkFile> mapVpks = new();

	readonly HashSet<string> mapPackages = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> mapRoots = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Wait until everything submitted has finished.</summary>
	public void WaitIdle() => gpu.WaitIdle();

	public override IntPtr UploadContext => uploadKey;

	public override int TextureIndex( Texture texture ) => gpu.BindlessIndex( TextureOf( texture ) );

	public override int SamplerIndex( SamplerState state )
	{
		if ( samplers.TryGetValue( state, out var made ) ) return made.Index;

		var info = state.Filter switch
		{
			FilterMode.Point => SamplerInfo.Point(),
			FilterMode.Bilinear => SamplerInfo.Linear() with { MipFilter = Filter.Nearest },
			FilterMode.Anisotropic => SamplerInfo.Linear( anisotropy: state.MaxAnisotropy ),
			_ => SamplerInfo.Linear(),
		};
		info.AddressU = Address( state.AddressModeU );
		info.AddressV = Address( state.AddressModeV );
		info.AddressW = Address( state.AddressModeW );
		info.MaxLod = 15;

		var sampler = gpu.CreateSampler( info );
		made = (sampler, gpu.SamplerIndex( sampler ));
		samplers[state] = made;
		return made.Index;
	}

	static AddressMode Address( TextureAddressMode mode ) => mode switch
	{
		TextureAddressMode.Mirror or TextureAddressMode.MirrorOnce => AddressMode.Mirror,
		TextureAddressMode.Clamp => AddressMode.Clamp,
		TextureAddressMode.Border => AddressMode.Border,
		_ => AddressMode.Wrap,
	};

	public override void Release( object resource )
	{
		switch ( resource )
		{
			case Texture texture when textures.TryGetValue( texture, out var backing ):
				lock ( textureOwners )
				{
					textures.Remove( texture );
					context.Forget( backing.Handle );
					backing.Collected = null;

					// A borrowed backing (a Borealis frame's target) stays its owner's
					if ( textureOwners.Remove( backing.Handle ) ) gpu.Destroy( backing.Handle );
				}

				break;

			case GpuBuffer buffer when buffers.TryGetValue( buffer, out var backing ):
				// Writes waiting for the next block may name it: submit them first, so it's freed after they've run
				SubmitUploads();
				ForgetCounter( buffer );
				buffers.Remove( buffer );
				backing.Collected = null;
				ownedBuffers.Remove( backing.Handle );
				gpu.Destroy( backing.Handle );
				break;

			case GpuBuffer buffer:
				ForgetCounter( buffer );
				break;
		}
	}

	/// <summary>
	/// Make <paramref name="handle"/>, a texture on this device that something else owns (a Borealis frame's target, now in
	/// <paramref name="layout"/>), the backing of <paramref name="texture"/>: drawing with the engine texture draws with it,
	/// and it isn't destroyed with the engine texture or this backend. A backing the texture had is released first.
	/// </summary>
	internal void Borrow( Texture texture, TextureHandle handle, ImageLayout layout )
	{
		if ( textures.TryGetValue( texture, out var backing ) )
		{
			if ( backing.Handle == handle )
			{
				context.Track( handle, layout );
				return;
			}

			Release( texture );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) );
		context.Track( handle, layout );
	}

	/// <summary>The layout an engine texture's backing is in, as the recording left it.</summary>
	internal ImageLayout LayoutOf( Texture texture ) => textures.TryGetValue( texture, out var backing ) ? context.LayoutOf( backing.Handle ) : ImageLayout.Undefined;

	/// <summary>How many textures were copied from Source 2's pixels, having nothing of their own to start from.</summary>
	public int CopiedFromSource2 { get; private set; }

	/// <summary>
	/// A texture's backing. A render target is made to match, cleared as Source 2 clears a new one. Anything else comes
	/// from its compiled file through Borealis.Source2, or from the pixels it was made with, as RGBA8; failing both (a
	/// texture changed since, or made before this device), from mip 0 of Source 2's copy. A material's texture with no
	/// path of its own (a sky's cubemap) is the material's, as Borealis's draws read it.
	/// </summary>
	internal TextureHandle TextureOf( Texture texture )
	{
		ArgumentNullException.ThrowIfNull( texture );
		if ( textures.TryGetValue( texture, out var backing ) ) return backing.Handle;

		// Looked up each time rather than kept: streaming swaps its image
		if ( MaterialTextureOf( texture ) is { } material ) return material.Handle;

		// Made once, under a lock: a sprite batch's workers ask for their sprites' indices (Texture.Index) in parallel
		// while the main thread waits for them
		lock ( textureOwners )
		{
			// No description: a texture that failed to load, which Source 2 draws with its error texture (and without its
			// device has no description of at all)
			if ( texture.Width <= 0 || texture.Height <= 0 ) return ErrorTexture();
			return textures.TryGetValue( texture, out backing ) ? backing.Handle : Make( texture );
		}
	}

	/// <summary>A render target's samples as its multisampling asks: what Vulkan has (6x is 4x, 16x is 8x).</summary>
	static int SamplesOf( NativeEngine.RenderMultisampleType type ) => type switch
	{
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_2X => 2,
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_4X or NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_6X => 4,
		NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_8X or NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_16X => 8,
		_ => 1,
	};

	TextureHandle Make( Texture texture )
	{
		TextureHandle handle;
		if ( texture.IsRenderTarget )
		{
			// Its samples (a multisampled one is still sampled, as Texture2DMS) and an array's layers, as it was made
			var samples = SamplesOf( texture.MultisampleType );
			var info = TextureInfo.RenderTarget( texture.Width, texture.Height, FormatOf( texture.ImageFormat ), samples, $"Engine target {texture.Width}x{texture.Height}" );
			info.Usage |= TextureUsage.Sampled;
			info.Mips = samples > 1 ? 1 : Math.Max( texture.Mips, 1 );
			if ( texture.Desc is { IsArray: true, IsCube: false } desc ) info.Layers = Math.Max( (int)desc.m_nDepth, 1 );
			var depth = info.Format.IsDepth();
			if ( !depth && samples == 1 ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );

			// A target made with pixels - an image whose mips Source 2 generates, or one made for compute - starts with them
			if ( samples == 1 && info.Layers == 1 && (InitialPixels( texture, info.Format ) ?? (RawPixels( texture ) is { } raw && raw.Format == info.Format ? raw.Data : null)) is { } pixels )
			{
				Upload( handle, info, pixels, ImageLayout.Undefined );
			}
			else
			{
				context.Track( handle, ImageLayout.Undefined );
				if ( !depth && texture.InitialColor is { } clear ) ClearNow( handle, clear );
			}
		}
		else if ( FromFile( texture ) is { } file )
		{
			handle = file;
		}
		else if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE ) )
		{
			handle = VolumeOf( texture );
		}
		else if ( InitialPixels( texture, GpuFormat.RGBA8Unorm ) is { } pixels )
		{
			// With its mips, as Source 2 generates them from the first
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			handle = gpu.CreateTexture( info );
			Upload( handle, info, pixels, ImageLayout.Undefined );
		}
		else if ( RawPixels( texture ) is { } raw )
		{
			// Another format made with its first mip's data (float images, lookup tables): the same bytes, and storage
			// when it was made for compute
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, raw.Format, Math.Max( texture.Mips, 1 ), $"Engine image {texture.Width}x{texture.Height}" );
			if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			Upload( handle, info, raw.Data, ImageLayout.Undefined );
		}
		else if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) && texture.Depth <= 1 && DataFormatOf( texture.ImageFormat ) is var storage && storage != GpuFormat.Unknown )
		{
			// Made empty for compute to fill (a terrain's baked normals): its own format, storage, cleared as a new target is
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, storage, Math.Max( texture.Mips, 1 ), $"Engine storage {texture.Width}x{texture.Height}" );
			info.Usage |= TextureUsage.Storage;
			handle = gpu.CreateTexture( info );
			context.Track( handle, ImageLayout.Undefined );
			ClearNow( handle, Color.Transparent );
		}
		else if ( TryCopyFromSource2( texture ) is { } copy )
		{
			var info = TextureInfo.Texture2D( texture.Width, texture.Height, GpuFormat.RGBA8Unorm, name: $"Engine image {texture.Width}x{texture.Height}" );
			handle = gpu.CreateTexture( info );
			Upload( handle, info, copy, ImageLayout.Undefined );
		}
		else
		{
			handle = Unreadable( texture );
		}

		textures.Add( texture, new Backing<TextureHandle>( handle ) { Collected = collectedTextures } );
		textureOwners.Add( handle );
		return handle;
	}

	/// <summary>
	/// A volume (<see cref="Texture.CreateVolume"/>) as a 3D texture in its own format, storage when made for compute, with the
	/// pixels it was made with, or cleared. As a 2D texture, a compute shader's storage binding of it had no view and its
	/// sampled binding the wrong shape.
	/// </summary>
	TextureHandle VolumeOf( Texture texture )
	{
		var format = DataFormatOf( texture.ImageFormat );
		var known = format != GpuFormat.Unknown;
		var info = new TextureInfo
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = Math.Max( texture.Depth, 1 ),
			Layers = 1,
			Mips = 1,
			Samples = 1,
			Dimension = TextureDimension.Texture3D,
			Format = known ? format : GpuFormat.RGBA8Unorm,
			Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
			Name = $"Engine volume {texture.Width}x{texture.Height}x{texture.Depth}",
		};
		if ( texture.Desc.m_nFlags.Contains( NativeEngine.RuntimeTextureSpecificationFlags.TSPEC_UAV ) ) info.Usage |= TextureUsage.Storage;

		var handle = gpu.CreateTexture( info );
		var size = (long)info.Width * info.Height * info.Depth * info.Format.BytesPerBlock();
		if ( known && texture.InitialPixels is { } data && data.Length >= size )
		{
			Upload( handle, info, data.AsSpan( 0, (int)size ), ImageLayout.Undefined );
		}
		else
		{
			context.Track( handle, ImageLayout.Undefined );
			ClearNow( handle, Color.Transparent );
		}

		return handle;
	}

	TextureHandle errorTexture;

	/// <summary>Source 2's error texture: a 32x32 magenta and black checkerboard of 4 pixel squares.</summary>
	TextureHandle ErrorTexture()
	{
		if ( errorTexture.IsValid ) return errorTexture;

		const int size = 32;
		var pixels = new byte[size * size * 4];
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var magenta = ((x & 4) ^ (y & 4)) != 0;
				pixels[(y * size + x) * 4] = magenta ? (byte)255 : (byte)0;
				pixels[(y * size + x) * 4 + 2] = magenta ? (byte)255 : (byte)0;
				pixels[(y * size + x) * 4 + 3] = 255;
			}
		}

		var info = TextureInfo.Texture2D( size, size, GpuFormat.RGBA8Unorm, name: "Engine error texture" );
		errorTexture = gpu.CreateTexture( info );
		Upload( errorTexture, info, pixels, ImageLayout.Undefined );
		return errorTexture;
	}

	/// <summary>
	/// The material's own texture for one read from a material (<see cref="Material.GetTexture"/>) with no path of its
	/// own, or null.
	/// </summary>
	BackendTexture MaterialTextureOf( Texture texture )
	{
		if ( !string.IsNullOrEmpty( texture.ResourcePath ) ) return null;
		if ( !Texture.MaterialOrigins.TryGetValue( texture, out var origin ) || origin is not Texture.MaterialOrigin o ) return null;
		return services.MaterialTexture( o.Material, o.Parameter ) as BackendTexture;
	}

	/// <summary>A texture loaded from a compiled <c>.vtex</c>, read from that file.</summary>
	TextureHandle? FromFile( Texture texture )
	{
		var path = texture.ResourcePath;
		if ( string.IsNullOrEmpty( path ) || !path.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) ) return null;
		if ( files.Find( path ) is not { } file ) return null;

		try
		{
			var handle = TextureFile.Load( file ).Create( gpu, path, UploadList() );
			context.Track( handle, ImageLayout.ShaderRead );
			return handle;
		}
		catch ( NotSupportedException )
		{
			return null;
		}
	}

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, in <paramref name="format"/> (RGBA8 or BGRA8), when it was made in one
	/// of those.
	/// </summary>
	static byte[] InitialPixels( Texture texture, GpuFormat format )
	{
		if ( texture.InitialPixels is not { } data ) return null;
		if ( texture.ImageFormat is not (ImageFormat.RGBA8888 or ImageFormat.BGRA8888) ) return null;

		var size = texture.Width * texture.Height * 4;
		if ( data.Length < size ) return null;

		var pixels = data.Length == size ? data : data[..size];
		var swap = (texture.ImageFormat == ImageFormat.BGRA8888) != (format == GpuFormat.BGRA8Unorm);
		if ( !swap ) return pixels;

		pixels = pixels == data ? (byte[])data.Clone() : pixels;
		for ( int i = 0; i < size; i += 4 ) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
		return pixels;
	}

	/// <summary>
	/// Mip 0 of the pixels a texture was made with, as they are, when its format has a Borealis.Gpu form and they're at
	/// least one mip's worth.
	/// </summary>
	static (GpuFormat Format, byte[] Data)? RawPixels( Texture texture )
	{
		if ( texture.InitialPixels is not { } data || texture.Depth > 1 ) return null;
		var format = DataFormatOf( texture.ImageFormat );
		if ( format == GpuFormat.Unknown ) return null;
		var size = texture.Width * texture.Height * format.BytesPerBlock();
		if ( data.Length < size ) return null;
		return (format, data.Length == size ? data : data[..size]);
	}

	/// <summary>The Borealis.Gpu form of an uncompressed engine format, or Unknown.</summary>
	static GpuFormat DataFormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.RGBA8888 or ImageFormat.RGBX8888 or ImageFormat.RGBA8888_LINEAR or ImageFormat.LINEAR_RGBA8888 => GpuFormat.RGBA8Unorm,
		ImageFormat.BGRA8888 or ImageFormat.BGRX8888 or ImageFormat.BGRA8888_LINEAR or ImageFormat.BGRX8888_LINEAR or ImageFormat.LINEAR_BGRA8888 or ImageFormat.LINEAR_BGRX8888 => GpuFormat.BGRA8Unorm,
		ImageFormat.A8 or ImageFormat.I8 or ImageFormat.LINEAR_I8 => GpuFormat.R8Unorm,
		ImageFormat.IA88 => GpuFormat.RG8Unorm,
		ImageFormat.RGBA16161616 or ImageFormat.LINEAR_RGBA16161616 => GpuFormat.RGBA16Unorm,
		ImageFormat.BGRA1010102 => GpuFormat.BGR10A2Unorm,
		ImageFormat.RG3232 => GpuFormat.RG32Uint,
		ImageFormat.RGBA32323232 => GpuFormat.RGBA32Uint,
		ImageFormat.R32F => GpuFormat.R32Float,
		ImageFormat.RG3232F => GpuFormat.RG32Float,
		ImageFormat.RGBA32323232F => GpuFormat.RGBA32Float,
		ImageFormat.RGBA16161616F => GpuFormat.RGBA16Float,
		ImageFormat.RG1616F => GpuFormat.RG16Float,
		ImageFormat.I16F or ImageFormat.R16F => GpuFormat.R16Float,
		ImageFormat.R11G11B10F => GpuFormat.RG11B10Float,
		ImageFormat.RGBA1010102 => GpuFormat.RGB10A2Unorm,
		ImageFormat.R16 => GpuFormat.R16Unorm,
		ImageFormat.RG1616 => GpuFormat.RG16Unorm,
		ImageFormat.R32_UINT => GpuFormat.R32Uint,
		_ => GpuFormat.Unknown,
	};

	/// <summary>Mip 0 of Source 2's copy as RGBA8, or null when Source 2 can't read it back.</summary>
	unsafe byte[] TryCopyFromSource2( Texture texture )
	{
		try
		{
			var pixels = new byte[texture.Width * texture.Height * 4];
			var rect = new NativeRect( 0, 0, texture.Width, texture.Height );
			fixed ( byte* data = pixels ) Source2RenderBackend.Instance.ReadPixels( texture, rect, 0, 0, rect, (IntPtr)data, ImageFormat.RGBA8888, 0 );
			CopiedFromSource2++;
			return pixels;
		}
		catch ( Exception )
		{
			// A readback Source 2 can't do
			return null;
		}
	}

	/// <summary>
	/// A texture with nothing to make it from (pixels Source 2 can't read back): one transparent pixel, kept
	/// as its backing so it isn't made again each time it's asked for, and said once.
	/// </summary>
	TextureHandle Unreadable( Texture texture )
	{
		Log.Warning( $"Borealis.Gpu: {(string.IsNullOrEmpty( texture.ResourcePath ) ? $"a {texture.Width}x{texture.Height} {texture.ImageFormat} texture" : texture.ResourcePath)} has nothing to read, so it draws transparent" );
		var handle = gpu.CreateTexture( TextureInfo.Texture2D( 1, 1, GpuFormat.RGBA8Unorm, name: "Engine image without pixels" ) );
		context.Track( handle, ImageLayout.Undefined );
		ClearNow( handle, Color.Transparent );
		return handle;
	}

	/// <summary>
	/// Write mip 0 of a texture (now in <paramref name="from"/>) and fill the rest from it, recorded where uploads go -
	/// the block being recorded, or the list of uploads submitted before the next block - rather than submitted and
	/// waited for. The texture ends in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void Upload( TextureHandle handle, in TextureInfo info, ReadOnlySpan<byte> pixels, ImageLayout from, int x = 0, int y = 0, int width = 0, int height = 0 )
	{
		var target = UploadList();
		target.Barrier( new TextureBarrier
		{
			Texture = handle,
			OldLayout = from,
			NewLayout = ImageLayout.CopyDestination,
			SourceStage = PipelineStage.AllCommands,
			SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
			DestinationStage = PipelineStage.Transfer,
			DestinationAccess = MemoryAccess.TransferWrite,
		} );

		target.UpdateTexture( handle, pixels, 0, 0, width > 0 ? width : info.Width, height > 0 ? height : info.Height, x, y );
		target.Barrier( MipBarrier( handle, 0, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		if ( info.Mips > 1 ) FillMips( target, handle, info, ImageLayout.CopyDestination );
		context.Track( handle, ImageLayout.ShaderRead );
		TextureUploads++;
	}

	/// <summary>Texture writes recorded rather than submitted and waited for, for tests.</summary>
	public int TextureUploads { get; private set; }

	/// <summary>
	/// Where a write is recorded: the block being recorded (outside any rendering scope), or the list of uploads
	/// submitted before the next one.
	/// </summary>
	GpuCommandList UploadList()
	{
		if ( list is not null )
		{
			context.MemoryBarrier();
			return list;
		}

		if ( uploads is null )
		{
			FollowFrame();
			uploads = gpu.BeginCommandList( QueueType.Graphics, "Engine uploads" );
			written.Clear();
		}

		return uploads;
	}

	/// <summary>
	/// Fill mips 1 and down of a texture whose mip 0 is in <see cref="ImageLayout.ShaderRead"/> (and the rest in
	/// <paramref name="rest"/>) by halving each into the next with a linear blit (a box filter), as a texture made with
	/// only its first mip gets the rest. Leaves it all in <see cref="ImageLayout.ShaderRead"/>.
	/// </summary>
	void FillMips( GpuCommandList mips, TextureHandle handle, in TextureInfo info, ImageLayout rest )
	{
		var filter = info.Format is GpuFormat.R32Float or GpuFormat.RG32Float or GpuFormat.RGBA32Float ? Filter.Nearest : Filter.Linear;
		for ( int i = 1; i < info.Mips; i++ )
		{
			int sw = Math.Max( info.Width >> (i - 1), 1 ), sh = Math.Max( info.Height >> (i - 1), 1 );
			int dw = Math.Max( info.Width >> i, 1 ), dh = Math.Max( info.Height >> i, 1 );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.ShaderRead, ImageLayout.CopySource ) );
			mips.Barrier( MipBarrier( handle, i, rest, ImageLayout.CopyDestination ) );
			mips.Blit( handle, (0, 0, sw, sh), handle, (0, 0, dw, dh), filter, i - 1, i );
			mips.Barrier( MipBarrier( handle, i - 1, ImageLayout.CopySource, ImageLayout.ShaderRead ) );
			mips.Barrier( MipBarrier( handle, i, ImageLayout.CopyDestination, ImageLayout.ShaderRead ) );
		}
	}

	/// <summary>
	/// A texture updated on Source 2's device: its copy here takes the same bytes, recorded where uploads go, rather than
	/// being dropped and copied back from Source 2's pixels. Mips past the first are filled again from it. False when the
	/// copy is in another format than the data, or the update is of part of a 3D texture's slices.
	/// </summary>
	public override bool Update( Texture texture, ReadOnlySpan<byte> data, int x, int y, int z, int width, int height, int depth )
	{
		if ( !textures.TryGetValue( texture, out var backing ) ) return true;

		var info = gpu.InfoOf( backing.Handle );
		if ( DataFormatOf( texture.ImageFormat ) != info.Format ) return false;

		// A volume takes every slice at once (CommandList.CopyBufferToTexture), so only an update of all of them
		var slices = info.Dimension == TextureDimension.Texture3D ? info.Depth : 1;
		if ( z != 0 || (depth > 1 && depth != slices) ) return false;

		width = width > 0 ? width : info.Width;
		height = height > 0 ? height : info.Height;
		if ( slices > 1 && (x != 0 || y != 0 || width != info.Width || height != info.Height) ) return false;
		var size = width * height * slices * info.Format.BytesPerBlock();
		if ( data.Length < size ) return false;

		// From a worker (an animated texture decodes and updates its frames on the thread pool): written at the start of the
		// next block, so nothing records into the main thread's lists from another thread
		if ( !ThreadSafe.IsMainThread )
		{
			workerUpdates.Enqueue( (texture, data[..size].ToArray(), x, y, width, height) );
			return true;
		}

		Upload( backing.Handle, info, data[..size], context.LayoutOf( backing.Handle ), x, y, width, height );
		return true;
	}

	readonly System.Collections.Concurrent.ConcurrentQueue<(Texture Texture, byte[] Data, int X, int Y, int Width, int Height)> workerUpdates = new();

	/// <summary>Write the texture updates workers asked for since the last block, before a block starts recording.</summary>
	void WriteWorkerUpdates()
	{
		while ( workerUpdates.TryDequeue( out var update ) )
		{
			if ( !textures.TryGetValue( update.Texture, out var backing ) ) continue;
			Upload( backing.Handle, gpu.InfoOf( backing.Handle ), update.Data, context.LayoutOf( backing.Handle ), update.X, update.Y, update.Width, update.Height );
		}
	}

	static TextureBarrier MipBarrier( TextureHandle texture, int mip, ImageLayout from, ImageLayout to ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = PipelineStage.AllCommands,
		SourceAccess = from == ImageLayout.Undefined ? MemoryAccess.None : MemoryAccess.MemoryWrite,
		DestinationStage = PipelineStage.AllCommands,
		DestinationAccess = MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite,
		BaseMip = mip,
		MipCount = 1,
		LayerCount = 1,
	};

	/// <summary>Clear a new target now, in the block being recorded or on its own.</summary>
	void ClearNow( TextureHandle handle, Color color )
	{
		var value = new Num.Vector4( color.r, color.g, color.b, color.a );
		if ( list is not null )
		{
			context.ClearTexture( handle, value );
			return;
		}

		var clear = gpu.BeginCommandList( QueueType.Graphics, "Clear new target" );
		context.Begin( clear );
		context.ClearTexture( handle, value );
		context.Finish();
		gpu.Submit( clear );
	}

	const string UncompressedTangentFrame = "UncompressedTangentFrame";

	// An A8 target is one channel written and read as red (ambient occlusion's result), as its R8 image holds it. Depth with
	// stencil is 32-bit float depth, as the frame's depth is
	static GpuFormat FormatOf( ImageFormat format ) => format switch
	{
		ImageFormat.D24S8 or ImageFormat.LINEAR_D24S8 or ImageFormat.D24FS8 or ImageFormat.D32FS8 => GpuFormat.D32FloatS8Uint,
		ImageFormat.D32 or ImageFormat.D24X8 => GpuFormat.D32Float,
		ImageFormat.D16 => GpuFormat.D16Unorm,
		_ => format is not ImageFormat.I8 && DataFormatOf( format ) is var mapped && mapped != GpuFormat.Unknown
			? mapped
			: throw new NotSupportedException( $"Render target format {format} on Borealis.Gpu" ),
	};

	/// <summary>
	/// A buffer's backing. One made while recording for another device starts with what Source 2's copy holds.
	/// </summary>
	internal BufferHandle BufferOf( GpuBuffer buffer )
	{
		ArgumentNullException.ThrowIfNull( buffer );
		if ( buffers.TryGetValue( buffer, out var backing ) ) return backing.Handle;

		var size = Math.Max( (long)buffer.ElementCount * buffer.ElementSize, 16 );
		var handle = gpu.CreateBuffer( new BufferInfo
		{
			Size = size,
			Usage = BufferUsage.Storage | BufferUsage.Index | BufferUsage.Vertex | BufferUsage.Indirect | BufferUsage.CopySource | BufferUsage.CopyDestination,
			Name = "Engine buffer",
		} );

		buffers.Add( buffer, new Backing<BufferHandle>( handle ) { Collected = collectedBuffers } );
		ownedBuffers.Add( handle );

		if ( buffer.Origin != this )
		{
			var bytes = new byte[buffer.ElementCount * buffer.ElementSize];
			buffer.GetData<byte>( bytes );
			gpu.WriteBuffer( handle, bytes );
		}

		return handle;
	}

	/// <summary>The compiled shader a material draws with (its shader, without material values).</summary>
	ShaderFile ShaderOf( Material material )
	{
		var path = material.native.GetString( "shader", "" );
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( shaders.TryGetValue( path, out var shader ) ) return shader;

		var file = path.Replace( '\\', '/' );
		if ( file.EndsWith( ".vfx", StringComparison.OrdinalIgnoreCase ) ) file = file[..^4] + ".shader";

		// A material may name its shader's compiled file (materials/postprocess/standard_pass1.vmat)
		if ( file.EndsWith( ".shader_c", StringComparison.OrdinalIgnoreCase ) ) file = file[..^2];
		if ( !file.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) file += ".shader";
		if ( !file.Contains( '/' ) ) file = "shaders/" + file;

		// Compute shaders name their path under shaders/ (postprocess/postprocess_bloom_cs)
		shader = drawer.Shaders.Load( file ) ?? (file.StartsWith( "shaders/", StringComparison.OrdinalIgnoreCase ) ? null : drawer.Shaders.Load( "shaders/" + file ));
		shaders[path] = shader;
		if ( shader is not null ) AddNames( shader );
		return shader;
	}

	ShaderFile Shader( string path ) => shaders.TryGetValue( path, out var shader ) ? shader : shaders[path] = AddNames( drawer.Shaders.Load( path ) );

	/// <summary>
	/// A shader compiled while running isn't under this device's content (the engine links it into GAME, or keeps an
	/// anonymous one in memory): take its bytes by the name its materials give, and forget what materials resolved to before.
	/// </summary>
	public override void ShaderCompiled( string path, byte[] compiled )
	{
		try
		{
			if ( services.Shaders.Add( path, compiled ) ) shaders.Clear();
		}
		catch ( Exception e ) when ( e is System.IO.InvalidDataException or System.IO.IOException )
		{
			Log.Warning( $"Borealis.Gpu: {path} compiled, but doesn't load here: {e.Message}" );
		}
	}

	/// <summary>Learn the attribute and combo names a shader reads, so tokens can be turned back into them.</summary>
	ShaderFile AddNames( ShaderFile shader )
	{
		foreach ( var type in (ProgramType[])[ProgramType.Features, ProgramType.Vertex, ProgramType.Pixel, ProgramType.Compute] )
		{
			if ( shader[type] is not { } program ) continue;
			foreach ( var variable in program.Variables )
			{
				if ( !string.IsNullOrEmpty( variable.Name ) ) names.TryAdd( new StringToken( variable.Name ).Value, variable.Name );
				if ( !string.IsNullOrEmpty( variable.Attribute ) ) names.TryAdd( new StringToken( variable.Attribute ).Value, variable.Attribute );
			}

			foreach ( var combo in program.DynamicCombos ) names.TryAdd( new StringToken( combo.Name ).Value, combo.Name );
		}

		return shader;
	}

	string Name( uint token ) => names.TryGetValue( token, out var name ) ? name : StringToken.GetValue( token );

	/// <summary>Lower managed attributes into what <see cref="ShaderDrawer"/> binds by name.</summary>
	ShaderAttributes Lower( RenderAttributes attributes )
	{
		lowered.Clear();

		// A native set that keeps a managed copy (a custom or dynamic object's) draws with the copy
		if ( attributes?.Managed is null && attributes?.ShadowSet is { } copy ) attributes = copy;
		if ( attributes?.Managed is null )
			throw new NotSupportedException( "Native render attributes can't be drawn with on Borealis.Gpu" );

		// A lent frame's transforms under everything, then a draw's attributes over the block's: a post process's blit reads
		// the frame grab its command list set on the block
		if ( lentTransforms.IsValid ) lowered.Set( "g_TransformBuffer", lentTransforms );
		if ( blockAttributes?.Managed is { } block && !ReferenceEquals( block, attributes.Managed ) ) Lower( block );
		Lower( attributes.Managed );
		return lowered;
	}

	void Lower( ManagedRenderAttributes managed )
	{
		foreach ( var (token, value) in managed.Values )
		{
			if ( Name( token ) is not { } name ) continue;
			switch ( value )
			{
				case float[] floats:
					lowered.Set( name, floats );
					break;
				case ManagedRenderAttributes.TextureValue { Texture: { } texture, Mip: < 0 }:
					lowered.Set( name, TextureOf( texture ) );
					break;
				case ManagedRenderAttributes.TextureValue { Texture: { } texture, Mip: var mip }:
					lowered.Set( name, context.View( TextureOf( texture ), mip ) );
					break;
				case GpuBuffer buffer:
					LowerBuffer( name, buffer );
					break;
				case byte[] data:
					lowered.SetData( name, data );
					break;
			}
		}

		foreach ( var (token, value) in managed.Combos )
		{
			if ( Name( token ) is { } name ) lowered.SetCombo( name, value );
		}
	}

	ShaderFile Require( Material material ) => ShaderOf( material ) ?? throw new NotSupportedException( $"{material} has no compiled shader Borealis.Gpu can load" );

	/// <summary>
	/// The shader a draw with <paramref name="material"/> draws with, and the material's compiled file as the drawer's
	/// material (<see cref="ShaderDrawer.Material"/>: its features, constants and textures), as native binds a material's mode.
	/// A copy reads its original's file and a material made in code its shader's defaults (<see cref="Material.MadeFromShader"/>),
	/// with what code set on it (<see cref="Material.SetInCode"/>) over them.
	/// </summary>
	ShaderFile Prepare( Material material )
	{
		// The shader by the material's name, which also learns the names its attributes lower to; the file's values only
		// when it names the same shader
		var shader = Require( material );
		var values = MaterialValues ? MaterialFile( material ) : null;
		drawer.Material = values is not null && ReferenceEquals( values.Shader, shader ) ? values : null;
		return shader;
	}

	/// <summary>Draw with materials' compiled values (the default); off draws their shaders' defaults, for comparisons.</summary>
	internal static bool MaterialValues { get; set; } = true;

	// A material's file as the drawer takes it, and with what code set on it, the version of those sets it has
	sealed class MaterialBacking
	{
		public BackendMaterial File;
		public int Version;
	}

	readonly ConditionalWeakTable<Material, MaterialBacking> materialFiles = new();

	BackendMaterial MaterialFile( Material material )
	{
		if ( material is null ) return null;
		if ( materialFiles.TryGetValue( material, out var known ) )
		{
			// Code set more on it since: its own copy of the file takes the new values
			if ( known.Version != material.SetInCodeVersion && known.File is not null && material.SetInCode is { } changed )
			{
				var (values, textures) = LowerSetInCode( changed );
				services.SetOnMaterial( known.File, values, textures );
				known.Version = material.SetInCodeVersion;
			}

			return known.File;
		}

		// A copy's path is its original's file (FilePath); as the .shader draws it, also where a surface stands in for its shader in
		// the scene. One with values set in code is a file of its own with them over the file's; one made in code from a shader
		// is that shader's defaults with them over
		var path = material.FilePath;
		BackendMaterial file = null;
		if ( material.MadeFromShader is { } shader )
		{
			file = services.CreateMaterial( material.Name ?? path ?? shader, ShaderPath( shader ) ) as BackendMaterial;
			if ( file?.Shader is null ) file = null;
			else if ( material.SetInCode is { Count: > 0 } set )
			{
				var (values, textures) = LowerSetInCode( set );
				services.SetOnMaterial( file, values, textures );
			}
		}
		else if ( !string.IsNullOrEmpty( path ) && files.Find( path ) is not null )
		{
			if ( material.SetInCode is { Count: > 0 } set )
			{
				var (values, textures) = LowerSetInCode( set );
				file = services.LoadMaterial( path, values, textures, $"{path} (set in code)" ) as BackendMaterial;
			}
			else file = services.ShaderMaterial( path );
		}

		materialFiles.Add( material, new MaterialBacking { File = file, Version = material.SetInCodeVersion } );
		return file;
	}

	/// <summary>A shader as a material names it (<c>complex.shader</c>, or with its folder), as its compiled file's path.</summary>
	static string ShaderPath( string shader )
	{
		shader = shader.Replace( '\\', '/' );
		if ( shader.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ) shader = shader[..^2];
		if ( !shader.EndsWith( ".shader", StringComparison.OrdinalIgnoreCase ) ) shader += ".shader";
		return shader.Contains( '/' ) ? shader : $"shaders/{shader}";
	}

	/// <summary>Values set on a material in code as shader variables: vectors, and textures as this device's.</summary>
	(Dictionary<string, float[]> Values, Dictionary<string, Borealis.GpuTexture> Textures) LowerSetInCode( IReadOnlyDictionary<string, object> set )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase );
		var textures = new Dictionary<string, Borealis.GpuTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (name, value) in set )
		{
			if ( value is Vector4 v ) values[name] = [v.x, v.y, v.z, v.w];
			else if ( value is Texture { IsValid: true } texture ) textures[name] = BorealisViewOf( texture );
		}

		return (values, textures);
	}

	/// <summary>Called on the main thread with no block open: work that runs at once on Source 2's device gets a block of its own.</summary>
	bool OutsideBlock => list is null && !Graphics.IsActive && ThreadSafe.IsMainThread;

	GpuCommandList Recording => list ?? throw new InvalidOperationException( "Borealis.Gpu draws need a render block (Graphics.Scope.CreateOn)." );

	//
	// Targets and viewport
	//

	public override RenderViewport GetViewport()
	{
		var v = context.Viewport;
		return new RenderViewport( new Rect( v.X, v.Y, v.Z, v.W ), 0, 1 );
	}

	public override void SetViewport( in RenderViewport viewport ) => SetViewport( viewport.Rect );

	public override void SetViewport( Rect rect ) => context.Viewport = new Num.Vector4( rect.Left, rect.Top, rect.Width, rect.Height );

	public override void BindRenderTarget( RenderTarget target ) => BindRenderTarget( target, false );

	/// <summary>
	/// Bind <paramref name="target"/>, its colour written through its sRGB view when <paramref name="srgbWrite"/> (and its
	/// format has one), as Source 2 writes an 8-bit texture a camera renders into.
	/// </summary>
	internal void BindRenderTarget( RenderTarget target, bool srgbWrite ) => BindRenderTarget( target, srgbWrite, 0 );

	/// <summary>
	/// <see cref="BindRenderTarget(RenderTarget, bool)"/>, drawing into layer <paramref name="depthSlice"/> of its depth. A target
	/// with no colour draws into its depth alone (a shadow map's slice).
	/// </summary>
	internal void BindRenderTarget( RenderTarget target, bool srgbWrite, int depthSlice )
	{
		_ = Recording;
		context.SetTarget( target.ColorTarget is { } color ? TextureOf( color ) : default, target.DepthTarget is { } depth ? TextureOf( depth ) : default, srgbWrite, depthSlice );
	}

	public override void Clear( Color color, bool clearColor, bool clearDepth, bool clearStencil )
	{
		_ = Recording;
		if ( clearColor ) context.Clear( new Num.Vector4( color.r, color.g, color.b, color.a ) );
		if ( clearDepth || clearStencil ) context.ClearDepth();
	}

	public override void ClearTexture( Texture texture, Color color )
	{
		_ = Recording;
		context.ClearTexture( TextureOf( texture ), new Num.Vector4( color.r, color.g, color.b, color.a ) );
	}

	//
	// Draws
	//

	static PrimitiveTopology Topology( Graphics.PrimitiveType type ) => type switch
	{
		Graphics.PrimitiveType.Points => PrimitiveTopology.PointList,
		Graphics.PrimitiveType.Lines => PrimitiveTopology.LineList,
		Graphics.PrimitiveType.LineStrip => PrimitiveTopology.LineStrip,
		Graphics.PrimitiveType.Triangles => PrimitiveTopology.TriangleList,
		Graphics.PrimitiveType.TriangleStrip => PrimitiveTopology.TriangleStrip,
		_ => throw new NotSupportedException( $"{type} on Borealis.Gpu" ),
	};

	public override unsafe void DrawVertices( Type vertexType, int vertexSize, IntPtr vertices, int vertexCount, IntPtr indices, int indexCount, Material material, RenderAttributes attributes, Graphics.PrimitiveType primitiveType )
	{
		_ = Recording;
		var layout = LayoutOf( vertexType );
		if ( layout.Stride > vertexSize ) throw new ArgumentException( $"{vertexType} vertex layout is larger than the vertex type" );

		var shader = Prepare( material );
		var bytes = new ReadOnlySpan<byte>( (void*)vertices, vertexSize * vertexCount );
		// CPU vertices hold plain normals and tangents, as a native view tells its draws (UncompressedTangentFrame)
		var lowered = Lower( attributes );
		if ( lowered.Get( UncompressedTangentFrame ) is null ) lowered.Set( UncompressedTangentFrame, 1.0f );
		if ( indices == IntPtr.Zero )
		{
			context.DrawVertices( shader, lowered, bytes, layout, vertexCount, Topology( primitiveType ) );
			return;
		}

		// Indexed: the vertices and 16-bit indices in this frame's upload memory
		var vertexUpload = list.AllocateUpload( bytes.Length, 16 );
		bytes.CopyTo( vertexUpload.Data );
		var indexBytes = new ReadOnlySpan<byte>( (void*)indices, indexCount * sizeof( ushort ) );
		var indexUpload = list.AllocateUpload( indexBytes.Length, 16 );
		indexBytes.CopyTo( indexUpload.Data );
		context.Draw( shader, lowered, new GeometryDraw
		{
			Vertices = vertexUpload.Buffer,
			VertexOffset = vertexUpload.Offset,
			Layout = layout,
			Indices = indexUpload.Buffer,
			IndexOffset = indexUpload.Offset,
			IndexFormat = IndexFormat.UInt16,
			Topology = Topology( primitiveType ),
			Count = indexCount,
		} );
	}

	public override bool SetRenderState( Material material, RenderAttributes attributes, Type vertexType )
	{
		_ = Recording;
		pendingLayout = vertexType is null ? null : LayoutOf( vertexType );
		pendingMaterial = material;
		pendingShader = Prepare( material );
		pendingAttributes = attributes;
		return true;
	}

	public override void DrawIndexedInstanced( GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int instanceCount ) => DrawPending( new GeometryDraw
	{
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		Count = indexBuffer.ElementCount,
		Instances = instanceCount,
	} );

	public override void Draw( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, int startVertex, int vertexCount ) => DrawPending( new GeometryDraw
	{
		Vertices = BufferOf( vertexBuffer ),
		Layout = pendingLayout,
		Topology = Topology( primitiveType ),
		First = startVertex,
		Count = vertexCount,
	} );

	public override void DrawIndexed( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, int startIndex, int indexCount ) => DrawPending( new GeometryDraw
	{
		Vertices = BufferOf( vertexBuffer ),
		Layout = pendingLayout,
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		First = startIndex,
		Count = indexCount,
	} );

	public override void DrawInstancedIndirect( GpuBuffer vertexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => DrawPending( new GeometryDraw
	{
		Vertices = vertexBuffer is null ? default : BufferOf( vertexBuffer ),
		Layout = vertexBuffer is null ? null : pendingLayout,
		Topology = Topology( primitiveType ),
		Arguments = BufferOf( indirectBuffer ),
		ArgumentsOffset = bufferOffset,
		DrawCount = (int)drawCount,
		Stride = (int)stride,
	} );

	public override void DrawIndexedInstancedIndirect( GpuBuffer vertexBuffer, GpuBuffer indexBuffer, Graphics.PrimitiveType primitiveType, GpuBuffer indirectBuffer, uint bufferOffset, uint drawCount, uint stride ) => DrawPending( new GeometryDraw
	{
		Vertices = vertexBuffer is null ? default : BufferOf( vertexBuffer ),
		Layout = vertexBuffer is null ? null : pendingLayout,
		Indices = BufferOf( indexBuffer ),
		IndexFormat = IndexFormatOf( indexBuffer ),
		Topology = Topology( primitiveType ),
		Arguments = BufferOf( indirectBuffer ),
		ArgumentsOffset = bufferOffset,
		DrawCount = (int)drawCount,
		Stride = (int)stride,
	} );

	//
	// Models
	//

	/// <summary>
	/// A model at a LOD (0 with none), an instance per transform: the transforms written as native writes them into the
	/// frame's transform buffer, which the model's shaders read through their instance ids.
	/// </summary>
	public override void DrawModel( Model model, ReadOnlySpan<Transform> transforms, int? lodLevel, RenderAttributes attributes )
	{
		_ = Recording;
		if ( transforms.IsEmpty ) return;
		var geometry = GeometryOf( model, lodLevel ?? 0 );
		if ( geometry.Draws.Length == 0 ) return;

		var (buffer, ids) = UploadTransforms( transforms );
		DrawModelGeometry( geometry, attributes, transforms.Length, transforms: buffer, ids: ids );
	}

	/// <summary>
	/// <paramref name="count"/> instances of each of the model's LOD 0 draw calls, every mesh group, with its material's
	/// shader: what the terrain clipmap draws its meshlets with, its shaders reading <c>SV_InstanceID</c>.
	/// </summary>
	public override void DrawModel( Model model, int count, RenderAttributes attributes )
	{
		_ = Recording;
		DrawModelGeometry( GeometryOf( model, 0 ), attributes, count );
	}

	/// <summary>The model's LOD 0 draw calls, each drawn with its indirect arguments, its shaders reading <c>SV_InstanceID</c>.</summary>
	public override void DrawModelIndirect( Model model, GpuBuffer arguments, int argumentsOffset, RenderAttributes attributes )
	{
		_ = Recording;
		DrawModelGeometry( GeometryOf( model, 0 ), attributes, 1, BufferOf( arguments ), argumentsOffset );
	}

	/// <summary>
	/// The model's draw calls at a LOD, each drawn with its indirect arguments, instance i reading transform i of
	/// <paramref name="transforms"/> as <c>g_TransformBuffer</c>.
	/// </summary>
	public override void DrawModelIndirect( Model model, GpuBuffer transforms, GpuBuffer arguments, int argumentsOffset, int lodLevel, RenderAttributes attributes )
	{
		_ = Recording;
		var geometry = GeometryOf( model, lodLevel );
		if ( geometry.Draws.Length == 0 ) return;
		DrawModelGeometry( geometry, attributes, 1, BufferOf( arguments ), argumentsOffset, BufferOf( transforms ), InstanceIds( 0, transforms.ElementCount ) );
	}

	//
	// Copies, grabs and mips
	//

	public override void CopyTexture( Texture source, Texture destination, int sourceMip, int sourceSlice, int destinationMip, int destinationSlice )
	{
		_ = Recording;
		var from = TextureOf( source );
		var to = TextureOf( destination );
		if ( sourceMip == destinationMip && sourceSlice == 0 && destinationSlice == 0 )
		{
			context.CopyTexture( from, to, sourceMip );
			return;
		}

		// Engine textures' copies here are made with one layer
		if ( sourceSlice >= Math.Max( gpu.InfoOf( from ).Layers, 1 ) || destinationSlice >= Math.Max( gpu.InfoOf( to ).Layers, 1 ) )
			throw new NotSupportedException( "Copies between array slices on Borealis.Gpu: engine textures are made with one layer there" );
		context.CopyTexture( from, sourceMip, sourceSlice, to, destinationMip, destinationSlice );
	}

	public override void ResolveColor( Texture destination, Rect viewport )
	{
		_ = Recording;
		var saved = context.Viewport;
		SetViewport( viewport );
		context.CopyTarget( TextureOf( destination ) );
		context.Viewport = saved;
	}

	/// <summary>
	/// Copy the viewport's part of the bound depth into <paramref name="destination"/>, as native resolves it. Multisampled depth
	/// into a single-sampled texture isn't done yet.
	/// </summary>
	public override void ResolveDepth( Texture destination, Rect viewport )
	{
		_ = Recording;
		var saved = context.Viewport;
		SetViewport( viewport );
		var copied = context.CopyDepth( TextureOf( destination ) );
		context.Viewport = saved;
		if ( !copied ) throw new NotSupportedException( "Resolving multisampled depth on Borealis.Gpu" );
	}

	public override void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method, int initialMip )
	{
		_ = Recording;
		switch ( method )
		{
			// Gaussian and GGX: downsample_cs, a mip a pass, its kernel by the method
			case Graphics.DownsampleMethod.GaussianBlur or Graphics.DownsampleMethod.GaussianBorder or Graphics.DownsampleMethod.GGX:
				context.GenerateMips( TextureOf( texture ), Shader( "shaders/downsample_cs.shader" ), (int)method, initialMip );
				break;

			// Box, min, max and min-max: downsample_spd, six mips a pass, as Source 2 runs them
			default:
				context.DownsampleSpd( TextureOf( texture ), Shader( "shaders/downsample_spd.shader" ), (int)method, initialMip );
				break;
		}
	}

	//
	// Compute
	//

	public override void Dispatch( Material computeMaterial, RenderAttributes attributes, int threadsX, int threadsY, int threadsZ )
	{
		if ( OutsideBlock )
		{
			// Outside a render block a dispatch runs now, as on Source 2's device: in a block of its own (gizmo text from layout)
			using ( Graphics.Scope.CreateOn( this ) ) Dispatch( computeMaterial, attributes, threadsX, threadsY, threadsZ );
			return;
		}

		_ = Recording;
		drawer.Material = null;
		if ( !context.Dispatch( Require( computeMaterial ), Lower( attributes ), threadsX, threadsY, threadsZ ) )
			throw new InvalidOperationException( $"{computeMaterial} didn't dispatch on Borealis.Gpu" );
	}

	public override void DispatchIndirect( Material computeMaterial, RenderAttributes attributes, GpuBuffer indirectBuffer, uint bufferOffset )
	{
		if ( OutsideBlock )
		{
			using ( Graphics.Scope.CreateOn( this ) ) DispatchIndirect( computeMaterial, attributes, indirectBuffer, bufferOffset );
			return;
		}

		_ = Recording;
		drawer.Material = null;
		if ( !context.DispatchIndirect( Require( computeMaterial ), Lower( attributes ), BufferOf( indirectBuffer ), bufferOffset ) )
			throw new InvalidOperationException( $"{computeMaterial} didn't dispatch on Borealis.Gpu" );
	}

	/// <summary>
	/// Build a view's depth chain from <paramref name="depth"/> into <paramref name="chain"/> (RG32F with mips, made for
	/// compute) in the block being recorded, as Borealis's depth chain layer builds one on Source 2.
	/// </summary>
	internal void BuildDepthChain( Texture depth, Texture chain )
	{
		_ = Recording;
		context.BuildDepthChain( TextureOf( depth ), TextureOf( chain ), Shader( "shaders/downsample_spd.shader" ) );
	}

	//
	// Auto exposure
	//

	/// <summary>Auto exposure on this device: native's tonemap system, ported (<see cref="AutoExposure"/>).</summary>
	internal AutoExposure CreateExposure() => new( gpu );

	/// <summary>A camera's tonemap settings (<see cref="TonemapSystem"/>) as <see cref="AutoExposure"/> takes them.</summary>
	internal static AutoExposure.Settings Settings( in SceneTonemapParameters_t p ) => new()
	{
		Min = p.m_flAutoExposureMin,
		Max = p.m_flAutoExposureMax,
		Compensation = p.m_flExposureCompensationScalar,
		PercentTarget = p.m_flTonemapPercentTarget,
		PercentBrightPixels = p.m_flTonemapPercentBrightPixels,
		MinAverageLuminance = p.m_flTonemapMinAvgLum,
		Rate = p.m_flRate,
		AccelerateDown = p.m_flAccelerateExposureDown,
		SpeedUp = p.m_flExposureAdaptationSpeedUp,
		SpeedDown = p.m_flExposureAdaptationSpeedDown,
		EvSmoothingRange = p.m_flTonemapEVSmoothingRange,
	};

	/// <summary>
	/// Count this frame's luminance bucket over <paramref name="viewport"/> of <paramref name="color"/> (the frame's HDR
	/// colour, before tonemapping) in the block being recorded, as <c>RenderTools.RenderAutoExposure</c> does on Source 2.
	/// The bound colour target sets the size counted in and isn't drawn to.
	/// </summary>
	internal void CountExposure( AutoExposure exposure, Texture color, Rect viewport )
	{
		_ = Recording;
		exposure.Draw( context, Shader( "shaders/tonemap_query.shader" ), TextureOf( color ), (int)viewport.Left, (int)viewport.Top, (int)viewport.Width, (int)viewport.Height );
	}

	//
	// Barriers: layouts are tracked, so a transition only orders memory
	//

	public override void ResourceBarrierTransition( Texture texture, ResourceState state, int mip ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state ) => Barrier();
	public override void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after ) => Barrier();
	public override void UavBarrier( Texture texture ) => Barrier();
	public override void UavBarrier( GpuBuffer buffer ) => Barrier();

	void Barrier()
	{
		_ = Recording;
		context.MemoryBarrier();
	}

	//
	// Buffers
	//

	public override unsafe void SetBufferData( GpuBuffer buffer, IntPtr data, int size, int offset )
	{
		var handle = BufferOf( buffer );
		var bytes = new ReadOnlySpan<byte>( (void*)data, size );

		// In a block, in order with its draws
		if ( list is not null )
		{
			context.UpdateBuffer( handle, bytes, offset );
			return;
		}

		// Outside one - a recording uploading its tables - copied once, into the upload memory of a list of their own that's
		// submitted before the next block, which is what draws with them: writing now would submit and wait for each
		if ( uploads is null )
		{
			FollowFrame();
			uploads = gpu.BeginCommandList( QueueType.Graphics, "Engine uploads" );
			written.Clear();
		}

		// A second write to a buffer waits for the first, in case they overlap
		if ( !written.Add( handle ) )
		{
			uploads.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
			written.Clear();
			written.Add( handle );
		}

		uploads.UpdateBuffer( handle, bytes, offset );
		UploadsRecorded++;
	}

	GpuCommandList uploads;
	readonly HashSet<BufferHandle> written = new();

	/// <summary>Buffer writes recorded outside blocks, for tests.</summary>
	public int UploadsRecorded { get; private set; }

	/// <summary>
	/// Submit the buffer writes made outside a block, visible to everything after them: before a block, a device frame,
	/// a readback, or freeing a buffer they may write.
	/// </summary>
	void SubmitUploads()
	{
		if ( uploads is null ) return;
		uploads.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		SubmittedReadbacks( uploadReadbacks, gpu.Submit( uploads ) );
		uploads = null;
	}

	/// <summary>Read a buffer's backing, after everything submitted so far. Not inside a block, which hasn't been submitted.</summary>
	public override void ReadBuffer( GpuBuffer buffer, Span<byte> data, int offset )
	{
		if ( list is not null ) throw new NotSupportedException( "Buffer reads inside a render block on Borealis.Gpu" );
		if ( data.IsEmpty ) return;

		var handle = BufferOf( buffer );
		SubmitUploads();
		var readback = gpu.CreateBuffer( new BufferInfo { Size = data.Length, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			copy.Barrier( PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferRead );
			copy.CopyBuffer( handle, offset, readback, 0, data.Length );
			copy.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.Host, MemoryAccess.HostRead );
			gpu.Wait( gpu.Submit( copy ) );
			gpu.Map( readback )[..data.Length].CopyTo( data );
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	//
	// Readback
	//

	/// <summary>
	/// A mip of a texture's backing as bytes, in its Borealis.Gpu format, after everything submitted so far.
	/// </summary>
	public byte[] ReadBytes( Texture texture, int mip = 0 )
	{
		var handle = TextureOf( texture );
		SubmitUploads();
		var info = gpu.InfoOf( handle );
		var size = Math.Max( info.Width >> mip, 1 ) * Math.Max( info.Height >> mip, 1 ) * info.Format.BytesPerBlock();
		var readback = gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback, mip );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );
			return gpu.Map( readback )[..size].ToArray();
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	/// <summary>
	/// A rectangle of a texture's backing (one mip and array layer), after everything submitted so far, converted on the
	/// CPU into the engine format asked for. Copies of the same size only, not depth or 3D textures, and not inside a
	/// block, which hasn't been submitted.
	/// </summary>
	public override unsafe bool ReadPixels( Texture texture, NativeRect source, int slice, int mip, NativeRect destination, IntPtr data, ImageFormat format, int stride )
	{
		// With Source 2's device kept (the editor): what native drew (a bake's captures) is read from its copy, and so are
		// volumes, which aren't read back here (a DDGI bake's relocation, made on the CPU)
		if ( Graphics.HasSource2Device && (texture.PixelsOnSource2 || (texture.Depth > 1 && !texture.Desc.IsCube)) )
			return base.ReadPixels( texture, source, slice, mip, destination, data, format, stride );

		if ( list is not null ) return false;
		if ( source.w != destination.w || source.h != destination.h || source.w <= 0 || source.h <= 0 ) return false;

		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		if ( mip < 0 || mip >= info.Mips || slice < 0 || slice >= Math.Max( info.Layers, 1 ) || info.Format.IsDepth() || info.Dimension == TextureDimension.Texture3D ) return false;
		if ( ImageFormatOf( info.Format ) is not { } from ) return false;

		int width = Math.Max( info.Width >> mip, 1 ), height = Math.Max( info.Height >> mip, 1 );
		if ( source.x < 0 || source.y < 0 || source.x + source.w > width || source.y + source.h > height ) return false;

		var pixel = NativeEngine.ImageLoader.GetMemRequired( 1, 1, 1, 1, format );
		if ( pixel <= 0 ) return false;
		stride = stride > 0 ? stride : destination.w * pixel;

		var size = info.Format.RowPitch( width ) * info.Format.Rows( height );
		var readback = gpu.CreateBuffer( new BufferInfo { Size = size, Memory = MemoryLocation.Readback, Name = "Readback" } );
		var converted = new byte[width * height * pixel];
		try
		{
			SubmitUploads();
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback, mip, slice );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );

			var bytes = gpu.Map( readback );
			fixed ( byte* src = bytes )
			fixed ( byte* dst = converted )
			{
				if ( !NativeEngine.ImageLoader.ConvertImageFormat( (IntPtr)src, from, (IntPtr)dst, format, width, height, 0, width * pixel ) ) return false;
			}
		}
		finally
		{
			gpu.Destroy( readback );
		}

		var row = source.w * pixel;
		for ( int y = 0; y < source.h; y++ )
		{
			var line = converted.AsSpan( ((source.y + y) * width + source.x) * pixel, row );
			line.CopyTo( new Span<byte>( (byte*)data + (long)(destination.y + y) * stride + (long)destination.x * pixel, row ) );
		}

		return true;
	}

	/// <summary>The engine form of a backing's format, for converting what's read back, or null.</summary>
	static ImageFormat? ImageFormatOf( GpuFormat format ) => format.Linear() switch
	{
		GpuFormat.RGBA8Unorm => ImageFormat.RGBA8888,
		GpuFormat.BGRA8Unorm => ImageFormat.BGRA8888,
		GpuFormat.R8Unorm => ImageFormat.I8,
		GpuFormat.RG8Unorm => ImageFormat.IA88,
		GpuFormat.BGR10A2Unorm => ImageFormat.BGRA1010102,
		GpuFormat.RG32Uint => ImageFormat.RG3232,
		GpuFormat.RGBA32Uint => ImageFormat.RGBA32323232,
		GpuFormat.RGBA16Float => ImageFormat.RGBA16161616F,
		GpuFormat.RGBA16Unorm => ImageFormat.RGBA16161616,
		GpuFormat.RG16Float => ImageFormat.RG1616F,
		GpuFormat.R16Float => ImageFormat.R16F,
		GpuFormat.R16Unorm => ImageFormat.R16,
		GpuFormat.RG16Unorm => ImageFormat.RG1616,
		GpuFormat.R32Float => ImageFormat.R32F,
		GpuFormat.RG32Float => ImageFormat.RG3232F,
		GpuFormat.RGBA32Float => ImageFormat.RGBA32323232F,
		GpuFormat.R32Uint => ImageFormat.R32_UINT,
		GpuFormat.RGB10A2Unorm => ImageFormat.RGBA1010102,
		GpuFormat.RG11B10Float => ImageFormat.R11G11B10F,
		GpuFormat.BC1Unorm => ImageFormat.DXT1,
		GpuFormat.BC3Unorm => ImageFormat.DXT5,
		GpuFormat.BC4Unorm => ImageFormat.ATI1N,
		GpuFormat.BC5Unorm => ImageFormat.ATI2N,
		GpuFormat.BC6HUfloat => ImageFormat.BC6H,
		GpuFormat.BC7Unorm => ImageFormat.BC7,
		_ => null,
	};

	/// <summary>
	/// Mip 0 of an RGBA8 texture's backing, after everything submitted so far: what the device drew into it.
	/// </summary>
	public Color32[] ReadPixels( Texture texture )
	{
		var handle = TextureOf( texture );
		var info = gpu.InfoOf( handle );
		if ( info.Format is not (GpuFormat.RGBA8Unorm or GpuFormat.RGBA8Srgb) ) throw new NotSupportedException( $"Reading {info.Format} back" );

		SubmitUploads();
		var readback = gpu.CreateBuffer( new BufferInfo { Size = info.Width * info.Height * 4, Memory = MemoryLocation.Readback, Name = "Readback" } );
		try
		{
			var copy = gpu.BeginCommandList( QueueType.Graphics, "Readback" );
			context.Begin( copy );
			context.CopyToBuffer( handle, readback );
			context.Finish();
			gpu.Wait( gpu.Submit( copy ) );

			var bytes = gpu.Map( readback );
			var pixels = new Color32[info.Width * info.Height];
			bytes[..(pixels.Length * 4)].CopyTo( MemoryMarshal.AsBytes( pixels.AsSpan() ) );
			return pixels;
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}

	/// <summary>
	/// Start the device's next frame: what the frames before the last one used is recycled, and destroyed backings freed.
	/// </summary>
	public void NextFrame()
	{
		SubmitUploads();
		services.BeginFrame();
		PollReadbacks();
	}

	/// <summary>The renderer services' frame, which every device frame moves on: GPU-driven drawing recycles its per-frame memory by it.</summary>
	internal uint ServicesFrame => services.GpuDrivenFrame;

	public void Dispose()
	{
		Unregister( this );
		SubmitUploads();
		gpu.WaitIdle();
		DestroyReadbacks();

		foreach ( var handle in textureOwners )
		{
			context.Forget( handle );
			gpu.Destroy( handle );
		}
		foreach ( var handle in ownedBuffers ) gpu.Destroy( handle );
		if ( errorTexture.IsValid )
		{
			context.Forget( errorTexture );
			gpu.Destroy( errorTexture );
			errorTexture = default;
		}
		DestroyModelGeometry();
		if ( services.PassTimer == busyTimer ) services.PassTimer = null;
		busyTimer?.Dispose();
		busyTimer = null;
		gpu.Destroy( pipelineTextures );
		foreach ( var package in mapVpks ) package.Dispose();
		mapVpks.Clear();
		foreach ( var (sampler, _) in samplers.Values ) gpu.Destroy( sampler );
		textureOwners.Clear();
		ownedBuffers.Clear();
		samplers.Clear();
		textures.Clear();
		buffers.Clear();

		context.Release();
		drawer.Dispose();
		services.Dispose();
		if ( ownsDevice ) gpu.Dispose();
	}
}
